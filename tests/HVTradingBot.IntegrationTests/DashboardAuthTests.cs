using System.Net;
using System.Net.Http.Json;
using HVTradingBot.Api.Auth;
using HVTradingBot.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DashboardAuthTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "correct horse battery";
    private WebApplicationFactory<Program> _app = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        var keys = Directory.CreateTempSubdirectory("hv-keys-");
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Not Development: that reads the repository .env, whose connection string would point at the real database.
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:TradingDb", fixture.ConnectionString);
            b.UseSetting("DataProtection:KeysPath", keys.FullName);
            b.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            b.ConfigureServices(s => s.RemoveAll<IHostedService>()); // no broadcaster or broker catalog calls
        });
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private HttpClient Client()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(AuthSetup.RequestHeader, "1");
        return client;
    }

    private async Task<HttpClient> SignedUpClientAsync()
    {
        var client = Client();
        var code = _app.Services.GetRequiredService<SetupCode>().Current();
        var response = await client.PostAsJsonAsync("/api/auth/setup", new SetupRequest(code, "owner", Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task Everything_but_sign_in_and_health_requires_a_login()
    {
        var client = Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/markets/names")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/kill-switch", new { active = true, reason = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/dashboard/negotiate?negotiateVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);

        var me = await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/me");
        Assert.False(me!.Authenticated);
        Assert.True(me.SetupRequired);
    }

    [Fact]
    public async Task Login_is_created_once_with_the_setup_code()
    {
        var client = Client();
        var wrong = await client.PostAsJsonAsync("/api/auth/setup", new SetupRequest("0000-0000", "owner", Password));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var owner = await SignedUpClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/markets/names")).StatusCode);
        Assert.Equal("owner", (await owner.GetFromJsonAsync<AuthStatusDto>("/api/auth/me"))!.Username);

        var code = _app.Services.GetRequiredService<SetupCode>().Current();
        var again = await Client().PostAsJsonAsync("/api/auth/setup", new SetupRequest(code, "intruder", Password));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Setup_code_tolerates_copy_paste_differences()
    {
        var code = _app.Services.GetRequiredService<SetupCode>().Current();
        var pasted = $" \u201c{code.ToLowerInvariant().Replace("-", " \u2013 ")}\u201d ";
        var response = await Client().PostAsJsonAsync("/api/auth/setup", new SetupRequest(pasted, "owner", Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Short_passwords_are_refused()
    {
        var code = _app.Services.GetRequiredService<SetupCode>().Current();
        var response = await Client().PostAsJsonAsync("/api/auth/setup", new SetupRequest(code, "owner", "short"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_passwords_lock_the_login()
    {
        await SignedUpClientAsync();
        var client = Client();

        var first = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", "wrong password"));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        for (var i = 1; i < UserAccounts.MaxFailedLogins - 1; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", "wrong password"));
        }

        var locked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", "wrong password"));
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        var evenCorrect = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", Password));
        Assert.Equal(HttpStatusCode.Locked, evenCorrect.StatusCode);
    }

    [Fact]
    public async Task Changes_without_the_app_header_are_refused()
    {
        var owner = await SignedUpClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Remove(AuthSetup.RequestHeader);
        owner.DefaultRequestHeaders.Remove(AuthSetup.RequestHeader);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_sessions()
    {
        var first = await SignedUpClientAsync();
        var second = Client();
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", Password))).StatusCode);

        var change = await first.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(Password, "a brand new password"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/markets/names")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/markets/names")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client().PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", Password))).StatusCode);
    }
}
