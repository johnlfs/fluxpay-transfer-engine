using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxPay.Api.Health;

public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented =
                true
        };

    public static async Task WriteAsync(
        HttpContext httpContext,
        HealthReport report)
    {
        httpContext.Response.ContentType =
            "application/json; charset=utf-8";

        var response =
            new HealthCheckResponse(
                Status:
                    report.Status.ToString(),

                TotalDurationMs:
                    Math.Round(
                        report.TotalDuration.TotalMilliseconds,
                        2),

                Checks:
                    report.Entries
                        .OrderBy(
                            entry =>
                                entry.Key,
                            StringComparer.Ordinal)
                        .Select(
                            entry =>
                                new HealthCheckEntryResponse(
                                    Name:
                                        entry.Key,

                                    Status:
                                        entry.Value.Status.ToString(),

                                    DurationMs:
                                        Math.Round(
                                            entry.Value.Duration.TotalMilliseconds,
                                            2),

                                    Description:
                                        entry.Value.Description,

                                    Error:
                                        entry.Value.Exception?.Message))
                        .ToArray());

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            response,
            SerializerOptions,
            httpContext.RequestAborted);
    }

    private sealed record HealthCheckResponse(
        string Status,
        double TotalDurationMs,
        IReadOnlyCollection<HealthCheckEntryResponse> Checks);

    private sealed record HealthCheckEntryResponse(
        string Name,
        string Status,
        double DurationMs,
        string? Description,
        string? Error);
}
