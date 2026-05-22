using System.Text.Json;
using Bpm.Admin.Api.Auth;
using Bpm.Admin.Api.Common;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Auth;
using Bpm.Admin.Application.Bundle;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Application.Roles;
using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Audit;
using Bpm.Admin.Persistence.Auth;
using Bpm.Admin.Persistence.Flows;
using Bpm.Admin.Persistence.Principals;
using Bpm.Admin.Persistence.Roles;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<AuditingSaveChangesInterceptor>();
builder.Services.AddDbContext<AdminDbContext>((sp, options) =>
{
    // Single shared db with bpm-svc: both services connect to the same
    // <repoRoot>/db/bpm.db via DbPathResolver. AdminDbContext owns its
    // own table set so there's no schema collision; sharing the file
    // means a chef worktree only has one db to migrate and seed.
    // The legacy "Admin" connection-string key is honoured for
    // override, but defaults to the shared "Default" / "Data Source=bpm.db".
    var connectionString = builder.Configuration.GetConnectionString("Admin")
        ?? builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=bpm.db";
    options.UseSqlite(DbPathResolver.Normalize(connectionString), sqlite =>
    {
        // Keep admin migrations in their own history table so bpm-svc
        // and admin-svc can both apply migrations against the same
        // physical file without EF rejecting "unknown" rows.
        sqlite.MigrationsHistoryTable("__AdminEFMigrationsHistory");
    });
    options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
});

builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IEffectiveRoleResolver, EffectiveRoleResolver>();
builder.Services.AddScoped<IGroupMembershipService, GroupMembershipService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFlowLifecycleService, FlowLifecycleService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Bpm.Admin.Application.Common.Abstractions.IClock, Bpm.Admin.Api.Common.AdminSystemClock>();
builder.Services.AddSingleton<Bpm.Admin.Application.Spec.Expressions.IExpressionEvaluator, Bpm.Admin.Application.Spec.Expressions.CelNetExpressionEvaluator>();
builder.Services.AddSingleton<BundleBuildValidator>();
builder.Services.AddSingleton<SpecMdRenderer>();
builder.Services.AddSingleton<WalkthroughRenderer>();
builder.Services.AddSingleton<ChangelogRenderer>();
builder.Services.AddSingleton<IBundleBuilder, BundleBuilder>();

// AI backend selection. Default "cli" (borrows the developer's Claude
// Code subscription) for local dev; "api" must be set explicitly when
// shipping to flowcook-hosted prod since Claude Code CLI auth can't be
// reused across machines or service accounts. This logic lived in
// bpm-svc until Phase D — moved here so the AI key + chat / spec-extract
// surface ships only with admin-svc, never with the customer-shippable
// bpm-svc binary.
builder.Services.AddHttpClient("anthropic", c =>
{
    c.BaseAddress = new Uri("https://api.anthropic.com/");
    c.Timeout = TimeSpan.FromSeconds(60);
});
var aiBackendName = (Environment.GetEnvironmentVariable("FLOWCOOK_AI_BACKEND")
    ?? Environment.GetEnvironmentVariable("BPM_AI_BACKEND")
    ?? "cli").ToLowerInvariant();
builder.Services.AddSingleton<IAiBackend>(sp => aiBackendName switch
{
    "api" => new AnthropicApiBackend(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<AnthropicApiBackend>>()),
    _     => new ClaudeCliBackend(sp.GetRequiredService<ILogger<ClaudeCliBackend>>()),
});

var app = builder.Build();
app.Logger.LogInformation("AI backend: {Backend} (set FLOWCOOK_AI_BACKEND=api|cli to switch)", aiBackendName);

