using System.Text;
using System.Text.Json;
using Bpm.Api.Auth;
using Bpm.Api.Common;
using Bpm.Application;
using Bpm.Application.Common.Abstractions;
using Bpm.Persistence;
using Bpm.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.RemoveAll<ICurrentUser>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient("anthropic", c =>
{
    c.BaseAddress = new Uri("https://api.anthropic.com/");
    c.Timeout = TimeSpan.FromSeconds(60);
});

// AI backend selection. Default to "cli" (uses Jason's Claude Code
// subscription) for local dev convenience; "api" must be set explicitly when
// shipping to production / cloud since Claude Code CLI auth can't be reused
// across machines or service accounts.
var aiBackendName = (Environment.GetEnvironmentVariable("BPM_AI_BACKEND") ?? "cli").ToLowerInvariant();
builder.Services.AddSingleton<IAiBackend>(sp => aiBackendName switch
{
    "api" => new AnthropicApiBackend(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<AnthropicApiBackend>>()),
    _     => new ClaudeCliBackend(sp.GetRequiredService<ILogger<ClaudeCliBackend>>()),
});

// JWT bearer + dev-login wiring.
// `BPM_AUTH_MODE` selects the auth scheme:
//   - "dev"      → JWT validated locally; /api/dev/login mints persona JWTs
//   - "prod"     → JWT validated locally; dev-login endpoint NOT registered
//   - "disabled" → no auth middleware; everything is anonymous (legacy demo bypass)
// Default is "dev" so the wizard's RoleSwitcher works out of the box.
var authMode = (Environment.GetEnvironmentVariable("BPM_AUTH_MODE") ?? "dev").ToLowerInvariant();
var jwtSecret = Environment.GetEnvironmentVariable("BPM_JWT_SECRET")
    ?? "dev-secret-do-not-use-in-prod-must-be-32-bytes-long-x";  // dev fallback
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("BPM_JWT_SECRET must be ≥ 32 bytes (current length: " + jwtSecret.Length + ")");

var jwtOptions = new JwtOptions { Secret = jwtSecret };
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(_ =>
{
    var map = builder.Configuration.GetSection("Personas").Get<Dictionary<string, string>>() ?? new();
    return new PersonaMappingOptions { Map = map };
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<PersonaLoginService>();
builder.Services.AddScoped<Bpm.Application.Impersonation.IImpersonationTokenMinter, Bpm.Api.Impersonation.JwtImpersonationTokenMinter>();

if (authMode != "disabled")
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.RequireHttpsMetadata = false;
            // Without this, "sub"/"roles" get mapped to long XML URIs
            // (nameidentifier / role) and our NameClaimType/RoleClaimType
            // settings below no longer match — User.IsInRole returns false.
            opts.MapInboundClaims = false;
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
                NameClaimType = "sub",
                RoleClaimType = "roles",
            };
            opts.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnTokenValidated = ctx =>
                {
                    if (ctx.Principal?.Identity is System.Security.Claims.ClaimsIdentity ident)
                    {
                        var arrayLikeRoles = ident.Claims
                            .Where(c => c.Type == "roles" && c.Value.StartsWith("[") && c.Value.EndsWith("]"))
                            .ToList();
                        foreach (var c in arrayLikeRoles)
                        {
                            ident.RemoveClaim(c);
                            try
                            {
                                var values = System.Text.Json.JsonSerializer.Deserialize<string[]>(c.Value) ?? Array.Empty<string>();
                                foreach (var v in values)
                                    ident.AddClaim(new System.Security.Claims.Claim("roles", v));
                            }
                            catch
                            {
                                // If parsing fails, keep the original string as a single role.
                                ident.AddClaim(c);
                            }
                        }
                    }
                    return Task.CompletedTask;
                },
            };
        });
    builder.Services.AddAuthorization();
}

