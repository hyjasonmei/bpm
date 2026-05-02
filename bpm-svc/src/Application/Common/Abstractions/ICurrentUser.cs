namespace Bpm.Application.Common.Abstractions;

public interface ICurrentUser
{
    string? Id { get; }
    bool IsAuthenticated { get; }
}
