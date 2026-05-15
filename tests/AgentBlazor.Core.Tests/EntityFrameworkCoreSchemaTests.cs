using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Data;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using AgentBlazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class EntityFrameworkCoreSchemaTests
{
    [Fact]
    public void WithDataSchemas_PreservesExplicitAgentOptIn()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddAgent("support-agent", agent => agent.WithDataSchemas("support-data"))
            .AddAgent("file-agent");

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentRegistry>();

        Assert.True(registry.TryGet("support-agent", out var supportAgent));
        Assert.Contains("support-data", supportAgent.AllowedDataSchemas);

        Assert.True(registry.TryGet("file-agent", out var fileAgent));
        Assert.Empty(fileAgent.AllowedDataSchemas);
    }

    [Fact]
    public void AddDataSchema_RegistersSchemaInCatalog()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddDataSchema(new AgentDataSchemaSet
            {
                Name = "support-data",
                Description = "Support ticket data.",
                Entities =
                [
                    new AgentEntitySchema
                    {
                        Name = "tickets",
                        Properties =
                        [
                            new AgentEntityPropertySchema
                            {
                                Name = "Id",
                                Type = "string",
                                IsKey = true
                            }
                        ]
                    }
                ]
            });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentDataSchemaCatalog>();

        Assert.True(catalog.TryGet("support-data", out var schema));
        Assert.Equal("Support ticket data.", schema.Description);
        var entity = Assert.Single(schema.Entities);
        Assert.Equal("tickets", entity.Name);
        var property = Assert.Single(entity.Properties);
        Assert.Equal("Id", property.Name);
        Assert.True(property.IsKey);
    }

    [Fact]
    public void AddEntitySchema_ExposesOnlyAllowlistedProperties()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchemaTestDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        services.AddAgentBlazorServices()
            .AddEntitySchema<SchemaTestDbContext>("support-data", schema =>
            {
                schema.WithDescription("Read-safe support ticket shape.");
                schema.Entity<SupportTicket>("support_tickets", entity =>
                {
                    entity.WithDescription("Support tickets visible to the workflow.");
                    entity.Property(ticket => ticket.Id, "Ticket identifier.");
                    entity.Property(ticket => ticket.Status, "Current status.");
                    entity.Property(ticket => ticket.CreatedUtc, "Creation timestamp.");
                });
            });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentDataSchemaCatalog>();

        Assert.True(catalog.TryGet("support-data", out var schema));
        Assert.Equal("Read-safe support ticket shape.", schema.Description);

        var entitySchema = Assert.Single(schema.Entities);
        Assert.Equal("support_tickets", entitySchema.Name);
        Assert.Equal(typeof(SupportTicket).FullName, entitySchema.ClrTypeName);
        Assert.Equal("Support tickets visible to the workflow.", entitySchema.Description);

        Assert.Collection(
            entitySchema.Properties,
            property =>
            {
                Assert.Equal("Id", property.Name);
                Assert.Equal("string", property.Type);
                Assert.True(property.IsKey);
                Assert.Equal("Ticket identifier.", property.Description);
            },
            property =>
            {
                Assert.Equal("Status", property.Name);
                Assert.Equal("string", property.Type);
                Assert.False(property.IsKey);
            },
            property =>
            {
                Assert.Equal("CreatedUtc", property.Name);
                Assert.Equal("datetime", property.Type);
                Assert.False(property.IsKey);
            });

        Assert.DoesNotContain(entitySchema.Properties, property => property.Name == nameof(SupportTicket.CustomerEmail));
    }

    [Fact]
    public void AddEntitySchema_RequiresDbContextFactory()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddEntitySchema<SchemaTestDbContext>("support-data", schema =>
            {
                schema.Entity<SupportTicket>("support_tickets", entity =>
                {
                    entity.Property(ticket => ticket.Id);
                });
            });

        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IAgentDataSchemaCatalog>());
        Assert.Contains("requires IDbContextFactory<SchemaTestDbContext>", error.Message);
    }

    [Fact]
    public async Task RuntimeAdapter_IncludesOnlyOptedInDataSchemasInInstructions()
    {
        var chatClient = new InstructionCapturingChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddDataSchema(new AgentDataSchemaSet
            {
                Name = "support-data",
                Entities =
                [
                    new AgentEntitySchema
                    {
                        Name = "support_tickets",
                        Properties =
                        [
                            new AgentEntityPropertySchema { Name = "Id", Type = "string", IsKey = true }
                        ]
                    }
                ]
            })
            .AddWorkflow<SchemaProbeCapabilities>("support-agent", agent =>
            {
                agent.WithDataSchemas("support-data");
            })
            .AddWorkflow<SchemaProbeCapabilities>("file-agent");

        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("hello", AgentName: "support-agent"));
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("hello", AgentName: "file-agent"));

        Assert.Contains("support_tickets", chatClient.Instructions[0]);
        Assert.DoesNotContain("support_tickets", chatClient.Instructions[1] ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SchemaTestDbContext(DbContextOptions<SchemaTestDbContext> options) : DbContext(options)
    {
        public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SupportTicket>(entity =>
            {
                entity.HasKey(ticket => ticket.Id);
                entity.Property(ticket => ticket.Id).IsRequired();
                entity.Property(ticket => ticket.Status).IsRequired();
                entity.Property(ticket => ticket.CustomerEmail).IsRequired();
            });
        }
    }

    private sealed class SupportTicket
    {
        public string Id { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTimeOffset CreatedUtc { get; set; }

        public string CustomerEmail { get; set; } = string.Empty;
    }

    [AgentCapability("schema_probe")]
    private sealed class SchemaProbeCapabilities
    {
        [AgentAction("Run schema probe")]
        public CapabilityResult RunProbe() => CapabilityResult.Success("Schema probe completed.");
    }

    private sealed class InstructionCapturingChatClient : IChatClient
    {
        public List<string?> Instructions { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = cancellationToken;
            Instructions.Add(options?.Instructions);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
