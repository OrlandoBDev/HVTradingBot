using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.Observability;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Hosting;
using Serilog;

const string serviceName = "HVTradingBot.Worker";
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddSharedTradingConfiguration(builder.Environment.EnvironmentName);

    builder.Services.AddSerilog((_, logger) => logger.ConfigureHvLogging(builder.Configuration, serviceName));
    builder.Services.AddHvTelemetry(builder.Configuration, serviceName);
    builder.Services.AddTradingCore(builder.Configuration).AddLiveTrading(builder.Configuration);
    // Only one worker per database may trade (TradingWorker takes the lock before anything else).
    builder.Services.AddTradingWorker(sp => new WorkerInstanceLock(
        builder.Configuration.GetConnectionString(DatabaseSetup.ConnectionStringName)!, sp.GetRequiredService<ILogger<WorkerInstanceLock>>()));

    await builder.Build().RunAsync();
    // BrokerSettingsWatcher sets exit code 3 to ask run.sh / Docker for a restart (market selection changed).
    return Environment.ExitCode;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "{Service} terminated unexpectedly", serviceName);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
