using System.Reflection;
using FluentValidation;
using Bpm.Application.Common.Behaviors;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Bpm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddSingleton<IExpressionEvaluator, CelNetExpressionEvaluator>();
        services.AddSingleton<BpmCelV1Validator>();
        services.AddScoped<ISpecImportService, SpecImportService>();

        // Bundle export pipeline (PR-I2). Renderers + validator are pure
        // functions, builder is scoped because it composes them per request
        // (no per-instance state, but kept Scoped for symmetry with other
        // services that touch IClock.UtcNow within a request).
        services.AddSingleton<SpecMdRenderer>();
        services.AddSingleton<WalkthroughRenderer>();
        services.AddSingleton<ChangelogRenderer>();
        services.AddSingleton<BundleBuildValidator>();
        services.AddScoped<IBundleBuilder, BundleBuilder>();

        return services;
    }
}
