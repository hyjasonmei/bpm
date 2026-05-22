namespace Bpm.Admin.Domain.Audit;

/// <summary>
/// Marker interface: entities implementing this are automatically audited
/// (Added / Modified / Deleted) by <c>AuditingSaveChangesInterceptor</c>.
/// Action-style events (login, password_set, etc.) still call IAuditLogger
/// directly; this interceptor captures row-level CRUD.
/// </summary>
public interface IAuditable
{
}
