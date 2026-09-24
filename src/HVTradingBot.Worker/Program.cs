using HVTradingBot.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
await host.RunAsync();
