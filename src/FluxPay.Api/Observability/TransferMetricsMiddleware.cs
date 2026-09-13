namespace FluxPay.Api.Observability;

public sealed class TransferMetricsMiddleware
{
    private const string TransferExecutionPath =
        "/api/transfers";

    private readonly RequestDelegate _next;

    public TransferMetricsMiddleware(
        RequestDelegate next)
    {
        _next =
            next;
    }

    public async Task InvokeAsync(
        HttpContext context)
    {
        if (!IsTransferExecutionRequest(
                context))
        {
            await _next(
                context);

            return;
        }

        try
        {
            await _next(
                context);
        }
        catch
        {
            ApiMetrics.RecordTransferFailure(
                StatusCodes.Status500InternalServerError);

            throw;
        }

        if (
            context.Response.StatusCode
            >= StatusCodes.Status400BadRequest)
        {
            ApiMetrics.RecordTransferFailure(
                context.Response.StatusCode);
        }
    }

    private static bool IsTransferExecutionRequest(
        HttpContext context)
    {
        if (!HttpMethods.IsPost(
                context.Request.Method))
        {
            return false;
        }

        var path =
            context.Request.Path.Value?
                .TrimEnd(
                    '/');

        return string.Equals(
            path,
            TransferExecutionPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
