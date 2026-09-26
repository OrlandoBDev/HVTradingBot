using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Signals;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Signals;

public sealed record SignalSettingsView(SignalSettings Settings, int Version, DateTime? UpdatedAtUtc, string? UpdatedBy);

/// <summary>Signal settings saved on the dashboard (defaults until then).</summary>
public sealed class SignalSettingsStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock)
{
    private const int RowId = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SignalSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.SignalSettings.AsNoTracking().SingleOrDefaultAsync(r => r.Id == RowId, cancellationToken);
        return row is null
            ? new SignalSettingsView(new SignalSettings(), 0, null, null)
            : new SignalSettingsView(JsonSerializer.Deserialize<SignalSettings>(row.Settings, Json) ?? new SignalSettings(), row.Version, row.UpdatedAtUtc,
                row.UpdatedBy);
    }

    public async Task<SignalSettingsView> SaveAsync(SignalSettings settings, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.SignalSettings.SingleOrDefaultAsync(r => r.Id == RowId, cancellationToken);
        if (row is null)
        {
            row = new SignalSettingsEntity { Id = RowId, Settings = "{}" };
            db.SignalSettings.Add(row);
        }

        row.Settings = JsonSerializer.Serialize(settings, Json);
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        return new SignalSettingsView(settings, row.Version, row.UpdatedAtUtc, actor);
    }
}

/// <summary>The signal settings in force for the engine; <see cref="RefreshAsync"/> picks up changes from the dashboard.</summary>
public sealed class SignalSettingsSource(SignalSettingsStore store, ILogger<SignalSettingsSource> logger) : ISignalSettingsSource
{
    private volatile SignalSettings _current = new();
    private int _version = -1;

    public SignalSettings Current => _current;

    public async Task<SignalSettings> RefreshAsync(CancellationToken cancellationToken)
    {
        var view = await store.GetAsync(cancellationToken);
        if (view.Version != _version)
        {
            var errors = view.Settings.Validate();
            if (errors.Count == 0)
            {
                _current = view.Settings;
            }
            else
            {
                logger.LogWarning("Stored signal settings are invalid ({Errors}); keeping the previous ones",
                    string.Join(", ", errors.Select(e => $"{e.Key}: {e.Value}")));
            }

            _version = view.Version;
        }

        return _current;
    }
}
