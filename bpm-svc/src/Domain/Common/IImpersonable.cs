namespace Bpm.Domain.Common;

// Audit-row entities implementing this interface get auto-stamped with the
// impersonator's user id by AuditSaveChangesInterceptor when the action was
// performed inside an impersonation session.
public interface IImpersonable
{
    Guid? ImpersonatedByUserId { get; set; }
}
