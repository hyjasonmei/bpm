using System.Text.Json;
using Bpm.Api.Common;
using Bpm.Application;
using Bpm.Application.Common.Abstractions;
using Bpm.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    "api" => new AnthropicApiBackend(sp.GetRequiredService<IHttpClientFactory>()),
    _     => new ClaudeCliBackend(sp.GetRequiredService<ILogger<ClaudeCliBackend>>()),
});

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

app.UseExceptionHandler();
app.UseCors("bpm-ui");

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
            ? Results.Ok(new { status = "healthy", aiBackend = ai.Name })
            : Results.Json(new { status = "db-unreachable", aiBackend = ai.Name }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "db-unreachable", aiBackend = ai.Name }, statusCode: 503);
    }
});

// Wizard hand-off: receive a completed spec.json from the front-end and
// drop it under {Spec:IncomingFolder}/{tenant}/{ts}-{flowCode}.json so a
// human (Jason) or a watcher can pick it up and feed prompt_template_v1 to
// Claude Code. Phase A is intentionally a file drop — Phase B will swap
// this for a job queue / pipeline trigger.
app.MapPost("/api/spec", async (HttpContext ctx, IClock clock) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(json))
        return Results.BadRequest(new { error = "Empty request body" });

    JsonDocument doc;
    try { doc = JsonDocument.Parse(json); }
    catch (JsonException ex) { return Results.BadRequest(new { error = "Invalid JSON", detail = ex.Message }); }

    using (doc)
    {
        if (!doc.RootElement.TryGetProperty("meta", out var meta))
            return Results.BadRequest(new { error = "Missing spec.meta" });

        var tenant = meta.TryGetProperty("tenant", out var t) ? t.GetString() : null;
        var flowCode = meta.TryGetProperty("flowCode", out var f) ? f.GetString() : null;
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(flowCode))
            return Results.BadRequest(new { error = "spec.meta.tenant and spec.meta.flowCode are required" });

        var safeTenant = string.Concat(tenant.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        var safeFlow = string.Concat(flowCode.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        var now = clock.UtcNow;
        var ts = now.ToString("yyyyMMddTHHmmssZ");
        var trackingId = $"{safeTenant}-{safeFlow}-{ts}";

        var configured = app.Configuration["Spec:IncomingFolder"];
        var rootFolder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Directory.GetCurrentDirectory(), "incoming")
            : configured;
        var folder = Path.Combine(rootFolder, safeTenant);
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, $"{ts}-{safeFlow}.json");
        await File.WriteAllTextAsync(fullPath, json);

        app.Logger.LogInformation("Spec received: tracking={Tracking} path={Path} bytes={Bytes}", trackingId, fullPath, json.Length);
        return Results.Ok(new { trackingId, path = fullPath, receivedAt = now });
    }
});

// CoPilot chat — routes to whichever IAiBackend is registered (cli or api).
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
