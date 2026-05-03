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

app.Run();
