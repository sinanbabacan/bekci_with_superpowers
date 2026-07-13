using Bekci.Application.Abstractions;
using Bekci.Domain;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Data;

public sealed class Repository(DbContextOptions<Repository> options, ITenantContext tenant)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();
    public DbSet<Patrol> Patrols => Set<Patrol>();
    public DbSet<Scan> Scans => Set<Scan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Repository).Assembly);

        modelBuilder.Entity<User>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Site>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Route>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Checkpoint>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Patrol>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Scan>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
    }
}
