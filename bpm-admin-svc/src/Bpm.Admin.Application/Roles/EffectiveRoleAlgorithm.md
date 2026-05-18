# EffectiveRoleResolver Algorithm

A user's effective role set is computed as the union of three layers:

```
direct:  PrincipalRole rows where PrincipalId = user.Id
              ∪
dept:    PrincipalRole rows for any dept the user belongs to (or any
         ancestor dept via DeptParent), where InheritToMembers = true
              ∪
group:   PrincipalRole rows for any group containing the user
         (transitively, walking GroupMember up), where InheritToMembers = true
```

- Direct assignments always count regardless of `InheritToMembers`.
- Container-level assignments (dept / group) count only when `InheritToMembers = true`.
- "Walks" are bounded by the existing cycle-detection guard on GroupMember insertion.
- Delegation is applied as a **separate overlay** in the runtime; this resolver is a pure principal-graph computation.
- v0 is query-time; if profiling shows it as a hotspot, swap to a materialized view (`effective_principal_role`) refreshed on membership change.
