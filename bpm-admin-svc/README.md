# bpm-admin-svc

flowcook admin backend. .NET Web API using Clean Architecture.

## Layout

```
src/
  Bpm.Admin.Api/           # ASP.NET Core Web API host
  Bpm.Admin.Application/   # business logic, handlers, services, DTOs
  Bpm.Admin.Domain/        # entities + value objects (no infra)
  Bpm.Admin.Persistence/   # EF Core DbContext, migrations
  Bpm.Admin.SeedCli/       # dev-only console app (seed clear / --org)
tests/
  Bpm.Admin.Api.Tests/
  Bpm.Admin.Application.Tests/
  Bpm.Admin.Persistence.Tests/
```

Dependency direction:

```
Domain ← Application ← Persistence
                    ← Api
                    ← SeedCli (also reads Persistence)
```

## Build / Test

```
dotnet build
dotnet test
```

## Reference docs

- `openspec/specs/flowcook-architecture`
- `openspec/specs/flowcook-principal-model`
- `openspec/changes/flowcook-step1-admin-svc-skeleton`
