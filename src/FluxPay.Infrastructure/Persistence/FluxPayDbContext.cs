using FluxPay.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence;

public sealed class FluxPayDbContext : DbContext
{
    public FluxPayDbContext(
        DbContextOptions<FluxPayDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FluxPayDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
