namespace SmartHome.Host;

/// <summary>
/// The controllers call the server at a fixed address: port 5075 on every interface, as in <c>master</c>. ASP.NET Core
/// alone would listen on localhost:5000 - the controllers would get no answer and fall back to their own scenarios,
/// silently. An address configured in Kestrel:Endpoints or urls wins.
/// </summary>
public static class ListenAddress
{
    public const string Default = "http://0.0.0.0:5075";

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrEmpty(configuration["urls"]) ||
        !string.IsNullOrEmpty(configuration["http_ports"]) ||
        !string.IsNullOrEmpty(configuration["https_ports"]) ||
        configuration.GetSection("Kestrel:Endpoints").GetChildren().Any();

    public static void UseDefaultListenAddress(this WebApplicationBuilder builder)
    {
        if (!IsConfigured(builder.Configuration))
            builder.WebHost.UseUrls(Default);
    }
}
