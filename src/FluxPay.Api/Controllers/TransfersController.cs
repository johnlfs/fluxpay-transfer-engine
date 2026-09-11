using FluxPay.Api.Contracts.Transfers;
using FluxPay.Api.ErrorHandling;
using FluxPay.Api.Observability;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Application.Transfers.GetTransfer;
using Microsoft.AspNetCore.Mvc;

namespace FluxPay.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController : ControllerBase
{
    private const string GetTransferByIdRouteName =
        "GetTransferById";

    private readonly ExecuteTransferHandler _executeTransferHandler;
    private readonly GetTransferHandler _getTransferHandler;

    public TransfersController(
        ExecuteTransferHandler executeTransferHandler,
        GetTransferHandler getTransferHandler)
    {
        _executeTransferHandler =
            executeTransferHandler;

        _getTransferHandler =
            getTransferHandler;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(TransferResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(TransferResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TransferResponse>> CreateAsync(
        [FromHeader(Name = "Idempotency-Key")]
        string? idempotencyKeyHeader,
        [FromBody]
        CreateTransferRequest request,
        CancellationToken cancellationToken)
    {
        if (
            string.IsNullOrWhiteSpace(
                idempotencyKeyHeader)
            || !Guid.TryParse(
                idempotencyKeyHeader,
                out var idempotencyKey)
            || idempotencyKey
                == Guid.Empty)
        {
            throw new InvalidIdempotencyKeyHeaderException();
        }

        var result =
            await _executeTransferHandler.HandleAsync(
                new ExecuteTransferCommand(
                    idempotencyKey,
                    request.SourceAccountId,
                    request.DestinationAccountId,
                    request.Amount),
                cancellationToken);

        ApiMetrics.RecordTransferExecution(
            result.IsReplay);

        var response =
            new TransferResponse(
                result.Id,
                result.SourceAccountId,
                result.DestinationAccountId,
                result.Amount,
                result.Status.ToString(),
                result.CreatedAt,
                result.FinalizedAt);

        if (result.IsReplay)
        {
            return Ok(
                response);
        }

        return CreatedAtRoute(
            GetTransferByIdRouteName,
            new
            {
                transferId =
                    result.Id
            },
            response);
    }

    [HttpGet(
        "{transferId:guid}",
        Name = GetTransferByIdRouteName)]
    [ProducesResponseType(
        typeof(TransferResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransferResponse>> GetByIdAsync(
        [FromRoute]
        Guid transferId,
        CancellationToken cancellationToken)
    {
        var result =
            await _getTransferHandler.HandleAsync(
                new GetTransferQuery(
                    transferId),
                cancellationToken);

        var response =
            new TransferResponse(
                result.Id,
                result.SourceAccountId,
                result.DestinationAccountId,
                result.Amount,
                result.Status.ToString(),
                result.CreatedAt,
                result.FinalizedAt);

        return Ok(
            response);
    }
}
