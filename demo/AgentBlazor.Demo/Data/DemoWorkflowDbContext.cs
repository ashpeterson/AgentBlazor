using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

internal sealed class DemoWorkflowDbContext(DbContextOptions<DemoWorkflowDbContext> options) : DbContext(options)
{
    public DbSet<SupplierEntity> Suppliers => Set<SupplierEntity>();
    public DbSet<OnboardingRequestEntity> OnboardingRequests => Set<OnboardingRequestEntity>();
    public DbSet<MitigationTaskEntity> MitigationTasks => Set<MitigationTaskEntity>();

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

        var mitigation = modelBuilder.Entity<MitigationTaskEntity>();
        mitigation.ToTable("mitigation_tasks");
        mitigation.HasKey(static x => x.Id);
        mitigation.HasIndex(static x => x.TaskId).IsUnique();
        mitigation.HasIndex(static x => x.SupplierId);
        mitigation.Property(static x => x.TaskId).IsRequired();
        mitigation.Property(static x => x.SupplierName).IsRequired();
        mitigation.Property(static x => x.ServiceName).IsRequired();
        mitigation.Property(static x => x.Owner).IsRequired();
        mitigation.Property(static x => x.MitigationAction).IsRequired();
        mitigation.Property(static x => x.Status).HasMaxLength(32);
        mitigation.Property(static x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
