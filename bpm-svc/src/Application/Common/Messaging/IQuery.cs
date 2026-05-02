using MediatR;

namespace Bpm.Application.Common.Messaging;

public interface IQuery<out TResponse> : IRequest<TResponse> { }
