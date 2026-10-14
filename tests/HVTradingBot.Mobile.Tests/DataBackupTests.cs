using System.IO.Compression;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Mobile.Core;
using Microsoft.Data.Sqlite;

namespace HVTradingBot.Mobile.Tests;

/// <summary>The phone's data is exported to a file while the engine runs and restored, never losing the current data.</summary>
public sealed class DataBackupTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hvtradingbot-backup-" + Guid.NewGuid().ToString("N"));
    private byte[] _backup = [];
    private int _decisions;

    private MobileSettings Settings(string name) => new(Path.Combine(_root, name))
    {
        MarketData = MarketDataProvider.Simulated,
        Broker = BrokerProvider.Paper,
        Overrides = new Dictionary<string, string?>
        {
            ["MarketData:Simulated:BarIntervalMilliseconds"] = "20",
            ["MarketData:Simulated:WarmupDays"] = "10"
        }
    };

    public async Task InitializeAsync()
    {
        // A phone that has been trading: export while the engine runs.
        await using var runtime = MobileRuntime.Create(Settings("phone"));
        await runtime.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (Decisions(runtime.Settings.DatabasePath) < 20 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        using var file = new MemoryStream();
        var info = await runtime.Backup.ExportAsync(file, CancellationToken.None);
        _backup = file.ToArray();
        _decisions = info.Decisions;
        Assert.True(_decisions >= 20, $"only {_decisions} decisions");
        Assert.Equal(info.CreatedAtUtc, runtime.Backup.GetStatus().LastExportUtc);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_backup_restores_on_another_phone_and_the_engine_runs_on_it()
    {
        var settings = Settings("new-phone");
        var backup = new DataBackup(settings);

        var inspected = await backup.InspectAsync(new MemoryStream(_backup), CancellationToken.None);
        Directory.CreateDirectory(settings.DataDirectory);
        var (restored, _) = await backup.RestoreAsync(new MemoryStream(_backup), CancellationToken.None);

        Assert.Equal(_decisions, inspected.Decisions);
        Assert.Equal(inspected, restored);
        Assert.True(Decisions(settings.DatabasePath) >= _decisions);
        await using var runtime = MobileRuntime.Create(settings);
        await runtime.StartAsync(CancellationToken.None);
        var status = await runtime.Api.HandleAsync("GET", "/api/app/backup", null, CancellationToken.None);
        Assert.Equal(200, status.Status);
    }

    [Fact]
    public async Task Restoring_first_saves_the_current_data()
    {
        var settings = Settings("phone"); // the phone the backup came from, which has traded on since
        var backup = new DataBackup(settings);
        var before = Decisions(settings.DatabasePath);

        var (_, safety) = await backup.RestoreAsync(new MemoryStream(_backup), CancellationToken.None);

        Assert.True(File.Exists(safety));
        await using var copy = File.OpenRead(safety);
        Assert.Equal(before, (await new DataBackup(Settings("check")).InspectAsync(copy, CancellationToken.None)).Decisions);
        Assert.Equal(_decisions, Decisions(settings.DatabasePath));
    }

    [Fact]
    public async Task Files_that_are_not_usable_backups_are_refused_without_changing_anything()
    {
        var settings = Settings("phone");
        var backup = new DataBackup(settings);
        var before = Decisions(settings.DatabasePath);

        await AssertRefused(backup, "not a zip"u8.ToArray(), "not a backup");
        await AssertRefused(backup, Zip(("hello.txt", "hi"u8.ToArray())), "not an HVTradingBot backup");
        await AssertRefused(backup, WithFutureMigration(), "newer version");

        Assert.Equal(before, Decisions(settings.DatabasePath));
        Assert.Empty(Directory.GetFiles(backup.SafetyDirectory, "restore-*"));
    }

    private static async Task AssertRefused(DataBackup backup, byte[] file, string reason)
    {
        var error = await Assert.ThrowsAsync<BackupException>(() => backup.RestoreAsync(new MemoryStream(file), CancellationToken.None));
        Assert.Contains(reason, error.Message);
    }

    /// <summary>The backup as a newer app would make it: a migration this app does not have.</summary>
    private byte[] WithFutureMigration()
    {
        var path = Path.Combine(_root, "future.db");
        using (var zip = new ZipArchive(new MemoryStream(_backup), ZipArchiveMode.Read))
        {
            zip.GetEntry("trading.db")!.ExtractToFile(path, overwrite: true);
        }

        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO __EFMigrationsHistory (migration_id, product_version) VALUES ('29990101000000_Future', '99.0.0')";
            insert.ExecuteNonQuery();
        }

        using var original = new ZipArchive(new MemoryStream(_backup), ZipArchiveMode.Read);
        using var manifest = new MemoryStream();
        original.GetEntry("backup.json")!.Open().CopyTo(manifest);
        return Zip(("trading.db", File.ReadAllBytes(path)), ("backup.json", manifest.ToArray()));
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static int Decisions(string database)
    {
        if (!File.Exists(database))
        {
            return 0;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM trade_decisions";
            return Convert.ToInt32(count.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return 0; // still being created
        }
    }
}
