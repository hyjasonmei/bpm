using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Messaging;
using MediatR;

namespace Bpm.Application.Common.Behaviors;

public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork uow) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next(ct);

        if (IsCommand(request))
            await uow.SaveChangesAsync(ct);

        return response;
    }

    private static bool IsCommand(TRequest request)
    {
        foreach (var iface in request.GetType().GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ICommand<>))
                return true;
        }
        return false;
    }
}
