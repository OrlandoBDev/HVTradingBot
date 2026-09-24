using System.Reflection;
using HVTradingBot.Application.Trading;
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
    public void Api_cannot_place_orders()
    {
        // The dashboard may only read state and toggle the kill switch; it must never reach a broker or the execution service.
        AssertSuccess(Types.InAssembly(Api).ShouldNot()
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
}
