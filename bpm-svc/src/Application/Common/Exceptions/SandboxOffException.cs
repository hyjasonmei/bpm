namespace Bpm.Application.Common.Exceptions;

/// <summary>
/// PR-J3: thrown by sandbox-only services (clock advance/reset, persona
/// switch, mailbox writes) when the tenant has <c>SandboxMode = false</c>.
/// Surface as a 400 with body <c>{ error: "sandbox_off" }</c>.
/// </summary>
public sealed class SandboxOffException : Exception
{
    public SandboxOffException() : base("sandbox is off") { }
}
