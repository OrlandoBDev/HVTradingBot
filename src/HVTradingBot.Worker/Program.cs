using HVTradingBot.Infrastructure.Notifications;
using HVTradingBot.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTradeDecisionNotifications(builder.Configuration);
builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
await host.RunAsync();
