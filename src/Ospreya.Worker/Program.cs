using Ospreya.Infrastructure;
using Ospreya.Infrastructure.Configuration;
using Ospreya.Infrastructure.Observability;
using Ospreya.Infrastructure.Persistence;
using Ospreya.Hosting;
using Serilog;

const string serviceName = "Ospreya.Worker";
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddSharedTradingConfiguration(builder.Environment.EnvironmentName);

    builder.Services.AddSerilog((_, logger) => logger.ConfigureOspreyaLogging(builder.Configuration, serviceName));
    builder.Services.AddOspreyaTelemetry(builder.Configuration, serviceName);
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
