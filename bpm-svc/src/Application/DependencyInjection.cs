using System.Reflection;
using FluentValidation;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Behaviors;
using Bpm.Application.Common.Services;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // PR-J4 §6: default no-op sandbox actor — Api layer overrides this
        // with HttpContextSandboxActor so request-scoped JWT claims are
        // consulted. TryAdd preserves the override.
        services.TryAddScoped<ISandboxActorContext, SystemSandboxActor>();

        // PR-J3: IClock is now Scoped (SandboxClock decorator). The CelNet
        // evaluator consumes IClock for the today()/now() built-ins, so it
        // must also be Scoped — Singleton would capture a stale clock and
        // fail DI scope validation in Development.
        services.AddScoped<IExpressionEvaluator, CelNetExpressionEvaluator>();
        services.AddScoped<BpmCelV1Validator>();
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

        // Bundle import pipeline (PR-I3). Parser is Scoped because it logs
        // per-bundle (Info-level entry list); validator is pure and stateless
        // so it lives as a Singleton.
        services.AddScoped<IBundleParser, BundleParser>();
        services.AddSingleton<IBundleValidator, BundleValidator>();

        return services;
    }
}
