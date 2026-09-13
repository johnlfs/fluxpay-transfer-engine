using System.Diagnostics;
using System.Text;
using FluxPay.Infrastructure.Observability;

namespace FluxPay.IntegrationTests.Observability;

public sealed class TraceContextHeadersTests
{
    [Fact]
    public void Inject_WithActiveW3CActivity_WritesCurrentTraceContext()
    {
        var previousActivity =
            Activity.Current;

        using var activity =
            new Activity(
                "fluxpay-header-test");

        activity.SetIdFormat(
            ActivityIdFormat.W3C);

        activity.TraceStateString =
            "fluxpay=test";

        activity.Start();

        try
        {
            Assert.NotNull(
                activity.Id);

            var headers =
                new Dictionary<string, object?>();

            TraceContextHeaders.Inject(
                headers);

            Assert.True(
                headers.TryGetValue(
                    TraceContextHeaders.TraceParentHeaderName,
                    out var traceParentValue));

            Assert.True(
                headers.TryGetValue(
                    TraceContextHeaders.TraceStateHeaderName,
                    out var traceStateValue));

            var traceParent =
                Encoding.UTF8.GetString(
                    Assert.IsType<byte[]>(
                        traceParentValue));

            var traceState =
                Encoding.UTF8.GetString(
                    Assert.IsType<byte[]>(
                        traceStateValue));

            Assert.Equal(
                activity.Id,
                traceParent);

            Assert.Equal(
                activity.TraceStateString,
                traceState);
        }
        finally
        {
            activity.Stop();

            Activity.Current =
                previousActivity;
        }
    }

    [Fact]
    public void Inject_WithoutActiveActivity_DoesNotWriteTraceHeaders()
    {
        var previousActivity =
            Activity.Current;

        Activity.Current =
            null;

        try
        {
            var headers =
                new Dictionary<string, object?>
                {
                    [
                        "x-fluxpay-test"
                    ] =
                        1
                };

            TraceContextHeaders.Inject(
                headers);

            Assert.False(
                headers.ContainsKey(
                    TraceContextHeaders.TraceParentHeaderName));

            Assert.False(
                headers.ContainsKey(
                    TraceContextHeaders.TraceStateHeaderName));

            Assert.Equal(
                1,
                headers[
                    "x-fluxpay-test"]);
        }
        finally
        {
            Activity.Current =
                previousActivity;
        }
    }

    [Fact]
    public void TryExtract_WithInjectedHeaders_ReconstructsRemoteContext()
    {
        var previousActivity =
            Activity.Current;

        var headers =
            new Dictionary<string, object?>();

        ActivityContext expectedContext;

        string? expectedTraceState;

        using (
            var activity =
                new Activity(
                    "fluxpay-extraction-test"))
        {
            activity.SetIdFormat(
                ActivityIdFormat.W3C);

            activity.TraceStateString =
                "fluxpay=test";

            activity.Start();

            try
            {
                TraceContextHeaders.Inject(
                    headers);

                expectedContext =
                    activity.Context;

                expectedTraceState =
                    activity.TraceStateString;
            }
            finally
            {
                activity.Stop();

                Activity.Current =
                    previousActivity;
            }
        }

        var extracted =
            TraceContextHeaders.TryExtract(
                headers,
                isRemote:
                    true,
                out var extractedContext);

        Assert.True(
            extracted);

        Assert.True(
            extractedContext.IsRemote);

        Assert.Equal(
            expectedContext.TraceId,
            extractedContext.TraceId);

        Assert.Equal(
            expectedContext.SpanId,
            extractedContext.SpanId);

        Assert.Equal(
            expectedContext.TraceFlags,
            extractedContext.TraceFlags);

        Assert.Equal(
            expectedTraceState,
            extractedContext.TraceState);
    }

    [Fact]
    public void Inject_WithOversizedTraceState_DropsTraceStateButKeepsTraceParent()
    {
        var previousActivity =
            System.Diagnostics.Activity.Current;

        using var activity =
            new System.Diagnostics.Activity(
                "oversized-tracestate");

        activity.SetIdFormat(
            System.Diagnostics.ActivityIdFormat.W3C);

        activity.TraceStateString =
            new string(
                'a',
                513);

        activity.Start();

        try
        {
            var headers =
                new Dictionary<string, object?>();

            FluxPay.Infrastructure.Observability.TraceContextHeaders.Inject(
                headers);

            Assert.True(
                headers.ContainsKey(
                    FluxPay.Infrastructure.Observability.TraceContextHeaders.TraceParentHeaderName));

            Assert.False(
                headers.ContainsKey(
                    FluxPay.Infrastructure.Observability.TraceContextHeaders.TraceStateHeaderName));
        }
        finally
        {
            activity.Stop();

            System.Diagnostics.Activity.Current =
                previousActivity;
        }
    }

    [Fact]
    public void TryExtract_WithOversizedTraceState_PreservesTraceParent()
    {
        const string traceParent =
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        var headers =
            new Dictionary<string, object?>
            {
                [
                    FluxPay.Infrastructure.Observability.TraceContextHeaders.TraceParentHeaderName
                ] =
                    System.Text.Encoding.UTF8.GetBytes(
                        traceParent),

                [
                    FluxPay.Infrastructure.Observability.TraceContextHeaders.TraceStateHeaderName
                ] =
                    System.Text.Encoding.UTF8.GetBytes(
                        new string(
                            'a',
                            513))
            };

        var extracted =
            FluxPay.Infrastructure.Observability.TraceContextHeaders.TryExtract(
                headers,
                isRemote:
                    true,
                out var context);

        Assert.True(
            extracted);

        Assert.Equal(
            "4bf92f3577b34da6a3ce929d0e0e4736",
            context.TraceId.ToHexString());

        Assert.Null(
            context.TraceState);
    }

}
