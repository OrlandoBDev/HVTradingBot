using System.Reflection;
using HVTradingBot.App.Core.Startup;
using HVTradingBot.Application.Trading;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace HVTradingBot.ArchitectureTests;

public class LayerTests
{
    private static readonly Assembly Domain = typeof(RiskManager).Assembly;
    private static readonly Assembly Application = typeof(TradingEngine).Assembly;
    private static readonly Assembly Infrastructure = typeof(TradingDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly AppCore = typeof(StartupPipeline).Assembly;
    private static readonly Assembly Dashboard = typeof(DashboardActions).Assembly;

    private static void AssertSuccess(TestResult result) =>
        Assert.True(result.IsSuccessful, "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void Domain_depends_on_nothing_outside_the_base_library() =>
        AssertSuccess(Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("HVTradingBot.Application", "HVTradingBot.Infrastructure", "HVTradingBot.Api",
                "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Extensions")
            .GetResult());

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_broker_sdks() =>
        AssertSuccess(Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("HVTradingBot.Infrastructure", "HVTradingBot.Api", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult());

    [Fact]
    public void Strategies_scoring_and_risk_cannot_reach_a_broker() =>
        AssertSuccess(Types.InAssembly(Domain).That()
            .ResideInNamespaceMatching(@"HVTradingBot\.Domain\.(Strategies|Scoring|Risk|Decisions|Analysis)")
            .ShouldNot().HaveDependencyOnAny("HVTradingBot.Application.Abstractions", "HVTradingBot.Infrastructure.Brokers")
            .GetResult());

    [Fact]
    public void Api_cannot_place_orders() => AssertCannotPlaceOrders(Api);

    [Fact]
    public void Dashboard_cannot_place_orders() => AssertCannotPlaceOrders(Dashboard);

    private static void AssertCannotPlaceOrders(Assembly assembly)
    {
        // The dashboard may only read state and record requests; it must never reach a broker or the execution service.
        // (The Android app hosts the dashboard and the engine in one process, so this boundary is what keeps them apart.)
        AssertSuccess(Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOnAny(
                "HVTradingBot.Application.Abstractions.IBroker",
                "HVTradingBot.Application.Abstractions.IExecutionBroker",
                "HVTradingBot.Application.Trading.ExecutionService",
                "HVTradingBot.Application.Trading.TradingEngine",
                "HVTradingBot.Infrastructure.Brokers")
            .GetResult());
    }

    [Fact]
    public void Only_execution_service_and_engine_use_the_broker_in_application_layer()
    {
        var users = Types.InAssembly(Application).That()
            .HaveDependencyOn("HVTradingBot.Application.Abstractions.IExecutionBroker")
            .And().DoNotResideInNamespace("HVTradingBot.Application.Backtesting")
            .GetTypes()
            .Select(t => t.Name)
            .Where(n => n is not ("ExecutionService" or "TradingEngine" or "IExecutionBroker"));

        Assert.Empty(users);
    }

    [Fact]
    public void App_core_only_orchestrates_docker_and_http()
    {
        // The macOS app talks to the stack through docker compose and the API's HTTP endpoints, never in-process, and
        // App.Core stays free of MAUI so the solution builds and tests on Linux.
        AssertSuccess(Types.InAssembly(AppCore).ShouldNot()
            .HaveDependencyOnAny("HVTradingBot.Domain", "HVTradingBot.Application", "HVTradingBot.Infrastructure",
                "HVTradingBot.Api", "HVTradingBot.Worker", "HVTradingBot.Contracts", "Microsoft.Maui")
            .GetResult());
    }
}
