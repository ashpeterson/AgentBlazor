using AgentBlazor;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Components;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

// Ensure [AgentFlow] logs are visible when running prompts
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Agents.AgentRuntime", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Interfaces.InMemoryAgentNavigationIntentService", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor", LogLevel.Information);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<DojoWorkspaceService>();
builder.Services.AddScoped<DemoFileWorkflowService>();
builder.Services.AddScoped<DojoRecipeReleaseWorkflowService>();
builder.Services.AddScoped<IncidentEscalationWorkflowService>();
builder.Services.AddScoped<SupplierComplianceWorkflowService>();
builder.Services.AddScoped<SupportInboxWorkflowService>();
builder.Services.AddScoped<ResponseOrchestrationWorkflowService>();
builder.Services.AddScoped<ReleaseDossierWorkflowService>();
builder.Services.Configure<DemoSecurityOptions>(builder.Configuration.GetSection(DemoSecurityOptions.SectionName));
builder.Services.Configure<DemoLoggingOptions>(builder.Configuration.GetSection(DemoLoggingOptions.SectionName));
builder.Services.Configure<DemoRemoteStorageOptions>(builder.Configuration.GetSection(DemoRemoteStorageOptions.SectionName));
builder.Services.AddHttpClient("demo-remote-storage");
builder.Services.AddSingleton<IDemoRemoteStorageAdapter, DemoRemoteStorageAdapter>();
builder.Services.AddSingleton<IDemoChatRequestLog, JsonlDemoChatRequestLog>();
builder.Services.AddSingleton<IDemoTrafficLog, JsonlDemoTrafficLog>();

var demoSecurityOptions = builder.Configuration.GetSection(DemoSecurityOptions.SectionName).Get<DemoSecurityOptions>()
    ?? new DemoSecurityOptions();
var proLicenseKey = builder.Configuration["AgentBlazor:LicenseKey"]
    ?? Environment.GetEnvironmentVariable("AGENTBLAZOR_LICENSE_KEY");
var proDataDirectory = builder.Configuration["AgentBlazor:DataDirectory"]
    ?? Environment.GetEnvironmentVariable("AGENTBLAZOR_DATA_DIRECTORY");

var openAiModel = FirstConfigured(builder.Configuration["OpenAI:Model"], "gpt-4o-mini")!;
var openAiApiKey = FirstConfigured(
    Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
    Environment.GetEnvironmentVariable("OpenAI__ApiKey"),
    builder.Configuration["OpenAI:ApiKey"]);
var ollamaModel = FirstConfigured(
    Environment.GetEnvironmentVariable("OLLAMA_MODEL"),
    Environment.GetEnvironmentVariable("Ollama__Model"),
    builder.Configuration["Ollama:Model"]);
var ollamaEndpoint = FirstConfigured(
    Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT"),
    Environment.GetEnvironmentVariable("Ollama__Endpoint"),
    builder.Configuration["Ollama:Endpoint"],
    "http://127.0.0.1:11434/v1")!;
var ollamaApiKey = FirstConfigured(
    Environment.GetEnvironmentVariable("OLLAMA_API_KEY"),
    Environment.GetEnvironmentVariable("Ollama__ApiKey"),
    builder.Configuration["Ollama:ApiKey"]);
var workflowConnectionString = builder.Configuration.GetConnectionString("DemoWorkflow")
    ?? "Data Source=agentblazor-demo.db";
var sharedAgentInstructionsPath = Path.Combine(builder.Environment.ContentRootPath, "agent-instructions.txt");
var sharedAgentInstructions = File.Exists(sharedAgentInstructionsPath)
    ? File.ReadAllText(sharedAgentInstructionsPath)
    : null;

var hasOpenAiProvider = !string.IsNullOrWhiteSpace(openAiApiKey);
var hasOllamaProvider = !string.IsNullOrWhiteSpace(ollamaModel);

if (!builder.Environment.IsDevelopment() &&
    demoSecurityOptions.RequireProviderInProduction &&
    !hasOpenAiProvider &&
    !(demoSecurityOptions.AllowOllamaInProduction && hasOllamaProvider))
{
    throw new InvalidOperationException(
        "The live demo requires a configured provider. Set OPENAI_API_KEY (recommended) or explicitly enable an Ollama production fallback.");
}

