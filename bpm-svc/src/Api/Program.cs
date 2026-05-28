using System.Text;
using Bpm.Api.Auth;
using Bpm.Api.Common;
using Bpm.Application;
using Bpm.Application.Common.Abstractions;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.RemoveAll<ICurrentUser>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
// PR-J4 §6: replace the default no-op SystemSandboxActor with an
// HttpContext-backed reader so audit interceptor sees actual_actor_id /
// sandbox_actor JWT claims.
builder.Services.RemoveAll<ISandboxActorContext>();
builder.Services.AddScoped<ISandboxActorContext, HttpContextSandboxActor>();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Phase D moved /api/chat + /api/spec-extract and the AI backend
// abstraction onto admin-svc. bpm-svc is now AI-free so the
// customer-shippable binary doesn't carry Anthropic credentials,
// Claude CLI shell-out logic, or the AnthropicApiBackend HTTP client.

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
        return ok
            ? Results.Ok(new { status = "healthy", authMode })
            : Results.Json(new { status = "db-unreachable", authMode }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "db-unreachable", authMode }, statusCode: 503);
    }
});

// Wizard hand-off used to be a file drop (POST /api/spec → {tenant}/{ts}-{flow}.json
// for a human + Claude Code pipeline to pick up). PR-I8 retired that path —
// the wizard now ships the completed design as a Spec Bundle (zip) saved to
// the customer's Flow Library via POST /api/admin/flow-library/build, and
// the runtime loads it inline via the bundle reproducibility runner. The
// pipeline-as-deliverable model becomes Phase B if/when codegen is needed.

// /api/chat + /api/spec-extract moved to admin-svc in Phase D (AI is
// flowcook IP, must not ship with the customer-side bpm-svc binary).

// Apply EF migrations at startup. Seed is owned by admin-svc after the
// unify-user-store change — bpm-svc no longer maintains its own
// persona/org fixture. BPM_SEED_ON_STARTUP env var is retired.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Startup migration failed");
    }
}

app.Run();
