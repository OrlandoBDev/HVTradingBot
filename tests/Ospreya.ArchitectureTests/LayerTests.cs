using System.Reflection;
using Ospreya.App.Core.Startup;
using Ospreya.Application.Trading;
using Ospreya.Dashboard;
using Ospreya.Domain.Risk;
using Ospreya.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace Ospreya.ArchitectureTests;

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
            .HaveDependencyOnAny("Ospreya.Application", "Ospreya.Infrastructure", "Ospreya.Api",
                "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Extensions")
            .GetResult());

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_broker_sdks() =>
        AssertSuccess(Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("Ospreya.Infrastructure", "Ospreya.Api", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult());

    [Fact]
    public void Strategies_scoring_and_risk_cannot_reach_a_broker() =>
        AssertSuccess(Types.InAssembly(Domain).That()
            .ResideInNamespaceMatching(@"Ospreya\.Domain\.(Strategies|Scoring|Risk|Decisions|Analysis)")
            .ShouldNot().HaveDependencyOnAny("Ospreya.Application.Abstractions", "Ospreya.Infrastructure.Brokers")
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
                "Ospreya.Application.Abstractions.IBroker",
                "Ospreya.Application.Abstractions.IExecutionBroker",
                "Ospreya.Application.Trading.ExecutionService",
                "Ospreya.Application.Trading.TradingEngine",
                "Ospreya.Infrastructure.Brokers")
            .GetResult());
    }

    [Fact]
    public void Only_execution_service_and_engine_use_the_broker_in_application_layer()
    {
        var users = Types.InAssembly(Application).That()
            .HaveDependencyOn("Ospreya.Application.Abstractions.IExecutionBroker")
            .And().DoNotResideInNamespace("Ospreya.Application.Backtesting")
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
            .HaveDependencyOnAny("Ospreya.Domain", "Ospreya.Application", "Ospreya.Infrastructure",
                "Ospreya.Api", "Ospreya.Worker", "Ospreya.Contracts", "Microsoft.Maui")
            .GetResult());
    }
}
