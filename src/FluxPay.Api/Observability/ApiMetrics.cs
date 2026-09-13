using System.Diagnostics.Metrics;

namespace FluxPay.Api.Observability;

public static class ApiMetrics
{
    public const string MeterName =
        "FluxPay.Api";

    private static readonly Meter Meter =
        new(
            MeterName);

    private static readonly Counter<long> TransferExecutions =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.transfer.executions",
            unit:
                "{transfer}",
            description:
                "Number of successfully handled transfer execution requests.");

    private static readonly Counter<long> TransferFailures =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.transfer.failures",
            unit:
                "{transfer}",
            description:
                "Number of failed transfer execution requests.");

    public static void RecordTransferExecution(
        bool isReplay)
    {
        TransferExecutions.Add(
            1,
            new KeyValuePair<string, object?>(
                "replay",
                isReplay));
    }

    public static void RecordTransferFailure(
        int statusCode)
    {
        TransferFailures.Add(
            1,
            new KeyValuePair<string, object?>(
                "status_code",
                statusCode));
    }
}
