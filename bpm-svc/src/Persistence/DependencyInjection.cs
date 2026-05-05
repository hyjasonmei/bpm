using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Services;
using Bpm.Application.Org;
using Bpm.Application.Spec;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.Spec;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bpm.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        services.AddScoped<AuditSaveChangesInterceptor>();

        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=bpm.db";

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Org / Spec / Resolver services
        services.AddScoped<IOrgChartReader, OrgChartReader>();
        services.AddScoped<IActorResolutionAuditor, ActorResolutionAuditor>();
        services.AddScoped<IActorResolver, ActorResolver>();
        services.AddSingleton<ActorRefValidator>();

        return services;
    }
}
