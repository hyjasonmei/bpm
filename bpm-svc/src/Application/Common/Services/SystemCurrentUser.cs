using Bpm.Application.Common.Abstractions;

namespace Bpm.Application.Common.Services;

public sealed class SystemCurrentUser : ICurrentUser
{
    public string? Id => "system";
    public bool IsAuthenticated => false;
}