// Auto-apply admin migrations on startup so admin's `Admin_*` tables
// exist on the shared db (post-Phase 1.1 db merge). Mirrors bpm-svc's
// own auto-migrate. No collision with bpm-svc's tables because admin
// uses an `Admin_` prefix and its own MigrationsHistoryTable.
//
// Phase 2.1c: also auto-seed the org graph (13 users / 6 depts / etc)
// when the Principals table is empty AND the env is Development OR
// `FLOWCOOK_ADMIN_SEED_ON_STARTUP=true`. Without this the very first
// boot lands an empty admin table set and every login returns 401 —
// the SeedCli must be run by hand. Matches bpm-svc's
// `BPM_SEED_ON_STARTUP` ergonomics.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.MigrateAsync();

        var allowSeed = (Environment.GetEnvironmentVariable("FLOWCOOK_ADMIN_SEED_ON_STARTUP")
            ?? (app.Environment.IsDevelopment() ? "true" : "false")).ToLowerInvariant() == "true";
        if (allowSeed && !await db.Principals.AnyAsync())
        {
            var conn = db.Database.GetDbConnection().ConnectionString;
            logger.LogInformation("Admin DB empty — seeding org graph (13 users / 6 depts / 14 roles)");
            await Bpm.Admin.Persistence.Seed.Seeder.SeedOrgAsync(conn);
            logger.LogInformation("Admin seed complete. Demo password: {DemoPassword}", Bpm.Admin.Persistence.Seed.Seeder.DemoPassword);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Admin DB migration / seed failed on startup");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<SessionAuthMiddleware>();
app.UseAuthorization();

app.MapControllers();

// ── AI minimal-API endpoints (moved from bpm-svc in Phase D) ──
// These were on bpm-svc until "AI is flowcook IP" became a hard rule:
// shipping bpm-svc to a customer must not expose /api/chat or the
// AnthropicApiBackend / ClaudeCliBackend implementations.

app.MapPost("/api/chat", async (HttpContext ctx, IAiBackend ai, CancellationToken ct) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync(ct);
    if (string.IsNullOrWhiteSpace(body))
        return Results.BadRequest(new { error = "Empty body" });

    JsonDocument incoming;
    try { incoming = JsonDocument.Parse(body); }
    catch (JsonException ex) { return Results.BadRequest(new { error = "Invalid JSON", detail = ex.Message }); }

    using (incoming)
    {
        var root = incoming.RootElement;
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return Results.BadRequest(new { error = "Missing messages[]" });

        var stepHint = root.TryGetProperty("step", out var s) ? s.GetString() : "unknown";
        var draftSummary = root.TryGetProperty("draftSummary", out var ds) ? ds.GetRawText() : "{}";
        JsonElement? tools = root.TryGetProperty("tools", out var to) && to.ValueKind == JsonValueKind.Array
            ? to.Clone()
            : null;
        var hasTools = tools.HasValue;

        var toolGuidance = hasTools
            ? @"
You have a tool available for THIS step. When the customer asks for a concrete change to the current step's draft (add field, change rule, set duration, etc.), call the tool with the merged result so the canvas updates immediately. The tool input must be the COMPLETE state for the relevant slice (not a patch) — include both existing and new items.

For purely informational questions (""what does this step do?"", ""is my draft OK?""), reply with text only and do NOT call the tool.
"
            : "";

        var systemPrompt = $@"You are the AI co-pilot inside the flowcook AI Kitchen wizard. The customer is mid-way through an 11-step flow that produces a spec.json describing their workflow.

Current step: **{stepHint}**

Current draft (summary, JSON):
```json
{draftSummary}
```

Help the customer:
- Explain what this step is for if asked
- Suggest sensible defaults for their industry / flow
- Spot-check inconsistencies in the current draft and surface them
- Do NOT invent node types beyond the spec_schema (startEvent, endEvent, userTask, approval, gateway, serviceTask, notify).
{toolGuidance}
Reply in 繁體中文 (Traditional Chinese) by default. Keep text replies tight (2-4 sentences). If the customer writes in English, reply in English.";

        var result = await ai.ChatAsync(systemPrompt, messages, tools, ct);
        return Results.Content(result.Body, result.ContentType, statusCode: result.StatusCode);
    }
});

app.MapPost("/api/spec-extract", async (HttpContext ctx, IAiBackend ai, CancellationToken ct) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync(ct);
    if (string.IsNullOrWhiteSpace(body))
        return Results.BadRequest(new { error = "Empty body" });

    JsonDocument incoming;
    try { incoming = JsonDocument.Parse(body); }
    catch (JsonException ex) { return Results.BadRequest(new { error = "Invalid JSON", detail = ex.Message }); }

    using (incoming)
    {
        var root = incoming.RootElement;
        var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (kind != "description" && kind != "image")
            return Results.BadRequest(new { error = "kind must be 'description' or 'image'" });

        string? userText = null;
        string? imageDataUrl = null;
        if (kind == "description")
        {
            userText = root.TryGetProperty("text", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(userText))
                return Results.BadRequest(new { error = "Missing text" });
        }
        else
        {
            imageDataUrl = root.TryGetProperty("dataUrl", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(imageDataUrl) || !imageDataUrl.StartsWith("data:"))
                return Results.BadRequest(new { error = "Missing or invalid dataUrl (expected data:image/...;base64,...)" });
        }

        var systemPrompt = @"You are an expert BPMN architect. The customer is starting a wizard that will deploy a workflow engine. Extract a complete flow skeleton (nodes + edges + meta) from their input. Conventions:

- Node types: startEvent, endEvent, userTask (employee fills a form), approval (someone approves), gateway (decision point), serviceTask (system action), notify (send notification)
- Every flow needs exactly one startEvent and at least one endEvent
- Gateways must have ≥2 outgoing edges (with conditions on each)
- Approval nodes are nodes where a human approves; userTask are nodes where someone fills a form
- ID convention: snake_case ASCII (start_1, task_apply, approval_manager, gateway_amount, end_1)
- meta.flowCode UPPERCASE_SNAKE for class/table naming
- meta.flowName 中文 (the customer's domain language)";

        var schema = new
        {
            type = "object",
            required = new[] { "meta", "nodes", "edges" },
            properties = new
            {
                meta = new
                {
                    type = "object",
                    required = new[] { "tenant", "flowName", "flowCode" },
                    properties = new
                    {
                        tenant = new { type = "string", description = "Customer tenant code, lowercase, e.g. acme" },
                        flowName = new { type = "string", description = "Human-readable Chinese name" },
                        flowCode = new { type = "string", description = "UPPERCASE_SNAKE identifier" }
                    }
                },
                nodes = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        required = new[] { "id", "type", "label" },
                        properties = new
                        {
                            id = new { type = "string" },
                            type = new { type = "string", @enum = new[] { "startEvent", "endEvent", "userTask", "approval", "gateway", "serviceTask", "notify" } },
                            label = new { type = "string" }
                        }
                    }
                },
                edges = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        required = new[] { "id", "source", "target" },
                        properties = new
                        {
                            id = new { type = "string" },
                            source = new { type = "string" },
                            target = new { type = "string" },
                            label = new { type = "string", description = "Optional edge label, especially for gateway branches" },
                            condition = new { type = "string", description = "Optional condition expression for gateway branches" }
                        }
                    }
                },
                confidence_notes = new
                {
                    type = "string",
                    description = "Plain-text notes about parts you were uncertain about (especially for image input)."
                }
            }
        };

        var result = await ai.ExtractFlowAsync(systemPrompt, userText, imageDataUrl, schema, ct);
        return Results.Content(result.Body, result.ContentType, statusCode: result.StatusCode);
    }
});

app.Run();

public partial class Program;
