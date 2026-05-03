using System.Reflection;
using FluentValidation;
using Bpm.Application.Common.Behaviors;
using Bpm.Application.Purchase.Services;
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

        services.AddScoped<PurchaseApprovalResolver>();
        services.AddScoped<PurchaseNotificationEmitter>();

        return services;
    }
}
