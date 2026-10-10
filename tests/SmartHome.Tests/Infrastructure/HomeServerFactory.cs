using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using SmartHome.Tests.Fakes;

namespace SmartHome.Tests.Infrastructure;

/// <summary>
/// The server as a black box: house files of the <see cref="House"/>, fake controllers and fake Telegram behind
/// <see cref="FakeNetwork"/>. The real HTTP clients are used - only the network is fake.
/// </summary>
public sealed class HomeServerFactory(House house, IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const long OwnerId = 111;
    public const long StrangerId = 222;

    public FakeNetwork Network { get; } = new(house.Devices.Select(x => (x.Id, x.Url, House.TestPassword, house.CmdAllSnapshot(x.Id))));

    public void Reset() => Network.Reset();

    /// <summary> The bot is started in the background after the host; wait for its first API call. </summary>
    public async Task WaitForBotAsync()
    {
        _ = Server;
        await Network.Telegram.WaitForCallAsync("setMyCommands");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            foreach (var file in house.Files)
                config.AddJsonFile(file, optional: false, reloadOnChange: false);
            config.AddInMemoryCollection(Overrides());
            config.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });

        builder.ConfigureTestServices(services =>
        {
            // The simulator of local runs stays in place: it is what such a test checks.
            var simulated = settings?.GetValueOrDefault("MegaD:UseSimulator") == "true" ? MegaDClientName : null;
            services.AddSingleton<IPostConfigureOptions<HttpClientFactoryOptions>>(new FakeNetworkInstaller(Network, simulated));
        });
    }

    private const string MegaDClientName = "megad";

    private sealed class FakeNetworkInstaller(FakeNetwork network, string? skippedClient) : IPostConfigureOptions<HttpClientFactoryOptions>
    {
        public void PostConfigure(string? name, HttpClientFactoryOptions options)
        {
            if (name != skippedClient)
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = network);
        }
    }

    private Dictionary<string, string?> Overrides()
    {
        var result = new Dictionary<string, string?>
                     {
                         ["MegaD:UseSimulator"] = "false",
                         ["Telegram:UseFakeBot"] = "false",
                         ["Telegram:BotToken"] = "123456:TEST-TOKEN",
                         // Background reads would mix into the requests the tests count.
                         ["Home:StatePollInterval"] = "00:00:00",
                     };

        // A test may leave the list empty by passing "Telegram:AllowedUsers".
        if (settings?.ContainsKey("Telegram:AllowedUsers") != true)
            result["Telegram:AllowedUsers:0"] = OwnerId.ToString();

        foreach (var (id, _, _) in house.Devices)
            result[$"MegaD:Controllers:{id}:Password"] = House.TestPassword;

        return result;
    }
}
