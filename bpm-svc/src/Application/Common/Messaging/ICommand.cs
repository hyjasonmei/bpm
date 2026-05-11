using MediatR;

namespace Bpm.Application.Common.Messaging;

public interface ICommand : IRequest<Unit> { }

public interface ICommand<out TResponse> : IRequest<TResponse> { }
