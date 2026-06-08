using System.Text;
using System.Text.Json;
using Bpm.Admin.Api.Auth;
using Bpm.Admin.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

// ── Unify-JWT auth ──────────────────────────────────────────────────────────
// admin-svc mints and validates the SAME token shape bpm-svc does (issuer
// "bpm-svc", audience "bpm-ui", shared BPM_JWT_SECRET, sub = Admin_Principals id).
// One admin login yields a bearer good against both /api (admin-svc) and
// /bpmsvc (bpm-svc) — no cookie, no per-service login, no same-origin proxy.
var jwtSecret = Environment.GetEnvironmentVariable("BPM_JWT_SECRET")
    ?? "dev-secret-do-not-use-in-prod-must-be-32-bytes-long-x";  // dev fallback (matches bpm-svc)
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("BPM_JWT_SECRET must be ≥ 32 bytes (current length: " + jwtSecret.Length + ")");
var jwtOptions = new JwtOptions { Secret = jwtSecret };
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddScoped<AdminJwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.RequireHttpsMetadata = false;
        // Default inbound mapping ON: the token's "sub" maps to
        // ClaimTypes.NameIdentifier, so the existing controllers
        // (FlowsController / FlowGroupsController / FeatureTablesController /
        // AuthController.Me) read the user id unchanged. RoleClaimType "roles"
        // matches the repeated roles claims for User.IsInRole.
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "roles",
        };
    });
builder.Services.AddAuthorization();

// admin-ui calls admin-svc cross-origin with a JWT bearer (no cookie post
// unify-jwt). Allowed origins are config-driven per environment.
builder.Services.AddCors(o => o.AddPolicy("admin-ui", p =>
{
    var configured = builder.Configuration["Cors:AdminUiOrigin"]
        ?? "http://localhost:5173,http://localhost:5174,http://localhost:5175";
    var origins = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

// PR-K1 / K2 dev ergonomics: when running in Development with no chef
// token configured, fall back to a known constant. Lets a local chef
// Claude Code session connect with a literal `Bearer dev-chef-token`
// (no env-var export required). Production deployments must set
// Bpm:Chef:Token explicitly — config-driven middleware skips the chef
// auth path entirely when the value is empty.
if (builder.Environment.IsDevelopment() && string.IsNullOrEmpty(builder.Configuration["Bpm:Chef:Token"]))
{
    builder.Configuration["Bpm:Chef:Token"] = "dev-chef-token";
}

// PR-K2: in-process MCP server. Tools are auto-discovered from the
// API assembly via [McpServerToolType]. The HTTP transport piggybacks
// on Kestrel — same port, same DI container. Chef Claude Code sessions
// connect at /mcp.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(Program).Assembly);

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
    // Provider (sqlite | postgres) is config-driven; admin migrations always
    // live in their own history table so bpm-svc + admin-svc can both migrate
    // the same physical database without EF rejecting each other's rows.
    var dbProvider = DbProviderSetup.ResolveProvider(builder.Configuration["Database:Provider"]);
    DbProviderSetup.Configure(options, dbProvider, connectionString);
    options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
});

builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IEffectiveRoleResolver, EffectiveRoleResolver>();
builder.Services.AddScoped<IGroupMembershipService, GroupMembershipService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFlowLifecycleService, FlowLifecycleService>();
builder.Services.AddScoped<IFlowChatService, FlowChatService>();
builder.Services.AddScoped<IFlowGroupService, FlowGroupService>();
builder.Services.AddScoped<IFeatureTablesService, FeatureTablesService>();
builder.Services.AddScoped<IChefOrgQueryService, ChefOrgQueryService>();
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
if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation("Chef token (dev): '{Token}' — chef .mcp.json should send `Authorization: Bearer {Token}`",
        app.Configuration["Bpm:Chef:Token"] ?? "(unset)",
        app.Configuration["Bpm:Chef:Token"] ?? "(unset)");
}

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
        // Provider-aware schema init: postgres (prod / local) applies migrations;
        // sqlite (in-memory tests, legacy local) builds straight from the model
        // via EnsureCreated. The migrations + snapshot are postgres-flavoured
        // (A1), so running MigrateAsync against sqlite trips
        // PendingModelChangesWarning — EnsureCreated sidesteps it and needs no
        // provider-specific migration history.
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        var allowSeed = (Environment.GetEnvironmentVariable("FLOWCOOK_ADMIN_SEED_ON_STARTUP")
            ?? (app.Environment.IsDevelopment() ? "true" : "false")).ToLowerInvariant() == "true";
        if (allowSeed && !await db.Principals.AnyAsync())
        {
            // Use the CONFIGURED connection string (carries the password). NOT
            // db.Database.GetDbConnection().ConnectionString — Npgsql strips the
            // password from that (Persist Security Info=false), so SeedOrgAsync's
            // fresh connection fails SCRAM auth against Azure Postgres ("No
            // password has been provided"). Local Postgres auth is laxer so this
            // only bites in the cloud.
            var conn = app.Configuration.GetConnectionString("Admin")
                ?? app.Configuration.GetConnectionString("Default")
                ?? db.Database.GetDbConnection().ConnectionString;
            logger.LogInformation("Admin DB empty — seeding org graph (13 users / 6 depts / 14 roles)");
            await Bpm.Admin.Persistence.Seed.Seeder.SeedOrgAsync(conn);
            logger.LogInformation("Admin seed complete. Demo password: {DemoPassword}", Bpm.Admin.Persistence.Seed.Seeder.DemoPassword);
        }

        // PR-G1: seed default flow groups so a fresh demo lights up
        // with launcher sections immediately. Only on empty table; admins
        // can rename / reorder / delete afterwards.
        if (allowSeed && !await db.FlowGroups.AnyAsync())
        {
            var now = DateTime.UtcNow;
            var defaults = new (string Code, string ZhTw, string En, int Sort, string Icon)[]
            {
                ("hr",       "人事", "HR",       10, "Users"),
                ("purchase", "採購", "Purchase", 20, "ShoppingCart"),
                ("it",       "IT",   "IT",       30, "Wrench"),
                ("office",   "行政", "Admin",    40, "FileText"),
            };
            foreach (var (code, zh, en, sort, icon) in defaults)
            {
                db.FlowGroups.Add(new Bpm.Admin.Domain.Flows.FlowGroup
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    DisplayNameJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["zh-TW"] = zh, ["en"] = en }),
                    SortOrder = sort,
                    Icon = icon,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} default flow groups", defaults.Length);
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

app.UseCors("admin-ui");
app.UseAuthentication();
// PR-K1: chef token auth runs *after* JWT authentication so the chef token
// never overrides an already-logged-in admin user, but can claim an otherwise-
// anonymous request as a Chef. (SessionAuthMiddleware retired by unify-jwt.)
app.UseMiddleware<ChefTokenAuthMiddleware>();
app.UseAuthorization();

app.MapControllers();

// PR-K2: MCP HTTP endpoint. Auth is enforced by ChefTokenAuthMiddleware
// upstream — calls without a valid chef Bearer drop through as
// anonymous, and the MCP tools call User.IsInRole("Chef")... actually
// no: MCP tools execute in DI scope but don't carry HttpContext.User
// implicitly. The chef-token check runs middleware-level: if the chef
// header is missing the request is anonymous, and the MCP server sees
// nothing useful in HttpContext. We still surface the endpoint at /mcp;
// follow-up will harden the gate (refuse the SSE handshake when not
// in chef role).
app.MapMcp("/mcp");

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
