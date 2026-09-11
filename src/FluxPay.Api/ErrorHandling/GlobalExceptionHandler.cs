using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FluxPay.Api.ErrorHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails =
            exception switch
            {
                AccountNotFoundException =>
                    CreateProblemDetails(
                        StatusCodes.Status404NotFound,
                        "Account not found",
                        exception.Message,
                        "urn:fluxpay:error:account-not-found"),

                AccountNumberAlreadyExistsException =>
                    CreateProblemDetails(
                        StatusCodes.Status409Conflict,
                        "Account number already exists",
                        exception.Message,
                        "urn:fluxpay:error:account-number-already-exists"),

                DomainValidationException =>
                    CreateProblemDetails(
                        StatusCodes.Status400BadRequest,
                        "Domain validation failed",
                        exception.Message,
                        "urn:fluxpay:error:domain-validation"),

                _ => null
            };

        if (problemDetails is null)
        {
            return false;
        }

        problemDetails.Instance =
            httpContext.Request.Path;

        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        httpContext.Response.StatusCode =
            problemDetails.Status
            ?? StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception
            });
    }

    private static ProblemDetails CreateProblemDetails(
        int status,
        string title,
        string detail,
        string type)
    {
        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = type
        };
    }
}
