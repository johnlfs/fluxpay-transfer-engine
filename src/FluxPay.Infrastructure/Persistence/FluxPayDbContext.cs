using FluxPay.Domain.Accounts;
using FluxPay.Domain.Transfers;
using FluxPay.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence;

public sealed class FluxPayDbContext : DbContext
{
    public FluxPayDbContext(
        DbContextOptions<FluxPayDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts =>
        Set<Account>();

    public DbSet<Transfer> Transfers =>
        Set<Transfer>();

    public DbSet<OutboxMessage> OutboxMessages =>
        Set<OutboxMessage>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FluxPayDbContext).Assembly);

        base.OnModelCreating(
            modelBuilder);
    }
}
