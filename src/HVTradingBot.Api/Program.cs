using HVTradingBot.Application.Configuration;
using HVTradingBot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<TradingOptions>()
    .Bind(builder.Configuration.GetSection(TradingOptions.SectionName))
    .ValidateOnStart();

builder.Services
    .AddOptions<RiskOptions>()
    .Bind(builder.Configuration.GetSection(RiskOptions.SectionName))
    .Validate(x => x.MaxRiskPerTradePercent > 0 && x.MaxRiskPerTradePercent <= 5,
        "MaxRiskPerTradePercent must be greater than 0 and no more than 5.")
    .Validate(x => x.MaxDailyLossPercent > 0, "MaxDailyLossPercent must be greater than 0.")
    .Validate(x => x.MaxOpenPositions > 0, "MaxOpenPositions must be greater than 0.")
    .Validate(x => x.MinimumRiskReward > 0, "MinimumRiskReward must be greater than 0.")
    .ValidateOnStart();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    application = "HVTradingBot",
    version = "0.1.0",
    tradingMode = "PAPER",
    status = "MVP Development"
}));

app.Run();
