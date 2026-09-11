namespace FluxPay.Application.Transfers.Exceptions;

public sealed class IdempotencyKeyConflictException : Exception
{
    public IdempotencyKeyConflictException(
        Guid idempotencyKey)
        : base(
            $"Idempotency key '{idempotencyKey}' was already used with a different transfer payload.")
    {
        IdempotencyKey =
            idempotencyKey;
    }

    public Guid IdempotencyKey { get; }
}
