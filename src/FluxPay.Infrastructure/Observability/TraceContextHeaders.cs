using System.Diagnostics;
using System.Text;

namespace FluxPay.Infrastructure.Observability;

public static class TraceContextHeaders
{
    public const string TraceParentHeaderName =
        "traceparent";

    public const string TraceStateHeaderName =
        "tracestate";

    private const int MaximumHeaderLength =
        512;

    public static void Inject(
        IDictionary<string, object?> headers)
    {
        ArgumentNullException.ThrowIfNull(
            headers);

        var activity =
            Activity.Current;

        if (
            activity is null
            || activity.IdFormat
                != ActivityIdFormat.W3C
            || string.IsNullOrWhiteSpace(
                activity.Id))
        {
            return;
        }

        headers[
            TraceParentHeaderName
        ] =
            Encoding.UTF8.GetBytes(
                activity.Id);

        if (
            string.IsNullOrWhiteSpace(
                activity.TraceStateString))
        {
            headers.Remove(
                TraceStateHeaderName);

            return;
        }

        headers[
            TraceStateHeaderName
        ] =
            Encoding.UTF8.GetBytes(
                activity.TraceStateString);
    }

    public static bool TryExtract(
        IDictionary<string, object?>? headers,
        bool isRemote,
        out ActivityContext context)
    {
        context =
            default;

        if (
            !TryReadHeader(
                headers,
                TraceParentHeaderName,
                out var traceParent)
            || string.IsNullOrWhiteSpace(
                traceParent))
        {
            return false;
        }

        if (traceParent.Length > MaximumHeaderLength)
        {
            return false;
        }

        _ =
            TryReadHeader(
                headers,
                TraceStateHeaderName,
                out var traceState);

        if (
            traceState is not null
            && traceState.Length
                > MaximumHeaderLength)
        {
            return false;
        }

        return ActivityContext.TryParse(
            traceParent,
            string.IsNullOrWhiteSpace(
                traceState)
                ? null
                : traceState,
            isRemote,
            out context);
    }

    private static bool TryReadHeader(
        IDictionary<string, object?>? headers,
        string name,
        out string? value)
    {
        value =
            null;

        if (
            headers is null
            || !headers.TryGetValue(
                name,
                out var rawValue)
            || rawValue is null)
        {
            return false;
        }

        value =
            rawValue switch
            {
                byte[] bytes =>
                    Encoding.UTF8.GetString(
                        bytes),

                ReadOnlyMemory<byte> memory =>
                    Encoding.UTF8.GetString(
                        memory.Span),

                string text =>
                    text,

                _ =>
                    null
            };

        return value is not null;
    }
}
