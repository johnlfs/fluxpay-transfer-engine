using System.Diagnostics.Metrics;
using FluxPay.Api.Observability;
using Microsoft.AspNetCore.Http;

namespace FluxPay.IntegrationTests.Observability;

public sealed class TransferFailureMetricsTests
{
    private const string FailureMetricName =
        "fluxpay.transfer.failures";

    [Fact]
    public async Task PostTransfer_WithFailureStatus_RecordsFailureMetric()
    {
        var measurements =
            new List<FailureMeasurement>();

        using var listener =
            CreateListener(
                measurements);

        var middleware =
            new TransferMetricsMiddleware(
                context =>
                {
                    context.Response.StatusCode =
                        StatusCodes.Status422UnprocessableEntity;

                    return Task.CompletedTask;
                });

        var context =
            CreateContext(
                HttpMethods.Post,
                "/api/transfers");

        await middleware.InvokeAsync(
            context);

        var measurement =
            Assert.Single(
                measurements);

        Assert.Equal(
            1,
            measurement.Value);

        Assert.Equal(
            StatusCodes.Status422UnprocessableEntity,
            measurement.StatusCode);
    }

    [Fact]
    public async Task PostTransfer_WithSuccessStatus_DoesNotRecordFailureMetric()
    {
        var measurements =
            new List<FailureMeasurement>();

        using var listener =
            CreateListener(
                measurements);

        var middleware =
            new TransferMetricsMiddleware(
                context =>
                {
                    context.Response.StatusCode =
                        StatusCodes.Status201Created;

                    return Task.CompletedTask;
                });

        var context =
            CreateContext(
                HttpMethods.Post,
                "/api/transfers");

        await middleware.InvokeAsync(
            context);

        Assert.Empty(
            measurements);
    }

    [Fact]
    public async Task GetTransfer_WithFailureStatus_DoesNotRecordExecutionFailureMetric()
    {
        var measurements =
            new List<FailureMeasurement>();

        using var listener =
            CreateListener(
                measurements);

        var middleware =
            new TransferMetricsMiddleware(
                context =>
                {
                    context.Response.StatusCode =
                        StatusCodes.Status404NotFound;

                    return Task.CompletedTask;
                });

        var context =
            CreateContext(
                HttpMethods.Get,
                "/api/transfers/"
                + Guid.NewGuid());

        await middleware.InvokeAsync(
            context);

        Assert.Empty(
            measurements);
    }

    [Fact]
    public async Task PostTransfer_WithUnhandledException_RecordsServerFailureAndRethrows()
    {
        var measurements =
            new List<FailureMeasurement>();

        using var listener =
            CreateListener(
                measurements);

        var middleware =
            new TransferMetricsMiddleware(
                _ =>
                    Task.FromException(
                        new InvalidOperationException(
                            "Forced failure.")));

        var context =
            CreateContext(
                HttpMethods.Post,
                "/api/transfers");

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                middleware.InvokeAsync(
                    context));

        var measurement =
            Assert.Single(
                measurements);

        Assert.Equal(
            1,
            measurement.Value);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            measurement.StatusCode);
    }

    private static DefaultHttpContext CreateContext(
        string method,
        string path)
    {
        var context =
            new DefaultHttpContext();

        context.Request.Method =
            method;

        context.Request.Path =
            path;

        return context;
    }

    private static MeterListener CreateListener(
        ICollection<FailureMeasurement> measurements)
    {
        var listener =
            new MeterListener();

        listener.InstrumentPublished =
            (
                instrument,
                meterListener
            ) =>
            {
                if (
                    instrument.Meter.Name
                        == ApiMetrics.MeterName
                    && instrument.Name
                        == FailureMetricName)
                {
                    meterListener
                        .EnableMeasurementEvents(
                            instrument);
                }
            };

        listener.SetMeasurementEventCallback<long>(
            (
                _,
                value,
                tags,
                _
            ) =>
            {
                int? statusCode =
                    null;

                foreach (var tag in tags)
                {
                    if (
                        tag.Key
                            == "status_code"
                        && tag.Value
                            is int parsedStatusCode)
                    {
                        statusCode =
                            parsedStatusCode;
                    }
                }

                measurements.Add(
                    new FailureMeasurement(
                        value,
                        statusCode));
            });

        listener.Start();

        return listener;
    }

    private sealed record FailureMeasurement(
        long Value,
        int? StatusCode);
}
