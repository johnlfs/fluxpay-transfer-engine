namespace FluxPay.Api.ErrorHandling;

public sealed class InvalidIdempotencyKeyHeaderException : Exception
{
    public InvalidIdempotencyKeyHeaderException()
        : base(
            "The 'Idempotency-Key' header is required and must contain a non-empty UUID.")
    {
    }
}
