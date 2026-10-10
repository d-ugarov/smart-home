using MegaDTelegramRemoteControl.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace MegaDTelegramRemoteControl.Tests.Infrastructure;

/// <summary>
/// The server as a black box: config of the <see cref="House"/>, fake controllers and fake Telegram behind
/// <see cref="FakeNetwork"/>. The real HTTP connectors are used - only the network is fake.
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
            config.AddJsonFile(house.AppSettingsPath, optional: false, reloadOnChange: false);
            config.AddInMemoryCollection(Overrides());
            config.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigureAll<HttpClientFactoryOptions>(x => x.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Network));
        });
    }

    private Dictionary<string, string?> Overrides()
    {
        var result = new Dictionary<string, string?>
                     {
                         ["PlatformConfig:UseFakeDeviceConnector"] = "false",
                         ["PlatformConfig:UseFakeTelegramBot"] = "false",
                         ["TelegramConfig:BotAccessToken"] = "123456:TEST-TOKEN",
                         ["TelegramConfig:AllowedUsers:0"] = OwnerId.ToString(),
                     };

        var index = 0;
        foreach (var _ in house.Devices)
            result[$"DevicesConfig:Devices:{index++}:Pwd"] = House.TestPassword;

        return result;
    }
}
