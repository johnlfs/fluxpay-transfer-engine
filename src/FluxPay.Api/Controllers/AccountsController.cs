using FluxPay.Api.Contracts.Accounts;
using FluxPay.Application.Accounts.CreateAccount;
using FluxPay.Application.Accounts.GetAccount;
using Microsoft.AspNetCore.Mvc;

namespace FluxPay.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController : ControllerBase
{
    private const string GetAccountByIdRouteName =
        "GetAccountById";

    private readonly CreateAccountHandler _createAccountHandler;
    private readonly GetAccountHandler _getAccountHandler;

    public AccountsController(
        CreateAccountHandler createAccountHandler,
        GetAccountHandler getAccountHandler)
    {
        _createAccountHandler = createAccountHandler;
        _getAccountHandler = getAccountHandler;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(CreateAccountResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateAccountResponse>> CreateAsync(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new CreateAccountCommand(
                request.AccountNumber,
                request.OwnerName,
                request.InitialBalance);

        var result =
            await _createAccountHandler.HandleAsync(
                command,
                cancellationToken);

        var response =
            new CreateAccountResponse(
                result.Id,
                result.AccountNumber,
                result.OwnerName,
                result.Balance,
                result.CreatedAt);

        return CreatedAtRoute(
            GetAccountByIdRouteName,
            new
            {
                accountId = result.Id
            },
            response);
    }

    [HttpGet(
        "{accountId:guid}",
        Name = GetAccountByIdRouteName)]
    [ProducesResponseType(
        typeof(AccountResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> GetByIdAsync(
        [FromRoute] Guid accountId,
        CancellationToken cancellationToken)
    {
        var result =
            await _getAccountHandler.HandleAsync(
                new GetAccountQuery(accountId),
                cancellationToken);

        var response =
            new AccountResponse(
                result.Id,
                result.AccountNumber,
                result.OwnerName,
                result.Balance,
                result.CreatedAt,
                result.UpdatedAt);

        return Ok(response);
    }
}
