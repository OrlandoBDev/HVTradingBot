using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Brokers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<PaperTradingBroker>();
builder.Services.AddSingleton<IBroker>(sp => sp.GetRequiredService<PaperTradingBroker>());

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    application = "HVTradingBot",
    version = "0.1.0",
    tradingMode = "PAPER",
    status = "Foundation"
}));

app.Run();
