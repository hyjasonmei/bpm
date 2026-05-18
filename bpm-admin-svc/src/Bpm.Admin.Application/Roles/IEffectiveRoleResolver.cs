namespace Bpm.Admin.Application.Roles;

public interface IEffectiveRoleResolver
{
    Task<IReadOnlyCollection<EffectiveRole>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default);
}
