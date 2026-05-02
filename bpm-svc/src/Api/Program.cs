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
    var origin = builder.Configuration["Cors:BpmUiOrigin"] ?? "http://localhost:5173";
    p.WithOrigins(origin).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
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

app.Run();
