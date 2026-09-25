using HVTradingBot.App.Core.Configuration;
using HVTradingBot.App.Core.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.App.Tests;

public class RepositoryEnvironmentTests
{
    private const string Repo = "/Users/me/HVTradingBot";
    private const string Example = "POSTGRES_USER=hvtradingbot\nPOSTGRES_PASSWORD=change-me\n# Dashboard/API port.\nAPI_PORT=5080\n";

    private readonly FakeFileSystem files = new();

    private RepositoryEnvironment Create() => new(files, NullLogger<RepositoryEnvironment>.Instance);

    [Fact]
    public void Env_is_created_from_the_example_with_a_random_password_and_mode_600()
    {
        files.Files[$"{Repo}/.env.example"] = Example;

        Assert.True(Create().EnsureEnvFile(Repo));

        var env = files.Files[$"{Repo}/.env"];
        Assert.Contains($"{Repo}/.env", files.PrivateFiles);
        Assert.Matches("^POSTGRES_USER=hvtradingbot\nPOSTGRES_PASSWORD=[0-9a-f]{48}\n# Dashboard/API port.\nAPI_PORT=5080\n$", env);
    }

    [Fact]
    public void Existing_env_is_left_alone()
    {
        files.Files[$"{Repo}/.env"] = "POSTGRES_PASSWORD=mine";

        Assert.False(Create().EnsureEnvFile(Repo));
        Assert.Equal("POSTGRES_PASSWORD=mine", files.Files[$"{Repo}/.env"]);
    }

    [Fact]
    public void Missing_example_is_an_error()
    {
        Assert.Throws<FileNotFoundException>(() => Create().EnsureEnvFile(Repo));
    }

    [Fact]
    public void Passwords_differ_each_time()
    {
        Assert.NotEqual(RepositoryEnvironment.NewPassword(), RepositoryEnvironment.NewPassword());
        Assert.Matches("^[0-9a-f]{48}$", RepositoryEnvironment.NewPassword());
    }

    [Fact]
    public void Password_line_is_appended_when_the_template_has_none()
    {
        Assert.Equal("A=1\nPOSTGRES_PASSWORD=x\n", RepositoryEnvironment.WithPassword("A=1\n", "x"));
    }

    [Theory]
    [InlineData("API_PORT=6090", 6090)]
    [InlineData("API_PORT=\"6091\"", 6091)]
    [InlineData("API_PORT=", 5080)]
    [InlineData("API_PORT=abc", 5080)]
    [InlineData("API_PORT=70000", 5080)]
    [InlineData("# API_PORT=6000", 5080)]
    public void Api_port_comes_from_env(string line, int expected)
    {
        files.Files[$"{Repo}/.env"] = "POSTGRES_PASSWORD=x\n" + line + "\n";

        Assert.Equal(expected, Create().ReadApiPort(Repo));
    }

    [Fact]
    public void Api_port_defaults_to_5080_without_env()
    {
        Assert.Equal(5080, Create().ReadApiPort(Repo));
    }

    [Fact]
    public void Key_folder_is_created_under_application_support()
    {
        var path = Create().EnsureKeysDirectory("/Users/me");

        Assert.Equal("/Users/me/Library/Application Support/HVTradingBot/keys", path);
        Assert.Contains(path, files.Directories);
    }

    [Fact]
    public void Compose_file_marks_a_repository()
    {
        Assert.False(Create().HasComposeFile(Repo));
        files.Files[$"{Repo}/docker-compose.yml"] = "";
        Assert.True(Create().HasComposeFile(Repo));
    }

    [Fact]
    public void Physical_private_file_has_mode_600()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("hv-app-").FullName, ".env");

        new PhysicalFileSystem().CreatePrivateFile(path, "A=1");

        Assert.Equal("A=1", File.ReadAllText(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }

        Assert.Throws<IOException>(() => new PhysicalFileSystem().CreatePrivateFile(path, "B=2"));
    }
}
