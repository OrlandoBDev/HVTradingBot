using HVTradingBot.Infrastructure.Configuration;

namespace HVTradingBot.UnitTests.Configuration;

public class DotEnvFileTests
{
    [Fact]
    public void Env_file_gives_the_same_connection_string_as_run_sh()
    {
        var env = DotEnvFile.Parse(
        [
            "# comment",
            "POSTGRES_USER=hvtradingbot",
            "POSTGRES_DB=hvtradingbot",
            "POSTGRES_PASSWORD=\"s3cret=with=equals\"",
            "",
            "export POSTGRES_PORT=5433",
            "BROKER_PROVIDER=Paper",
            "DERIV_API_TOKEN="
        ]);

        var settings = DotEnvFile.ToSettings(env);

        Assert.Equal("Host=127.0.0.1;Port=5433;Database=hvtradingbot;Username=hvtradingbot;Password=s3cret=with=equals",
            settings["ConnectionStrings:TradingDb"]);
        Assert.Equal("Paper", settings["Broker:Provider"]);
        Assert.False(settings.ContainsKey("Deriv:ApiToken")); // empty values are not applied
    }

    [Fact]
    public void Env_file_is_found_in_a_parent_directory()
    {
        var root = Directory.CreateTempSubdirectory("hv-env-");
        var nested = root.CreateSubdirectory("src/HVTradingBot.Api/bin/Debug/net10.0");
        File.WriteAllText(Path.Combine(root.FullName, DotEnvFile.FileName), "POSTGRES_PASSWORD=x");

        Assert.Equal(Path.Combine(root.FullName, DotEnvFile.FileName), DotEnvFile.Find(nested.FullName));
    }
}
