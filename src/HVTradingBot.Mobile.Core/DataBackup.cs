using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Mobile.Core;

/// <summary>What a backup holds, shown before restoring it.</summary>
public sealed record BackupInfo(DateTime CreatedAtUtc, long DatabaseBytes, int ClosedTrades, int Decisions, int Signals, string? LastMigration);

/// <summary>When this phone's data was last exported (null: never).</summary>
public sealed record BackupStatus(DateTime? LastExportUtc, BackupInfo? LastExport, long DatabaseBytes);

/// <summary>A backup that cannot be restored, with the reason for the user.</summary>
public sealed class BackupException(string message) : Exception(message);

/// <summary>
/// Everything the app knows lives in one SQLite file on the phone; this copies it to a file the user keeps elsewhere
/// (Google Drive, Downloads) and puts it back. A backup is a zip with a consistent copy of the database (made while
/// the engine runs) and a small manifest. The encryption keys are deliberately left out, so a backup file never
/// exposes the Deriv token or email password: restoring on the same phone keeps them working, on a new phone they are
/// entered again. Restoring first saves the current data next to the database, so nothing is ever lost.
/// </summary>
public sealed class DataBackup(MobileSettings settings)
{
    public const int FormatVersion = 1;
    private const string DatabaseEntry = "trading.db";
    private const string ManifestEntry = "backup.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Safety copies made before each restore (never deleted by the app).</summary>
    public string SafetyDirectory => Path.Combine(settings.DataDirectory, "backups");

    private string StatusPath => Path.Combine(settings.DataDirectory, "backup-status.json");

    private sealed record Manifest(int Format, string App, DateTime CreatedAtUtc, BackupInfo Info);

    public BackupStatus GetStatus()
    {
        BackupInfo? last = null;
        try
        {
            if (File.Exists(StatusPath))
            {
                last = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(StatusPath), Json);
            }
        }
        catch (JsonException)
        {
            // A damaged status file only means "unknown"; the next export rewrites it.
        }

