using System.Reflection;
using FluxPay.Api.Controllers;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Domain.Accounts;
using FluxPay.Infrastructure;

namespace FluxPay.ArchitectureTests.Dependencies;

public sealed class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly =
        typeof(Account).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(ExecuteTransferHandler).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(DependencyInjection).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(AccountsController).Assembly;

    [Fact]
    public void Domain_MustNotDependOnApplicationInfrastructureApiOrWorker()
    {
        AssertDoesNotReference(
            DomainAssembly,
            "FluxPay.Application",
            "FluxPay.Infrastructure",
            "FluxPay.Api",
            "FluxPay.Worker");
    }

    [Fact]
    public void Domain_MustNotDependOnPersistenceMessagingOrWebFrameworks()
    {
        AssertDoesNotReferencePrefixes(
            DomainAssembly,
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Npgsql",
            "RabbitMQ");
    }

    [Fact]
    public void Application_MustNotDependOnInfrastructureApiOrWorker()
    {
        AssertDoesNotReference(
            ApplicationAssembly,
            "FluxPay.Infrastructure",
            "FluxPay.Api",
            "FluxPay.Worker");
    }

    [Fact]
    public void Application_MustNotDependOnPersistenceMessagingOrWebFrameworks()
    {
        AssertDoesNotReferencePrefixes(
            ApplicationAssembly,
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Npgsql",
            "RabbitMQ");
    }

    [Fact]
    public void Infrastructure_MustNotDependOnApi()
    {
        AssertDoesNotReference(
            InfrastructureAssembly,
            "FluxPay.Api");
    }

    [Fact]
    public void Api_MustNotDependOnWorker()
    {
        AssertDoesNotReference(
            ApiAssembly,
            "FluxPay.Worker");
    }

    [Fact]
    public void Worker_MustNotDependOnApi()
    {
        var workerAssembly =
            Assembly.Load(
                "FluxPay.Worker");

        AssertDoesNotReference(
            workerAssembly,
            "FluxPay.Api");
    }

    private static void AssertDoesNotReference(
        Assembly assembly,
        params string[] forbiddenAssemblies)
    {
        var references =
            GetReferencedAssemblyNames(
                assembly);

        var violations =
            forbiddenAssemblies
                .Where(
                    forbiddenAssembly =>
                        references.Contains(
                            forbiddenAssembly))
                .OrderBy(
                    name =>
                        name)
                .ToArray();

        Assert.True(
            violations.Length == 0,
            BuildViolationMessage(
                assembly,
                violations));
    }

    private static void AssertDoesNotReferencePrefixes(
        Assembly assembly,
        params string[] forbiddenPrefixes)
    {
        var references =
            GetReferencedAssemblyNames(
                assembly);

        var violations =
            references
                .Where(
                    reference =>
                        forbiddenPrefixes.Any(
                            forbiddenPrefix =>
                                reference.StartsWith(
                                    forbiddenPrefix,
                                    StringComparison.Ordinal)))
                .OrderBy(
                    name =>
                        name)
                .ToArray();

        Assert.True(
            violations.Length == 0,
            BuildViolationMessage(
                assembly,
                violations));
    }

    private static HashSet<string> GetReferencedAssemblyNames(
        Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Where(
                reference =>
                    reference.Name is not null)
            .Select(
                reference =>
                    reference.Name!)
            .ToHashSet(
                StringComparer.Ordinal);
    }

    private static string BuildViolationMessage(
        Assembly assembly,
        IReadOnlyCollection<string> violations)
    {
        return
            $"Assembly '{assembly.GetName().Name}' contains forbidden dependencies: "
            + string.Join(
                ", ",
                violations);
    }
}
