using System.Reflection;
using FluentValidation;
using Bpm.Application.Common.Behaviors;
using Bpm.Application.Spec;
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

        return services;
    }
}