        var size = File.Exists(settings.DatabasePath) ? new FileInfo(settings.DatabasePath).Length : 0;
        return new BackupStatus(last?.CreatedAtUtc, last, size);
    }

    /// <summary>Writes a backup of the current data to <paramref name="destination"/>. Safe while the engine runs.</summary>
    public async Task<BackupInfo> ExportAsync(Stream destination, CancellationToken cancellationToken, bool recordStatus = true)
    {
        Directory.CreateDirectory(SafetyDirectory);
        var copy = Path.Combine(SafetyDirectory, $"export-{Guid.NewGuid():N}.db");
        try
        {
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = settings.DatabasePath, Pooling = false }.ToString()))
            {
                await source.OpenAsync(cancellationToken);
                await using var vacuum = source.CreateCommand();
                // A transactionally consistent, compacted copy, also while the engine is writing.
                vacuum.CommandText = $"VACUUM INTO '{copy.Replace("'", "''")}'";
                await vacuum.ExecuteNonQueryAsync(cancellationToken);
            }

            var info = await DescribeAsync(copy, DateTime.UtcNow, cancellationToken);
            using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry(DatabaseEntry, CompressionLevel.Optimal);
                await using (var target = entry.Open())
                await using (var file = File.OpenRead(copy))
                {
                    await file.CopyToAsync(target, cancellationToken);
                }

                var manifest = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                await using var writer = manifest.Open();
                await JsonSerializer.SerializeAsync(writer, new Manifest(FormatVersion, "HVTradingBot", info.CreatedAtUtc, info), Json, cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            if (recordStatus)
            {
                await File.WriteAllTextAsync(StatusPath, JsonSerializer.Serialize(info, Json), cancellationToken);
            }

            return info;
        }
        finally
        {
            File.Delete(copy);
        }
    }

    /// <summary>Checks a backup and says what it holds, without changing anything.</summary>
    public async Task<BackupInfo> InspectAsync(Stream source, CancellationToken cancellationToken)
    {
        var staged = await StageAsync(source, cancellationToken);
        try
        {
            return staged.Info;
        }
        finally
        {
            File.Delete(staged.Path);
        }
    }

    /// <summary>
    /// Replaces this phone's data with the backup. The engine must be stopped. The current data is first saved to
    /// <see cref="SafetyDirectory"/>; the backup is fully checked before anything is replaced.
    /// </summary>
    public async Task<(BackupInfo Restored, string SafetyCopy)> RestoreAsync(Stream source, CancellationToken cancellationToken)
    {
        var staged = await StageAsync(source, cancellationToken);
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(SafetyDirectory);
            var safety = Path.Combine(SafetyDirectory, $"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            if (File.Exists(settings.DatabasePath))
            {
                await using var file = File.Create(safety);
                await ExportAsync(file, cancellationToken, recordStatus: false);
            }

            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                File.Delete(settings.DatabasePath + suffix);
            }

            File.Move(staged.Path, settings.DatabasePath, overwrite: true);
            return (staged.Info, safety);
        }
        finally
        {
            File.Delete(staged.Path);
        }
    }

    /// <summary>Extracts the database from a backup into the data folder and checks it.</summary>
    private async Task<(string Path, BackupInfo Info)> StageAsync(Stream source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SafetyDirectory);
        var path = Path.Combine(SafetyDirectory, $"restore-{Guid.NewGuid():N}.db");
        try
        {
            Manifest? manifest;
            try
            {
                using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                var manifestEntry = zip.GetEntry(ManifestEntry);
                var databaseEntry = zip.GetEntry(DatabaseEntry);
                if (manifestEntry is null || databaseEntry is null)
                {
                    throw new BackupException("This is not an HVTradingBot backup.");
                }

                await using (var reader = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<Manifest>(reader, Json, cancellationToken);
                }

                await using var entry = databaseEntry.Open();
                await using var file = File.Create(path);
                await entry.CopyToAsync(file, cancellationToken);
            }
            catch (InvalidDataException)
            {
                throw new BackupException("This file is not a backup (not a zip file).");
            }
            catch (JsonException)
            {
                throw new BackupException("The backup's description is damaged.");
            }

            if (manifest is not { App: "HVTradingBot" })
            {
                throw new BackupException("This is not an HVTradingBot backup.");
            }

            if (manifest.Format > FormatVersion)
            {
                throw new BackupException("This backup was made by a newer version of the app. Update the app first.");
            }

            var header = new byte[16];
            await using (var file = File.OpenRead(path))
            {
                await file.ReadExactlyAsync(header, cancellationToken);
            }

            if (Encoding.ASCII.GetString(header, 0, 15) != "SQLite format 3")
            {
                throw new BackupException("The database in this backup is damaged.");
            }

            await CheckAsync(path, cancellationToken);
            return (path, await DescribeAsync(path, manifest.CreatedAtUtc, cancellationToken));
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            throw;
        }
    }

    /// <summary>The database must be intact and must not need a newer app (a migration this app does not know).</summary>
    private static async Task CheckAsync(string path, CancellationToken cancellationToken)
    {
        await using (var connection = Open(path))
        {
            await connection.OpenAsync(cancellationToken);
            await using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            if (await check.ExecuteScalarAsync(cancellationToken) as string != "ok")
            {
                throw new BackupException("The database in this backup is damaged.");
            }
        }

        await using var db = Context(path);
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> applied;
        try
        {
            applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        }
        catch (SqliteException)
        {
            throw new BackupException("This backup has no HVTradingBot data.");
        }

        if (!applied.Any())
        {
            throw new BackupException("This backup has no HVTradingBot data.");
        }

        if (applied.FirstOrDefault(m => !known.Contains(m)) is { } unknown)
        {
            throw new BackupException($"This backup was made by a newer version of the app ({unknown}). Update the app first.");
        }
    }

    private static async Task<BackupInfo> DescribeAsync(string path, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        await using var db = Context(path);
        var closed = await db.Positions.CountAsync(p => !p.IsOpen, cancellationToken);
        var decisions = await db.TradeDecisions.CountAsync(cancellationToken);
        int signals;
        try
        {
            signals = await db.Signals.CountAsync(cancellationToken);
        }
        catch (SqliteException)
        {
            signals = 0; // a backup from before signals; the table is added when it is restored
        }

        var last = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        SqliteConnection.ClearAllPools();
        return new BackupInfo(createdAtUtc, new FileInfo(path).Length, closed, decisions, signals, last);
    }

    private static SqliteConnection Open(string path) =>
        new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());

    private static TradingDbContext Context(string path)
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>();
        DatabaseSetup.Configure(options, DatabaseProvider.Sqlite,
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        return new TradingDbContext(options.Options);
    }
}
