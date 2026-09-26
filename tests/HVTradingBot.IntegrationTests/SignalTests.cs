using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Signals;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Persistence.Stores;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.Signals;
using HVTradingBot.Infrastructure.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

/// <summary>
/// Signals in the database: the engine adds them, the dashboard records decisions (validating accepted risks), the
/// worker claims and records outcomes, and results are reported apart from the bot's.
/// </summary>
public abstract class SignalTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc);
    private readonly Clock _clock = new() { UtcNow = Now };
    private SignalStore _store = null!;
    private SignalDashboard _dashboard = null!;

    private static readonly DashboardCaller Caller = new("tester", "c1");

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _store = new SignalStore(fixture.DbFactory, _clock);
        var settingsStore = new SignalSettingsStore(fixture.DbFactory, _clock);
        _dashboard = new SignalDashboard(_store, settingsStore, new SignalSettingsSource(settingsStore, NullLogger<SignalSettingsSource>.Instance),
            fixture.DbFactory, new EfDecisionJournal(fixture.DbFactory, _clock), NullLogger<SignalDashboard>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static NewSignal New(string setupId, params RiskCheck[] failed) => new(Guid.NewGuid(), setupId, SignalKinds.SignalsOnlyMarket, "EUR/USD",
        Direction.Long, "Trend Following", 80, "TrendingBullish", 1.1m, 1.098m, 1.105m, Now, Now.AddMinutes(10), Guid.NewGuid(),
        new RiskDecision(failed.Length == 0, 1000, failed.Length == 0 ? 20m : 0, [new RiskCheck("KillSwitch", true, "Inactive."), .. failed]));

    [Fact]
    public async Task A_signal_goes_from_pending_to_placed_and_the_same_setup_is_added_once()
    {
        var signal = New("EURUSD-TF-L-1");
        Assert.True(await _store.AddAsync(signal, CancellationToken.None));
        Assert.False(await _store.AddAsync(signal with { Id = Guid.NewGuid() }, CancellationToken.None));

        var accepted = await _dashboard.AcceptAsync(signal.Id, new AcceptSignalRequest([], null), Caller, CancellationToken.None);
        Assert.IsType<DashboardResult.OkResult>(accepted);
        var claimed = await _store.ClaimNextAcceptedAsync(CancellationToken.None);
        Assert.Equal(signal.Id, claimed!.Id);
        Assert.Equal(SignalStatus.Placing, claimed.Status);
        Assert.Null(await _store.ClaimNextAcceptedAsync(CancellationToken.None));

        var positionId = Guid.NewGuid();
        await _store.RecordOutcomeAsync(signal.Id, new SignalOutcome(SignalOutcomeStatus.Placed, "Filled.", [new RiskCheck("KillSwitch", true, "Inactive.")],
            20m, positionId, 1.1001m), CancellationToken.None);

        var dto = await _dashboard.GetAsync(signal.Id, CancellationToken.None);
        Assert.Equal(SignalStatus.Placed, dto!.Status);
        Assert.Equal(positionId, dto.PositionId);
        Assert.Equal("tester", dto.DecidedBy);
        Assert.Equal(2.5m, dto.RewardToRisk);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "SignalAccepted"));
    }

    [Fact]
    public async Task Hard_rules_can_never_be_accepted_and_loss_limits_need_the_setting_and_a_typed_confirmation()
    {
        var signal = New("EURUSD-TF-L-2", new RiskCheck(RiskRuleKinds.SignalDailyLoss, false, "Lost too much."));
        await _store.AddAsync(signal, CancellationToken.None);

        var hard = await _dashboard.AcceptAsync(signal.Id, new AcceptSignalRequest(["KillSwitch"], null), Caller, CancellationToken.None);
        var notAllowed = await _dashboard.AcceptAsync(signal.Id, new AcceptSignalRequest([RiskRuleKinds.SignalDailyLoss], "ACCEPT"), Caller, CancellationToken.None);
        Assert.IsType<DashboardResult.InvalidResult>(hard);
        Assert.IsType<DashboardResult.InvalidResult>(notAllowed);
        var listed = await _dashboard.GetAsync(signal.Id, CancellationToken.None);
        Assert.False(listed!.Checks.Single(c => c.Rule == RiskRuleKinds.SignalDailyLoss).MayAccept);

        var settings = await _dashboard.GetSettingsAsync(CancellationToken.None);
        Assert.IsType<DashboardResult.OkResult>(await _dashboard.SaveSettingsAsync(settings with { AllowLossLimitOverride = true }, Caller, CancellationToken.None));

        var unconfirmed = await _dashboard.AcceptAsync(signal.Id, new AcceptSignalRequest([RiskRuleKinds.SignalDailyLoss], "yes"), Caller, CancellationToken.None);
        var confirmed = await _dashboard.AcceptAsync(signal.Id, new AcceptSignalRequest([RiskRuleKinds.SignalDailyLoss], "ACCEPT"), Caller, CancellationToken.None);
        Assert.IsType<DashboardResult.InvalidResult>(unconfirmed);
        Assert.IsType<DashboardResult.OkResult>(confirmed);
        Assert.Equal([RiskRuleKinds.SignalDailyLoss], SignalStore.ReadAcceptedRules((await _store.GetAsync(signal.Id, CancellationToken.None))!));
    }

    [Fact]
    public async Task Unanswered_signals_expire_and_can_no_longer_be_traded()
    {
        var signal = New("EURUSD-TF-L-3");
        await _store.AddAsync(signal, CancellationToken.None);

        _clock.UtcNow = Now.AddMinutes(11);
        Assert.Equal(1, await _store.ExpireDueAsync(CancellationToken.None));
        var late = await _dashboard.AcceptAsync(signal.Id, new AcceptSignalRequest([], null), Caller, CancellationToken.None);

        Assert.IsType<DashboardResult.InvalidResult>(late);
        Assert.Equal(SignalStatus.Expired, (await _store.GetAsync(signal.Id, CancellationToken.None))!.Status);
        Assert.Empty((await _dashboard.ListAsync(true, 1, 20, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task A_review_clears_the_accepted_risks_and_a_restart_fails_signals_being_placed()
    {
        var review = New("EURUSD-TF-L-4");
        var interrupted = New("EURUSD-TF-L-5");
        await _store.AddAsync(review, CancellationToken.None);
        await _store.AddAsync(interrupted, CancellationToken.None);
        await _store.AcceptAsync(review.Id, ["Spread"], "tester", CancellationToken.None);
        await _store.AcceptAsync(interrupted.Id, [], "tester", CancellationToken.None);
        await _store.ClaimNextAcceptedAsync(CancellationToken.None);
        await _store.ClaimNextAcceptedAsync(CancellationToken.None);

        await _store.RecordOutcomeAsync(review.Id, new SignalOutcome(SignalOutcomeStatus.NeedsReview, "1 risk rule failed.",
            [new RiskCheck("Cooldown", false, "Cooling down.")]), CancellationToken.None);
        Assert.Equal(1, await _store.FailInterruptedAsync(CancellationToken.None));
        var reviewed = (await _store.GetAsync(review.Id, CancellationToken.None))!;
        Assert.Equal(SignalStatus.NeedsReview, reviewed.Status);
        Assert.Empty(SignalStore.ReadAcceptedRules(reviewed));
        Assert.Equal(SignalStatus.Failed, (await _store.GetAsync(interrupted.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Results_report_taken_overridden_and_missed_signals_and_keep_signal_trades_out_of_app_profit()
    {
        // Taken with an accepted risk: closed at +30. Skipped: its virtual trade reached the target (+2.5R).
        var taken = New("EURUSD-TF-L-6");
        var skipped = New("EURUSD-TF-L-7");
        await _store.AddAsync(taken, CancellationToken.None);
        await _store.AddAsync(skipped, CancellationToken.None);
        await _store.SkipAsync(skipped.Id, "tester", CancellationToken.None);
        await _store.AcceptAsync(taken.Id, ["Spread"], "tester", CancellationToken.None);
        await _store.ClaimNextAcceptedAsync(CancellationToken.None);
        var positionId = Guid.NewGuid();
        await _store.RecordOutcomeAsync(taken.Id, new SignalOutcome(SignalOutcomeStatus.Placed, "Filled.",
            [new RiskCheck("Spread", true, "Accepted by you: wide.", Overridden: true)], 20m, positionId, 1.1m), CancellationToken.None);

        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Positions.AddRange(
                Position(positionId, SignalOrders.Prefix + taken.SetupId, pnl: 30m),
                Position(Guid.NewGuid(), "EURUSD-bot-1", pnl: -10m));
            db.SetupOutcomes.Add(new SetupOutcomeEntity
            {
                Id = Guid.NewGuid(), SetupId = skipped.SetupId, Instrument = "EUR/USD", AssetClass = "Forex", Strategy = "Trend Following",
                Regime = "TrendingBullish", Direction = "Long", DecisionState = "ApprovalRequired", Score = 80, Entry = 1.1m, StopLoss = 1.098m,
                TakeProfit = 1.105m, OpenedAtUtc = Now, Status = "TakeProfit", RMultiple = 2.5m, ExitPrice = 1.105m, ClosedAtUtc = Now.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var stats = await _dashboard.GetStatsAsync(CancellationToken.None);
        Assert.Equal((1, 1, 30m, 1, 30m, 1, 1, 2.5m), (stats.Taken, stats.TakenWins, stats.TakenPnl, stats.Overridden, stats.OverriddenPnl, stats.Skipped,
            stats.MissedResolved, stats.MissedR));
        var history = (await _dashboard.ListAsync(false, 1, 20, CancellationToken.None)).Items;
        Assert.Equal(30m, history.Single(s => s.Id == taken.Id).Result!.Pnl);
        Assert.Equal(2.5m, history.Single(s => s.Id == skipped.Id).Result!.RMultiple);

        var profit = await Queries().GetProfitSummaryAsync("UTC", CancellationToken.None);
        Assert.Equal(-10m, profit.App.AllTime);
        Assert.Equal(30m, profit.Signals!.AllTime);
        var trades = await Queries().GetTradeHistoryAsync(10, CancellationToken.None);
        Assert.Equal("Signal", trades.Single(t => t.Id == positionId).Source);
    }

    [Fact]
    public async Task Settings_are_validated_saved_and_applied()
    {
        var current = await _dashboard.GetSettingsAsync(CancellationToken.None);
        var invalid = await _dashboard.SaveSettingsAsync(current with { RiskPerTradePercent = 9, QuietHoursStart = "25:00" }, Caller, CancellationToken.None);
        var saved = await _dashboard.SaveSettingsAsync(current with
        {
            SignalOnlyInstruments = ["EUR/USD"], QuietHoursStart = "22:00", QuietHoursEnd = "07:00", TimeZone = "Europe/London"
        }, Caller, CancellationToken.None);

        var errors = Assert.IsType<DashboardResult.InvalidResult>(invalid).Errors;
        Assert.Contains("riskPerTradePercent", errors.Keys);
        Assert.Contains("quietHoursStart", errors.Keys);
        var dto = (SignalSettingsDto)Assert.IsType<DashboardResult.OkResult>(saved).Value!;
        Assert.Equal(["EUR/USD"], dto.SignalOnlyInstruments);
        Assert.Equal("22:00", dto.QuietHoursStart);
        Assert.Equal(1, dto.Version);

        var engineView = new SignalSettingsSource(new SignalSettingsStore(fixture.DbFactory, _clock), NullLogger<SignalSettingsSource>.Instance);
        var applied = await engineView.RefreshAsync(CancellationToken.None);
        Assert.True(applied.IsSignalOnly("EUR/USD"));
        Assert.Equal(new TimeOnly(7, 0), applied.QuietHoursEnd);
    }

    private DashboardQueries Queries() => new(fixture.DbFactory, new EfTradingStateStore(fixture.DbFactory), _clock,
        new RiskOptionsSource(new RiskOptions(), new RiskSettingsStore(fixture.DbFactory, _clock), NullLogger<RiskOptionsSource>.Instance),
        new TradingEngineOptions(), new MarketCatalogStore(fixture.DbFactory, _clock), new CloseRequestStore(fixture.DbFactory, _clock));

    private static PositionEntity Position(Guid id, string clientOrderId, decimal pnl) => new()
    {
        Id = id,
        OrderId = Guid.NewGuid(),
        Broker = "Paper",
        ClientOrderId = clientOrderId,
        Instrument = "EUR/USD",
        Direction = "Long",
        Units = 1000,
        EntryPrice = 1.1m,
        StopLoss = 1.098m,
        TakeProfit = 1.105m,
        InitialRiskAmount = 20,
        OpenedAtUtc = Now.AddHours(-2),
        Strategy = "Trend Following",
        Score = 80,
        IsOpen = false,
        RealizedPnl = pnl,
        ClosedAtUtc = Now.AddHours(-1)
    };

    private sealed class Clock : IClock
    {
        public DateTime UtcNow { get; set; }
    }
}

[Collection(PostgresCollection.Name)]
public sealed class SignalTestsOnPostgres(PostgresFixture fixture) : SignalTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class SignalTestsOnSqlite(SqliteFixture fixture) : SignalTests(fixture);
