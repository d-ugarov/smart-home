using Microsoft.AspNetCore.Http.Extensions;
using System.Diagnostics;

namespace SmartHome.Host;

/// <summary> Every request to the server - mostly controller events - with the answer and the duration (own log file). </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var url = context.Request.GetDisplayUrl();

        logger.LogDebug("{Id} Started | Url: {Url} | IP: {Ip}", context.TraceIdentifier, url, context.Connection.RemoteIpAddress);

        await next(context);

        logger.LogDebug("{Id} Finished in {Seconds:0.####}s | Url: {Url} | Response: {StatusCode}",
                        context.TraceIdentifier, Stopwatch.GetElapsedTime(started).TotalSeconds, url, context.Response.StatusCode);
    }
}
