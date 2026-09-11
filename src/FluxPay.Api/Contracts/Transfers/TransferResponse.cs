namespace FluxPay.Api.Contracts.Transfers;

public sealed record TransferResponse(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt);
