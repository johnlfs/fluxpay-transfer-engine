using FluxPay.Api.ErrorHandling;
using FluxPay.Application.Accounts.CreateAccount;
using FluxPay.Application.Accounts.GetAccount;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Application.Transfers.GetTransfer;
using FluxPay.Infrastructure;
using FluxPay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder =
    WebApplication.CreateBuilder(
        args);

var connectionString =
    builder.Configuration[
        "FLUXPAY_DB_CONNECTION"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Environment variable 'FLUXPAY_DB_CONNECTION' is required.");
}

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddProblemDetails();

builder.Services.AddExceptionHandler<
    GlobalExceptionHandler>();

builder.Services.AddInfrastructure(
    connectionString);

builder.Services.AddScoped<
    CreateAccountHandler>();

builder.Services.AddScoped<
    GetAccountHandler>();

builder.Services.AddScoped<
    ExecuteTransferHandler>();

builder.Services.AddScoped<
    GetTransferHandler>();

builder.Services.AddSingleton(
    TimeProvider.System);

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<FluxPayDbContext>(
        name:
            "postgresql",
        tags:
            [
                "ready"
            ]);

var app =
    builder.Build();

app.UseExceptionHandler();

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate =
            _ =>
                false
    });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate =
            registration =>
                registration.Tags.Contains(
                    "ready")
    });

app.MapControllers();

app.Run();
