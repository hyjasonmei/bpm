namespace Bpm.Persistence.SharedIdentity;

// Mirror of Bpm.Admin.Domain.Principals.PrincipalType. Same integer values
// — they're persisted as the int discriminator in Admin_Principals.Type and
// must round-trip cleanly between the two services.
public enum SharedPrincipalType
{
    User = 0,
    Dept = 1,
    Group = 2,
}
