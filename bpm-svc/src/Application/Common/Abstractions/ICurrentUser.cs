namespace Bpm.Application.Common.Abstractions;

public interface ICurrentUser
{
    string? Id { get; }
    bool IsAuthenticated { get; }

    // Set on requests authenticated with an impersonation JWT (carries
    // `impersonated_by` and `imp_session_id` claims). Otherwise both null.
    Guid? ImpersonatedById { get; }
    Guid? ImpersonationSessionId { get; }
}