builder.Services.AddCors(o => o.AddPolicy("bpm-ui", p =>
{
    var configured = builder.Configuration["Cors:BpmUiOrigin"] ?? "http://localhost:5173";
    var origins = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.Logger.LogInformation("AI backend: {Backend} (set BPM_AI_BACKEND=api|cli to switch)", aiBackendName);
app.Logger.LogInformation("Auth mode: {AuthMode} (set BPM_AUTH_MODE=dev|prod|disabled)", authMode);

app.UseExceptionHandler();
app.UseCors("bpm-ui");

// JWT bearer auth pipeline. Public routes (/health, /swagger, OPTIONS,
// /api/dev/login when in dev mode) bypass the [Authorize] requirement.
if (authMode != "disabled")
{
    app.UseAuthentication();
    app.UseAuthorization();

    // Custom 401 envelope to match the rest of the API's error JSON shape.
    app.Use(async (ctx, next) =>
    {
        await next();
        if (ctx.Response.StatusCode == StatusCodes.Status401Unauthorized && !ctx.Response.HasStarted)
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new { error = "missing_or_invalid_token" });
        }
    });
}

// Auth gate for legacy minimal-API endpoints (/api/chat, /api/spec-extract) —
// MVC controllers with [Authorize] are handled by the framework above; the
// minimal-API routes below need the gate inline.
async Task<bool> RequireAuth(HttpContext ctx)
{
    if (authMode == "disabled") return true;
    var path = ctx.Request.Path.Value ?? "";
    if (path == "/health" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) return true;
    if (HttpMethods.IsOptions(ctx.Request.Method)) return true;
    if (ctx.User?.Identity?.IsAuthenticated == true) return true;
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await ctx.Response.WriteAsJsonAsync(new { error = "missing_or_invalid_token" });
    return false;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.MapGet("/health", async (AppDbContext db, IAiBackend ai) =>
{
    try
    {
        var ok = await db.Database.CanConnectAsync();
        return ok
            ? Results.Ok(new { status = "healthy", aiBackend = ai.Name, authMode })
            : Results.Json(new { status = "db-unreachable", aiBackend = ai.Name, authMode }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "db-unreachable", aiBackend = ai.Name, authMode }, statusCode: 503);
    }
});

// Wizard hand-off used to be a file drop (POST /api/spec → {tenant}/{ts}-{flow}.json
// for a human + Claude Code pipeline to pick up). PR-I8 retired that path —
// the wizard now ships the completed design as a Spec Bundle (zip) saved to
// the customer's Flow Library via POST /api/admin/flow-library/build, and
// the runtime loads it inline via the bundle reproducibility runner. The
// pipeline-as-deliverable model becomes Phase B if/when codegen is needed.

// CoPilot chat — routes to whichever IAiBackend is registered (cli or api).
app.MapPost("/api/chat", async (HttpContext ctx, IAiBackend ai, CancellationToken ct) =>
{
    if (!await RequireAuth(ctx)) return Results.Empty;

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

        var systemPrompt = $@"You are the AI co-pilot inside a BPM (Business Process Management) onboarding wizard. The customer is mid-way through a 9-step flow that produces a spec.json describing their workflow.

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

// Spec extraction — text → flow skeleton (both backends), image → flow
// skeleton (api backend only; cli has no base64 image flag for -p mode).
app.MapPost("/api/spec-extract", async (HttpContext ctx, IAiBackend ai, CancellationToken ct) =>
{
    if (!await RequireAuth(ctx)) return Results.Empty;

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

// Apply EF migrations and (optionally) seed the org fixture at startup.
// Seed runs only when BPM_SEED_ON_STARTUP=true (default in dev) so prod
// boots can't accidentally write fixture data into a real DB.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.MigrateAsync();
        var seedOnStartup = (Environment.GetEnvironmentVariable("BPM_SEED_ON_STARTUP")
            ?? (app.Environment.IsDevelopment() ? "true" : "false")).ToLowerInvariant() == "true";
        if (seedOnStartup)
            await OrgFixture.RunAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Startup migration/seed failed");
    }
}

app.Run();
