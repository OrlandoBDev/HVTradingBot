using HVTradingBot.Domain.Common;
using HVTradingBot.Infrastructure.Brokers.Deriv;

namespace HVTradingBot.UnitTests.Deriv;

public class DerivContractMathTests
{
    private static readonly int[] Multipliers = [100, 200, 300, 500, 800];

    [Fact]
    public void Chooses_lowest_multiplier_that_fits_the_stake_limit()
    {
        // 30,000 USD notional: x100 -> stake 300 (fits 500 max)
        var (plan, rejection) = DerivContractMath.Plan(Direction.Long, 30_000m, 1.1000m, 1.0980m, 1.1040m, Multipliers, 1m, 500m, 0.000213m);

        Assert.Null(rejection);
        Assert.Equal("MULTUP", plan!.ContractType);
        Assert.Equal(100, plan.Multiplier);
        Assert.Equal(300m, plan.Stake);
        // stop: 30,000 * 0.002/1.1 = 54.55 + commission 6.39 = 60.94; target: 30,000 * 0.004/1.1 = 109.09 - 6.39 = 102.70
        Assert.Equal(60.94m, plan.StopLossAmount);
        Assert.Equal(102.70m, plan.TakeProfitAmount);
    }

    [Fact]
    public void Larger_positions_move_to_a_higher_multiplier()
    {
        var (plan, _) = DerivContractMath.Plan(Direction.Short, 120_000m, 1.1m, 1.102m, 1.096m, Multipliers, 1m, 500m, 0.000213m);
        Assert.Equal("MULTDOWN", plan!.ContractType);
        Assert.Equal(300, plan.Multiplier);
        Assert.Equal(400m, plan.Stake);
    }

    [Fact]
    public void Rejects_positions_larger_than_the_broker_allows()
    {
        var (plan, rejection) = DerivContractMath.Plan(Direction.Long, 1_000_000m, 1.1m, 1.098m, 1.104m, Multipliers, 1m, 500m, 0.000213m);
        Assert.Null(plan);
        Assert.Contains("maximum", rejection);
    }

    [Fact]
    public void Rejects_stop_wider_than_the_contract_stake()
    {
        // 45,000 notional at x100 = 450 stake; a 1,500-pip stop loses ~6,100, beyond the stake (automatic stop-out).
        var (plan, rejection) = DerivContractMath.Plan(Direction.Long, 45_000m, 1.1m, 0.95m, 1.4m, Multipliers, 1m, 500m, 0.000213m);
        Assert.Null(plan);
        Assert.Contains("exceed the contract stake", rejection);
    }

    [Fact]
    public void Rejects_target_that_does_not_cover_commission()
    {
        var (plan, rejection) = DerivContractMath.Plan(Direction.Long, 10_000m, 1.1m, 1.098m, 1.10001m, Multipliers, 1m, 500m, 0.000213m);
        Assert.Null(plan);
        Assert.Contains("commission", rejection);
    }

    [Fact]
    public void Quoted_commission_resets_stop_and_target_amounts()
    {
        var (plan, _) = DerivContractMath.Plan(Direction.Long, 30_000m, 1.1m, 1.098m, 1.104m, Multipliers, 1m, 500m, 0.000213m);

        var (adjusted, rejection) = DerivContractMath.WithQuotedCommission(plan!, 1.1m, 1.098m, 1.104m, 10m, 0.25m);

        Assert.Null(rejection);
        Assert.Equal(64.55m, adjusted!.StopLossAmount);   // 54.55 + 10
        Assert.Equal(99.09m, adjusted.TakeProfitAmount);  // 109.09 - 10
    }

    [Fact]
    public void Commission_above_a_quarter_of_the_risk_is_rejected()
    {
        // Tiny position (e.g. a $100 demo balance): 200 USD notional, 0.4% stop -> 0.73 at risk; a 0.23 commission is ~24%, 0.30 is ~29%.
        var (plan, _) = DerivContractMath.Plan(Direction.Long, 200m, 1.1m, 1.0956m, 1.1088m, Multipliers, 1m, 500m, 0.000213m);
        Assert.NotNull(DerivContractMath.WithQuotedCommission(plan!, 1.1m, 1.0956m, 1.1088m, 0.23m, 0.25m).Plan);
        var (rejected, reason) = DerivContractMath.WithQuotedCommission(plan!, 1.1m, 1.0956m, 1.1088m, 0.30m, 0.25m);
        Assert.Null(rejected);
        Assert.Contains("too small for its fees", reason);
    }

    [Theory]
    [InlineData(1.0981, true)]
    [InlineData(1.0995, false)]
    public void Broker_stop_must_be_close_to_strategy_stop(double brokerStop, bool ok) =>
        Assert.Equal(ok, DerivContractMath.StopWithinTolerance(1.1m, 1.098m, (decimal)brokerStop, 0.25m));

    [Theory]
    [InlineData(Direction.Long, 1.1040, ExitClassification.TakeProfit)]
    [InlineData(Direction.Long, 1.0979, ExitClassification.StopLoss)]
    [InlineData(Direction.Long, 1.1010, ExitClassification.Manual)]
    [InlineData(Direction.Short, 1.0960, ExitClassification.TakeProfit)]
    [InlineData(Direction.Short, 1.1025, ExitClassification.StopLoss)]
    public void Classifies_broker_exits(Direction direction, double exit, ExitClassification expected)
    {
        var (stop, target) = direction == Direction.Long ? (1.098m, 1.104m) : (1.102m, 1.096m);
        Assert.Equal(expected, DerivContractMath.ClassifyExit(direction, (decimal)exit, stop, target));
    }
}