builder.Services.PostConfigure<DemoRemoteStorageOptions>(options =>
{
    options.HttpBaseUrl = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_REMOTE_STORAGE_HTTP_BASE_URL"),
        options.HttpBaseUrl);
    options.HttpApiKey = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_REMOTE_STORAGE_HTTP_API_KEY"),
        options.HttpApiKey);
    options.HttpBearerToken = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_REMOTE_STORAGE_HTTP_BEARER_TOKEN"),
        options.HttpBearerToken);
});

builder.Services.PostConfigure<DemoLoggingOptions>(options =>
{
    options.DirectoryPath = FirstConfigured(
            Environment.GetEnvironmentVariable("DEMO_LOG_DIRECTORY"),
            Environment.GetEnvironmentVariable("DemoLogging__DirectoryPath"),
            options.DirectoryPath)
        ?? Path.Combine(Path.GetTempPath(), "agentblazor-demo-logs");
    options.AccessToken = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_LOG_ACCESS_TOKEN"),
        Environment.GetEnvironmentVariable("DemoLogging__AccessToken"),
        options.AccessToken);
});

if (demoSecurityOptions.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"error":"Rate limit exceeded. Try again in a moment."}""",
            token);
    };

    options.AddPolicy(DemoSecurityOptions.AgentEndpointRateLimitPolicyName, httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, demoSecurityOptions.RateLimiting.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, demoSecurityOptions.RateLimiting.WindowSeconds)),
            QueueLimit = Math.Max(0, demoSecurityOptions.RateLimiting.QueueLimit),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });
});

builder.Services.AddDbContextFactory<DemoWorkflowDbContext>(options =>
    options.UseSqlite(workflowConnectionString));
builder.Services.AddSingleton<DemoWorkflowDatabaseSeeder>();

builder.Services.AddAgentBlazor(options =>
{
    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        options.UseOpenAI(openAiApiKey, openAiModel);
    }
    else if (!string.IsNullOrWhiteSpace(ollamaModel))
    {
        options.UseOllama(ollamaModel, ollamaEndpoint, ollamaApiKey);
    }

    //if (builder.Environment.IsDevelopment())
    //{
    //    options.UseDevTools();
    //}

    if (!string.IsNullOrWhiteSpace(proLicenseKey))
    {
        options.UseProLicense(proLicenseKey, proDataDirectory);
    }

    options.UseMiddleware<DemoChatRequestLoggingMiddleware>();

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.EnablePromptTracing();

        agentBuilder.AddDataSchema(new AgentDataSchemaSet
        {
            Name = "support-data",
            Description = "Read-safe support ticket fields used by the support inbox workflow. This is planning context only; ticket reads and drafts still go through typed workflow actions.",
            Entities =
            [
                new AgentEntitySchema
                {
                    Name = "support_tickets",
                    Description = "Support tickets visible in the demo queue.",
                    ClrTypeName = typeof(SupportTicketRow).FullName,
                    Properties =
                    [
                        new AgentEntityPropertySchema { Name = "Id", Type = "string", IsKey = true, Description = "Ticket identifier such as TCK-1042." },
                        new AgentEntityPropertySchema { Name = "Subject", Type = "string", Description = "Customer-visible ticket subject." },
                        new AgentEntityPropertySchema { Name = "Team", Type = "string", Description = "Owning support team." },
                        new AgentEntityPropertySchema { Name = "Priority", Type = "string", Description = "Priority label such as High or Medium." },
                        new AgentEntityPropertySchema { Name = "AgeDays", Type = "integer", Description = "Age of the ticket in days." },
                        new AgentEntityPropertySchema { Name = "WaitingOnReply", Type = "boolean", Description = "Whether the customer is waiting on a support reply." },
                        new AgentEntityPropertySchema { Name = "EscalationRisk", Type = "boolean", Description = "Whether the ticket is at risk of escalation." },
                        new AgentEntityPropertySchema { Name = "MissingEvidence", Type = "boolean", Description = "Whether draft preparation is blocked by missing evidence." }
                    ]
                }
            ]
        });

        agentBuilder.AddAgent("Workflow Hub Agent", agent =>
        {
            agent.WithDescription("Focused on routing users toward the right semantic workflow showcase and explaining the workflow-first product story.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
        });

        agentBuilder.AddAgent("Supplier Analyst Agent", agent =>
        {
            agent.WithDescription("Focused on the component reference surface for data-centric controls and selection patterns.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDataGrid", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentSelect", "AgentAutocomplete");
            agent.WithRoutePrefixes("/demo/components", "/demo/components/datagrid", "/demo/components/select", "/demo/components/autocomplete", "/demo/components/date-picker", "/demo/components/date-range-picker", "/demo/components/tree-view");
        });

        agentBuilder.AddAgent("Workflow Orchestrator Agent", agent =>
        {
            agent.WithDescription("Focused on the component reference surface for form, dialog, command, and file workflow primitives.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentStepper", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentTreeView", "AgentCommandBar", "AgentFileUpload");
            agent.WithRoutePrefixes("/demo/components", "/demo/components/form", "/demo/components/dialog", "/demo/components/tabs", "/demo/components/stepper", "/demo/components/command-bar", "/demo/components/file-upload");
        });

        agentBuilder.AddWorkflow<SupplierComplianceCapabilities>("Supplier Compliance Agent", agent =>
        {
            agent.WithDescription("Focused on supplier risk review, explanation, recovery-playbook guidance, and remediation preparation.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithRoutePrefixes("/demo/workflows/supplier-compliance");
        });

        agentBuilder.AddWorkflow<SupportInboxCapabilities>("Support Inbox Agent", agent =>
        {
            agent.WithDescription("Focused on support tickets that need a reply, reply drafting, escalation, and queue guidance.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithDataSchemas("support-data");
            agent.WithRoutePrefixes("/demo/workflows/support-inbox");
        });

        agentBuilder.AddWorkflow<DemoFileWorkflowCapabilities>("File Workflow Agent", agent =>
        {
            agent.WithDescription("Focused on file audit bundles, remote handoff, and token verification workflows.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentFileUpload", "AgentCommandBar");
            agent.WithRoutePrefixes("/demo/workflows/file-audit-bundle");
        });

        agentBuilder.AddWorkflow<DojoRecipeReleaseCapabilities>("Recipe Release Agent", agent =>
        {
            agent.WithDescription("Focused on recipe readiness, release blockers, recovery-playbook guidance, and publish-ready draft preparation.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentForm", "AgentDataGrid", "AgentDialog");
            agent.WithRoutePrefixes("/demo/workflows/recipe-release");
        });

        agentBuilder.AddWorkflow<IncidentEscalationCapabilities>("Incident Escalation Agent", agent =>
        {
            agent.WithDescription("Focused on incident triage, evidence review, escalation brief preparation, and recovery from blocked review-board handoffs.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentTreeView", "AgentTabs", "AgentStepper", "AgentCommandBar", "AgentDialog");
            agent.WithRoutePrefixes("/demo/workflows/incident-escalation");
        });

        agentBuilder.AddWorkflow<ResponseOrchestrationCapabilities>("Response Orchestration Agent", agent =>
        {
            agent.WithDescription("Focused on cross-system orchestration across supplier risk, audit evidence, and incident escalation, including guided subsystem-stage advancement before operational handoff.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDialog");
            agent.WithRoutePrefixes("/demo/workflows/response-orchestration");
        });

        agentBuilder.AddWorkflow<ReleaseDossierCapabilities>("Release Dossier Agent", agent =>
        {
            agent.WithDescription("Focused on recipe release readiness and audit evidence orchestration before release dossier handoff.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDialog");
            agent.WithRoutePrefixes("/demo/workflows/release-dossier");
        });

        agentBuilder.AddWorkflow<RuntimeProbeCapabilities>("Runtime Probe Agent", agent =>
        {
            agent.WithDescription("Focused on validating runtime cancellation behavior in the live demo host.");
            agent.WithRoutePrefixes("/demo/workflows/runtime-probe");
        });
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DemoWorkflowDatabaseSeeder>();
    await seeder.InitializeAsync(CancellationToken.None);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (demoSecurityOptions.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<DemoTrafficLoggingMiddleware>();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
var agentEndpoints = app.MapAgentBlazorEndpoints();
app.MapDemoLogEndpoints();

if (demoSecurityOptions.RateLimiting.Enabled)
{
    agentEndpoints.RequireRateLimiting(DemoSecurityOptions.AgentEndpointRateLimitPolicyName);
}

app.Run();

static string? FirstConfigured(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return null;
}
