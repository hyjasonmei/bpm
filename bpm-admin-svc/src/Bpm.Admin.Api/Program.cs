using Bpm.Admin.Api.Auth;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Auth;
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
    var connectionString = builder.Configuration.GetConnectionString("Admin")
        ?? "Data Source=admin.db";
    options.UseSqlite(connectionString);
    options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
});

builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IEffectiveRoleResolver, EffectiveRoleResolver>();
builder.Services.AddScoped<IGroupMembershipService, GroupMembershipService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFlowLifecycleService, FlowLifecycleService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<SessionAuthMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
