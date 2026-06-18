using System.Reflection;
using FluentValidation;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Behaviors;
using Bpm.Application.Common.Services;
using Bpm.Application.Features.APE.V1;
using Bpm.Application.Features.LEAVE.V1;
using Bpm.Application.Features.EOB.V1;
using Bpm.Application.Features.FAD.V1;
using Bpm.Application.Features.FAP.V1;
using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Application.Features.ETM.V1;
using Bpm.Application.Features.TEO.V1;
using Bpm.Application.Features.TRQ.V1;
using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Application.Features.WFH.V1;
using Bpm.Application.Features.WFH.V2;
using Bpm.Application.Inbox;
using Bpm.Application.Notifications;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bpm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // PR-N1: Model-B notification dispatcher. POC writes to a local
        // text file (see FileNotifyDispatcher TODO for production swap).
        // Configured under `Bpm:Notifications` so deployments can repoint
        // without code change. Scoped because the implementation may
        // grow per-request behavior (audit context, etc).
        var notifySection = configuration?.GetSection("Bpm:Notifications");
        if (notifySection is not null && notifySection.Exists())
        {
            services.Configure<NotifyDispatcherOptions>(notifySection);
        }
        else
        {
            services.Configure<NotifyDispatcherOptions>(_ => { });
        }

        // SMTP transport options (Bpm:Notifications:Smtp). Disabled by default;
        // SmtpNotifyDispatcher no-ops until a deployment turns it on + points it
        // at a real relay (ACS / SendGrid SMTP) via env.
        var smtpSection = configuration?.GetSection("Bpm:Notifications:Smtp");
        if (smtpSection is not null && smtpSection.Exists())
            services.Configure<SmtpOptions>(smtpSection);
        else
            services.Configure<SmtpOptions>(_ => { });

        services.AddScoped<INotifyDispatcher, FileNotifyDispatcher>();

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

        // Chef-cooked feature services living in the Application layer.
        services.AddScoped<LEAVE_V1_LeaveService>();
        services.AddScoped<PURCHASE_REQUEST_V1_PurchaseRequestService>();
        services.AddScoped<TRQ_V1_TravelRequestService>();
        services.AddScoped<APE_V1_AdvancePaymentService>();
        services.AddScoped<TEO_V1_TravelExpenseService>();
        services.AddScoped<FAP_V1_PurchaseService>();
        services.AddScoped<FAD_V1_DisposalService>();
        services.AddScoped<EOB_V1_OnboardingService>();
        services.AddScoped<ETM_V1_TerminationService>();
        services.AddScoped<VENDOR_EXPENSE_V1_VendorExpenseService>();
        services.AddScoped<WFH_V1_WfhService>();
        services.AddScoped<WFH_V2_WfhService>();

        // Unified inbox: scan the Application assembly for
        // ITypedInboxProvider impls so chef-cooked feature providers
        // shipped under Application/Features/<CODE>/V<N>/ get registered
        // automatically. Persistence/DI scans the Persistence assembly
        // separately for the legacy LEAVE V1 reference shape.
        foreach (var providerType in assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                        && !t.IsInterface
                        && typeof(ITypedInboxProvider).IsAssignableFrom(t)))
        {
            services.AddScoped(typeof(ITypedInboxProvider), providerType);
        }

        return services;
    }
}
