using MegaDTelegramRemoteControl.Infrastructure.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace MegaDTelegramRemoteControl.Infrastructure.Helpers;

public static class WebApplicationHelper
{
    public static void ConfigureByType<T>(this WebApplicationBuilder webAppBuilder) where T : class, new()
    {
        webAppBuilder.Services.Configure<T>(webAppBuilder.GetSectionByType<T>());
    }

    public static void ConfigureByType<T>(this WebApplicationBuilder webAppBuilder, T option) where T : class, new()
    {
        webAppBuilder.Services.Configure<T>(_ => Options.Create(option));
    }

    public static void ConfigureByTypeAsSingleton<T>(this WebApplicationBuilder webAppBuilder) where T : class, new()
    {
        webAppBuilder.Services.AddSingleton(_ => webAppBuilder.GetConfigByType<T>() ?? new());
    }

    public static T? GetConfigByType<T>(this WebApplicationBuilder webAppBuilder) where T : class, new()
    {
        return webAppBuilder.Configuration.GetConfigByType<T>();
    }

    public static T? GetConfigByType<T>(this IConfiguration configuration) where T : class, new()
    {
        return configuration.GetSection(typeof(T).Name).Get<T>();
    }

    public static IConfigurationSection GetSectionByType<T>(this WebApplicationBuilder webAppBuilder) where T : class, new()
    {
        return webAppBuilder.Configuration.GetSection(typeof(T).Name);
    }

    public static void ConfigureKestrel(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<KestrelServerOptions>(x =>
        {
            var kestrel = builder.Configuration.GetSection(nameof(KestrelConfig)).Get<KestrelConfig>();

            switch (kestrel)
            {
                case {Url: not (null or ""), Port: > 0}:
                    x.Listen(IPAddress.Parse(kestrel.Url), kestrel.Port);
                    break;
                case {Port: > 0}:
                    x.Listen(IPAddress.Any, kestrel.Port);
                    break;
            }
        });
    }
}