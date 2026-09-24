namespace HVTradingBot.Domain.Common;

public static class DirectionExtensions
{
    /// <summary>+1 for long, -1 for short. Used to sign price differences.</summary>
    public static int Sign(this Direction direction) => direction == Direction.Long ? 1 : -1;

    public static Direction Opposite(this Direction direction) =>
        direction == Direction.Long ? Direction.Short : Direction.Long;
}
