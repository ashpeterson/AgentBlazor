using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

internal sealed class DemoWorkflowDbContext(DbContextOptions<DemoWorkflowDbContext> options) : DbContext(options)
{
    public DbSet<DojoWorkspaceEntity> DojoWorkspaces => Set<DojoWorkspaceEntity>();
    public DbSet<DojoIngredientEntity> DojoIngredients => Set<DojoIngredientEntity>();
    public DbSet<DojoStepEntity> DojoSteps => Set<DojoStepEntity>();
    public DbSet<DojoRunNoteEntity> DojoRunNotes => Set<DojoRunNoteEntity>();
    public DbSet<DemoFileWorkflowFileEntity> FileWorkflowFiles => Set<DemoFileWorkflowFileEntity>();
    public DbSet<DemoFileWorkflowEventEntity> FileWorkflowEvents => Set<DemoFileWorkflowEventEntity>();
    public DbSet<DemoFileWorkflowJobEntity> FileWorkflowJobs => Set<DemoFileWorkflowJobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dojoWorkspace = modelBuilder.Entity<DojoWorkspaceEntity>();
        dojoWorkspace.ToTable("dojo_workspaces");
        dojoWorkspace.HasKey(static x => x.Id);
        dojoWorkspace.HasIndex(static x => x.SessionKey).IsUnique();
        dojoWorkspace.Property(static x => x.SessionKey).IsRequired();
        dojoWorkspace.Property(static x => x.Title).IsRequired();
        dojoWorkspace.Property(static x => x.Difficulty).IsRequired();
        dojoWorkspace.Property(static x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        dojoWorkspace.Property(static x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var dojoIngredient = modelBuilder.Entity<DojoIngredientEntity>();
        dojoIngredient.ToTable("dojo_ingredients");
        dojoIngredient.HasKey(static x => x.Id);
        dojoIngredient.HasIndex(static x => x.SessionKey);
        dojoIngredient.HasIndex(static x => new { x.SessionKey, x.IngredientId }).IsUnique();
        dojoIngredient.Property(static x => x.SessionKey).IsRequired();
        dojoIngredient.Property(static x => x.IngredientId).IsRequired();
        dojoIngredient.Property(static x => x.Name).IsRequired();
        dojoIngredient.Property(static x => x.Amount).IsRequired();
        dojoIngredient.Property(static x => x.SortOrder).IsRequired();

        var dojoStep = modelBuilder.Entity<DojoStepEntity>();
        dojoStep.ToTable("dojo_steps");
        dojoStep.HasKey(static x => x.Id);
        dojoStep.HasIndex(static x => x.SessionKey);
        dojoStep.HasIndex(static x => new { x.SessionKey, x.StepNumber }).IsUnique();
        dojoStep.Property(static x => x.SessionKey).IsRequired();
        dojoStep.Property(static x => x.StepNumber).IsRequired();
        dojoStep.Property(static x => x.Text).IsRequired();

        var dojoNote = modelBuilder.Entity<DojoRunNoteEntity>();
        dojoNote.ToTable("dojo_run_notes");
        dojoNote.HasKey(static x => x.Id);
        dojoNote.HasIndex(static x => x.SessionKey);
        dojoNote.HasIndex(static x => new { x.SessionKey, x.TimestampUtc });
        dojoNote.Property(static x => x.SessionKey).IsRequired();
        dojoNote.Property(static x => x.TimestampUtc).IsRequired();
        dojoNote.Property(static x => x.Message).IsRequired();

        var fileWorkflowFile = modelBuilder.Entity<DemoFileWorkflowFileEntity>();
        fileWorkflowFile.ToTable("demo_file_workflow_files");
        fileWorkflowFile.HasKey(static x => x.Id);
        fileWorkflowFile.HasIndex(static x => x.SessionKey);
        fileWorkflowFile.HasIndex(static x => new { x.SessionKey, x.FileName }).IsUnique();
        fileWorkflowFile.Property(static x => x.SessionKey).IsRequired();
        fileWorkflowFile.Property(static x => x.FileName).IsRequired();
        fileWorkflowFile.Property(static x => x.UploadMode).IsRequired();
        fileWorkflowFile.Property(static x => x.AddedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        fileWorkflowFile.Property(static x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var fileWorkflowEvent = modelBuilder.Entity<DemoFileWorkflowEventEntity>();
        fileWorkflowEvent.ToTable("demo_file_workflow_events");
        fileWorkflowEvent.HasKey(static x => x.Id);
        fileWorkflowEvent.HasIndex(static x => x.SessionKey);
        fileWorkflowEvent.HasIndex(static x => new { x.SessionKey, x.TimestampUtc });
        fileWorkflowEvent.Property(static x => x.SessionKey).IsRequired();
        fileWorkflowEvent.Property(static x => x.TimestampUtc).IsRequired();
        fileWorkflowEvent.Property(static x => x.EventType).IsRequired();
        fileWorkflowEvent.Property(static x => x.FileName).IsRequired();
        fileWorkflowEvent.Property(static x => x.Message).IsRequired();

        var fileWorkflowJob = modelBuilder.Entity<DemoFileWorkflowJobEntity>();
        fileWorkflowJob.ToTable("demo_file_workflow_jobs");
        fileWorkflowJob.HasKey(static x => x.Id);
        fileWorkflowJob.HasIndex(static x => x.SessionKey);
        fileWorkflowJob.HasIndex(static x => x.JobId).IsUnique();
        fileWorkflowJob.HasIndex(static x => new { x.SessionKey, x.UpdatedUtc });
        fileWorkflowJob.Property(static x => x.SessionKey).IsRequired();
        fileWorkflowJob.Property(static x => x.JobId).IsRequired();
        fileWorkflowJob.Property(static x => x.Operation).IsRequired();
        fileWorkflowJob.Property(static x => x.FileName).IsRequired();
        fileWorkflowJob.Property(static x => x.UploadMode).IsRequired();
        fileWorkflowJob.Property(static x => x.Status).IsRequired();
        fileWorkflowJob.Property(static x => x.Message).IsRequired();
        fileWorkflowJob.Property(static x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        fileWorkflowJob.Property(static x => x.UpdatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
