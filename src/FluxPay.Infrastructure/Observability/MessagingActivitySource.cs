using System.Diagnostics;

namespace FluxPay.Infrastructure.Observability;

public static class MessagingActivitySource
{
    public const string Name =
        "FluxPay.Messaging";

    public static ActivitySource Source { get; } =
        new(
            Name);
}
