using Microsoft.Extensions.Logging;
using Npgsql;

namespace HVTradingBot.Infrastructure.Persistence;

/// <summary>
/// Makes sure only one trading worker runs per database: two workers on the same database and broker account would
/// place duplicate orders. Holds a PostgreSQL session-level advisory lock on a dedicated, unpooled connection for the
/// life of the process. PostgreSQL releases the lock when the connection ends, so a crashed worker never blocks the
/// next one. Advisory locks are per database, so a worker on another database (e.g. ./run.sh dev) is not affected.
/// </summary>
public sealed class WorkerInstanceLock(string connectionString, ILogger<WorkerInstanceLock> logger) : IWorkerInstanceLock, IAsyncDisposable
{
    /// <summary>Advisory lock key shared by all worker versions ("HVTB" + 1). Never change it.</summary>
    public const long LockKey = 0x4856_5442_0000_0001;

    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(10);

    // Unpooled: closing the connection must end the session, which is what releases the lock.
    private readonly string _connectionString =
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, ApplicationName = "HVTradingBot.Worker lock" }.ConnectionString;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile NpgsqlConnection? _connection;

    /// <summary>True once this process holds the lock.</summary>
    public bool IsAcquired => _connection is not null;

    /// <summary>Tries once to take the lock. Returns false while another worker holds it.</summary>
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return true;
            }

            var connection = new NpgsqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                if (await command.ExecuteScalarAsync(cancellationToken) is true)
                {
                    _connection = connection;
                    return true;
                }
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            await connection.DisposeAsync();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Waits until the lock is taken, retrying every <paramref name="retryInterval"/>. Logs a warning once while
    /// another worker holds it, so the process stays alive (no restart loop) and takes over when the other one stops.
    /// </summary>
    public async Task AcquireAsync(TimeSpan retryInterval, CancellationToken cancellationToken)
    {
        var warned = false;
        while (!await TryAcquireAsync(cancellationToken))
        {
            if (!warned)
            {
                logger.LogWarning(
                    "Another trading worker is already running on this database; waiting for it to stop (retrying every {RetrySeconds} s)",
                    retryInterval.TotalSeconds);
                warned = true;
            }

            await Task.Delay(retryInterval, cancellationToken);
        }

        logger.LogInformation(warned
            ? "The other trading worker stopped; this worker now holds the single-instance lock"
            : "Acquired the trading worker single-instance lock");
    }

    /// <summary>
    /// Checks that the lock is still held. A session-level advisory lock lives as long as its connection, so a lost
    /// connection (e.g. PostgreSQL restarted) means the lock is gone and another worker may already hold it. Returns
    /// false, and forgets the dead connection, when the lock is no longer held.
    /// </summary>
    public async Task<bool> IsStillHeldAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not { } connection)
            {
                return false;
            }

            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted " +
                    "AND ((classid::bigint << 32) | objid::bigint) = @key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                if (await command.ExecuteScalarAsync(cancellationToken) is true)
                {
                    return true;
                }

                logger.LogCritical("The trading worker single-instance lock is no longer held by this worker");
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                logger.LogCritical(ex, "Lost the connection holding the trading worker single-instance lock");
            }

            _connection = null;
            await connection.DisposeAsync();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the lock by closing its connection.</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is { } connection)
            {
                _connection = null;
                await connection.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
