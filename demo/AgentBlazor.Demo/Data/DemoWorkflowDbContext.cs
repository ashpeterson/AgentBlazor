using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

internal sealed class DemoWorkflowDbContext(DbContextOptions<DemoWorkflowDbContext> options) : DbContext(options)
{
    public DbSet<SupplierEntity> Suppliers => Set<SupplierEntity>();
    public DbSet<OnboardingRequestEntity> OnboardingRequests => Set<OnboardingRequestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var supplier = modelBuilder.Entity<SupplierEntity>();
        supplier.ToTable("suppliers");
        supplier.HasKey(static x => x.Id);
        supplier.HasIndex(static x => x.SupplierId).IsUnique();
        supplier.Property(static x => x.SupplierId).IsRequired();
        supplier.Property(static x => x.SupplierName).IsRequired();
        supplier.Property(static x => x.Region).IsRequired();
        supplier.Property(static x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var onboarding = modelBuilder.Entity<OnboardingRequestEntity>();
        onboarding.ToTable("onboarding_requests");
        onboarding.HasKey(static x => x.Id);
        onboarding.HasIndex(static x => x.RequestId).IsUnique();
        onboarding.HasIndex(static x => x.SupplierId);
        onboarding.Property(static x => x.RequestId).IsRequired();
        onboarding.Property(static x => x.SupplierId).IsRequired();
        onboarding.Property(static x => x.SupplierName).IsRequired();
        onboarding.Property(static x => x.ContactEmail).IsRequired();
        onboarding.Property(static x => x.Status).HasMaxLength(32);
        onboarding.Property(static x => x.SubmittedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
