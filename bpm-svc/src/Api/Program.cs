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

builder.Services.AddCors(o => o.AddPolicy("bpm-ui", p =>
{
    var configured = builder.Configuration["Cors:BpmUiOrigin"] ?? "http://localhost:5173";
    var origins = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("bpm-ui");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.MapGet("/health", async (AppDbContext db) =>
{
    try
    {
        var ok = await db.Database.CanConnectAsync();
        return ok ? Results.Ok(new { status = "healthy" }) : Results.Json(new { status = "db-unreachable" }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "db-unreachable" }, statusCode: 503);
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

// CoPilot chat — proxies the wizard's left-pane chat to Anthropic so we can
// keep the API key on the server. System prompt + tools are hand-tuned for
// the BPM onboarding context. If ANTHROPIC_API_KEY is not configured the
// endpoint returns 503 with a structured payload the UI renders as a setup
// hint instead of a generic error.
app.MapPost("/api/chat", async (HttpContext ctx, IHttpClientFactory clientFactory) =>
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Json(new
        {
            error = "configure_api_key",
            message = "後端環境變數 ANTHROPIC_API_KEY 尚未設定。請執行：export ANTHROPIC_API_KEY=sk-ant-... && dotnet run"
        }, statusCode: 503);
    }

    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
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
- When the customer asks for a change, describe what you would change in plain language so they can apply it via the canvas. Do NOT invent fields/types beyond the spec_schema (startEvent, endEvent, userTask, approval, gateway, serviceTask, notify).

Reply in 繁體中文 (Traditional Chinese) by default. Keep responses tight (2-4 sentences). If the customer writes in English, reply in English.

Phase A: Customers cannot upload diagrams yet — direct them to use the LEAVE / PURCHASE preset on the SOURCE step.";

        var anthropicReq = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 1024,
            system = new object[]
            {
                new { type = "text", text = systemPrompt, cache_control = new { type = "ephemeral" } }
            },
            messages = JsonSerializer.Deserialize<object>(messages.GetRawText())
        };

        var http = clientFactory.CreateClient("anthropic");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(anthropicReq)
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        try
        {
            using var res = await http.SendAsync(req);
            var resBody = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return Results.Json(new { error = "anthropic_error", status = (int)res.StatusCode, body = resBody }, statusCode: (int)res.StatusCode);
            return Results.Content(resBody, "application/json");
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = "upstream_failure", message = ex.Message }, statusCode: 502);
        }
    }
});

app.Run();
