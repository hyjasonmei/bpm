namespace Bpm.Application.Impersonation;

// Issued by Api layer; abstracted so service stays in Persistence layer.
public interface IImpersonationTokenMinter
{
    (string Token, DateTime ExpiresAt) MintFor(Guid targetUserId, Guid impersonatorUserId, Guid sessionId);
}
