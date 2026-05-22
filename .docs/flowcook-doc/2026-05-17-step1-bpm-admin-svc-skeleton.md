# Step 1 — `bpm-admin-svc` Skeleton

> Status: **DRAFT — ready to implement**
> Date: 2026-05-17
> Related: `2026-05-17-migration-plan.md` §3 Step 1、`2026-05-16-flowcook-pivot-design.md` §3 Principal & Role Model

flowcook 第一個 implementation milestone：把 admin BE 從零建好、Principal 七表 + SeedCli + User & Role API + 帳號密碼登入。完成後 admin FE（Step 2）可顯示組織資料、AI Kitchen（Step 3）可選 Principal 當 actor。

---

## 1. Scope

| 在 Step 1 內 | 不在 Step 1 內 |
|---|---|
| `bpm-admin-svc/` Clean Architecture 骨架（獨立 .NET solution） | bpm-admin-ui 改造（Step 2） |
| Principal 七表 EF Core entity + migration | AI Kitchen wizard（Step 3） |
| `effective_principal_role` query-time 計算（materialized view 之後加） | bpm-svc refactor（Step 4） |
| SeedCli `clear` + `--org`（drop admin+bpm 兩顆 DB） | syncer 整合（Step 6） |
| User & Role API（CRUD + soft delete） | chef（Step 7） |
| 帳號密碼登入（cookie session） | SSO / 外部 IdP 整合 |
| 基本 xUnit tests | chef on-hold callback API（Step 7 順便加） |

---

## 2. Project 結構

### 2.1 Solution 設定

獨立 .NET solution（與 `bpm-svc` 分開），路徑：

```
bpm-admin-svc/
├── bpm-admin-svc.sln
├── src/
│   ├── Bpm.Admin.Api/             # Web API host
│   ├── Bpm.Admin.Application/     # business logic
│   ├── Bpm.Admin.Domain/          # entities / value objects
│   ├── Bpm.Admin.Persistence/     # EF DbContext, migrations
│   └── Bpm.Admin.SeedCli/         # console app (clear / --org)
├── tests/
│   ├── Bpm.Admin.Application.Tests/
│   ├── Bpm.Admin.Persistence.Tests/
│   └── Bpm.Admin.Api.Tests/        # integration tests via TestServer
├── CLAUDE.md
└── README.md
```

不與 `bpm-svc/` 共用 sln；之後 syncer 在它們之間透過 HTTP 通訊。

### 2.2 Clean Architecture 分層

```
Bpm.Admin.Api          (controllers)
   ↓ DI
Bpm.Admin.Application  (services / handlers / DTOs)
   ↓
Bpm.Admin.Domain       (entities, no infra deps)
   ↑
Bpm.Admin.Persistence  (EF Core / DbContext, depends on Domain)
   ↑
Bpm.Admin.SeedCli      (depends on Persistence + Application)
```

依賴方向：Persistence → Domain；Application → Domain；Api → Application；SeedCli → Persistence + Application。

---

## 3. DB 設定

### 3.1 SQLite + Postgres-ready

- Step 1 用 SQLite（dev/demo 快）
- Code 遵守 CLAUDE.md 既有 conventions：
  - 全 EF Core，禁 raw `IDbConnection` / `Dapper`
  - 禁 SQLite 特有函式（`json_extract` / `unixepoch` / SQLite ROW_NUMBER）
  - 不寫 raw SQL migration
  - JSON 用 EF Owned types 或純 TEXT，避 query 內 JSON path
  - 並發控制 EF OptimisticConcurrency (RowVersion)
- 切 Postgres 只改 connection string + 跑 migrations 即可

### 3.2 兩顆 DB

- **admin DB**（`bpm-admin-svc`）：Principal / Role / Delegation / AuditEvent / FlowSpec / FlowLifecycleState / 等
- **bpm DB**（既有 `bpm-svc`）：ProcessInstance / Task / TaskHistory / NotificationDispatchAudit / ...
- 兩個 DB 由各自 EF DbContext 管理，schema migration 獨立
- SeedCli 同時管兩顆（見 §5）

---

## 4. Principal 七表 EF Core Entity

對應 `2026-05-16-flowcook-pivot-design.md` §3.8 Schema 雛形。

### 4.1 entity classes（Bpm.Admin.Domain）

```csharp
public enum PrincipalType { User, Dept, Group }

public class Principal {
    public Guid Id { get; set; }
    public PrincipalType Type { get; set; }
    public string DisplayName { get; set; }
    public string? Email { get; set; }        // user-only meaningful
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }  // ISoftDeletable
    [Timestamp] public byte[] RowVersion { get; set; }
}

public class UserDept {
    public Guid UserId { get; set; }
    public Guid DeptId { get; set; }
    public bool IsPrimary { get; set; }
    // composite PK (UserId, DeptId)
}

public class DeptParent {
    public Guid DeptId { get; set; }         // PK
    public Guid? ParentDeptId { get; set; }  // null = root
}

public class GroupMember {
    public Guid GroupId { get; set; }
    public Guid MemberPrincipalId { get; set; }
    public PrincipalType MemberType { get; set; }
    // composite PK (GroupId, MemberPrincipalId)
}

public class Role {
    public Guid Id { get; set; }
    public string Name { get; set; }
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
}

public class PrincipalRole {
    public Guid PrincipalId { get; set; }
    public Guid RoleId { get; set; }
    public bool InheritToMembers { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid AssignedByUserId { get; set; }
    // composite PK (PrincipalId, RoleId)
}

public class Delegation {
    public Guid Id { get; set; }
    public Guid DelegatorPrincipalId { get; set; }
    public Guid DelegateToUserId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool Active { get; set; } = true;
    public string? Reason { get; set; }
}
```

### 4.2 ISoftDeletable + global filter

```csharp
public interface ISoftDeletable {
    DateTime? DeletedAt { get; set; }
}

// In AdminDbContext.OnModelCreating:
foreach (var entity in modelBuilder.Model.GetEntityTypes()
    .Where(e => typeof(ISoftDeletable).IsAssignableFrom(e.ClrType))) {
    modelBuilder.Entity(entity.ClrType)
        .HasQueryFilter(SoftDeleteExpression(entity.ClrType));
}
```

Principal 是第一個套用此 pattern 的 entity；未來新表 implement `ISoftDeletable` 自動帶。

### 4.3 effective_principal_role 計算

v0 用 query-time 函數（不建 materialized view）：

```csharp
// Bpm.Admin.Application/Authorization/EffectiveRoleResolver.cs
public async Task<HashSet<Guid>> GetEffectiveRolesAsync(Guid userId) {
    var direct = await GetDirectRolesAsync(userId);            // PrincipalRole where PrincipalId=userId
    var deptRoles = await GetInheritedDeptRolesAsync(userId);  // walk UserDept → DeptParent ↑ → PrincipalRole where Inherit=true
    var groupRoles = await GetInheritedGroupRolesAsync(userId);// walk GroupMember ← group → PrincipalRole where Inherit=true
    return direct.Union(deptRoles).Union(groupRoles).ToHashSet();
}
```

性能瓶頸（如果有）→ Step 4 之後加 materialized view。

---

## 5. SeedCli

### 5.1 Subcommands

```
dotnet run --project src/Bpm.Admin.SeedCli -- <subcommand>

Subcommands:
  clear       Drop & recreate both DBs (admin + bpm)
  --org       Seed minimal org data (after clear)
  status      Show current DB state
```

⚠️ **dev-only guard**：啟動檢查 `ASPNETCORE_ENVIRONMENT=Development` 或 `FLOWCOOK_ALLOW_SEED=1`，否則 refuse。

### 5.2 `--org` 種子內容

| Entity | 數量 |
|---|---|
| Principal (user) | ~13（沿用 PersonaSeedService 名單） |
| Principal (dept) | ~6 |
| Principal (group) | 1-2（demo 跨部門小組） |
| Role | ~14（沿用既有 14 role） |
| UserDept | ~15（含少數 user 兼任兩部門） |
| DeptParent | ~5（簡單 2-3 層 tree） |
| GroupMember | ~6 |
| PrincipalRole | ~20（夾 inherit_to_members true/false 範例） |
| Delegation | 1-2（範例 user→user delegation） |

### 5.3 兩 DB 處理

SeedCli 在同一隻 console app 內處理兩個 DbContext：
- `AdminDbContext`（連 admin DB）
- `BpmDbContext`（連 bpm DB，從 bpm-svc 暴露 connection string 或重複定義 schema 簡化版）

Step 1 階段 `bpm DB` 可能還是舊 bpm-svc 的 schema（沒 Principal）。SeedCli 兩種模式：
- 模式 A：只 seed admin DB；bpm DB 留給 Step 4 refactor 後處理
- 模式 B：兩 DB 都 seed，bpm DB schema 暫時保持 legacy

**Step 1 採模式 A**：只動 admin DB；bpm DB 等 Step 4 再接入。

---

## 6. User & Role API

REST endpoint 對應 admin FE 五大頁中的 **User & Role**。

### 6.1 Principal

```
GET    /api/principals?type=user|dept|group&page=1
GET    /api/principals/{id}
POST   /api/principals               { type, displayName, email?, ... }
PUT    /api/principals/{id}          { displayName?, email?, active? }
DELETE /api/principals/{id}          (soft delete, sets DeletedAt)
```

### 6.2 UserDept / DeptParent / GroupMember

```
POST   /api/principals/{userId}/dept-memberships          { deptId, isPrimary }
DELETE /api/principals/{userId}/dept-memberships/{deptId}

PUT    /api/principals/{deptId}/parent                    { parentDeptId? }

POST   /api/principals/{groupId}/members                  { memberPrincipalId, memberType }
DELETE /api/principals/{groupId}/members/{memberId}
```

### 6.3 Role / PrincipalRole

```
GET    /api/roles
POST   /api/roles
POST   /api/principals/{principalId}/roles                { roleId, inheritToMembers }
DELETE /api/principals/{principalId}/roles/{roleId}
```

### 6.4 Delegation

```
GET    /api/delegations?delegatorPrincipalId=...
POST   /api/delegations           { delegatorPrincipalId, delegateToUserId, startAt, endAt, reason? }
DELETE /api/delegations/{id}      (soft cancel: active=false)
```

### 6.5 Effective roles query（給 spec ActorRef 解析用）

```
GET /api/principals/{userId}/effective-roles
  → [{ roleId, sourcePrincipalId, viaInherit }]
```

### 6.6 Audit

每個 mutating endpoint 寫 audit event（先寫進本地 AuditEvent table；syncer 之後接過去）。Step 1 直接用 EF Interceptor / decorator 攔截。

---

## 7. 帳號密碼登入

### 7.1 設計

- User table 沿用 Principal table（type=user）+ 多一張 `UserCredential`（password hash）
- 登入 POST `/api/auth/login` 帶 `{ username, password }`
- 成功回 cookie（HttpOnly、Secure、SameSite=Strict）
- Cookie 內帶 session id → 對應 server-side session record（admin DB 內 `UserSession` table）
- Logout POST `/api/auth/logout` 刪 session
- 受保 endpoint 用 middleware 驗 cookie

### 7.2 UserCredential schema

```csharp
public class UserCredential {
    public Guid UserId { get; set; }            // PK, FK to Principal.Id (type=user)
    public string PasswordHash { get; set; }    // ASP.NET Identity PasswordHasher<TUser> default
    public string Salt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}

public class UserSession {
    public Guid Id { get; set; }                // session token (random)
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
```

### 7.3 SeedCli 帶 default 密碼

`seed --org` 給每個 demo user 設一個預設密碼（例如 `flowcook2026`），方便 dev 測試直接登入。

### 7.4 audit

- `login_success` / `login_fail` / `logout` 都進 audit
- session expired / forced sign-out 也記

---

## 8. Test 策略

### 8.1 單元測試（xUnit）

| 範圍 | 內容 |
|---|---|
| Domain | Principal / Role / Delegation 不變量（active flag、type enum 等） |
| Application | EffectiveRoleResolver 各 case：直接 / dept inherit / group inherit / 不繼承 |
| Application | Delegation 解析：active 區間 / 對象限 user |
| Application | Login service：success / wrong password / soft-deleted user |
| Application | Audit logger：所有 mutating action 都產 event |

### 8.2 整合測試（TestServer + In-Memory SQLite）

| 範圍 | 內容 |
|---|---|
| API Principal CRUD happy path | 建立 → 查 → 改 → 軟刪 |
| API Role assignment with inherit | dept assign role inherit=true → user 拿到 effective role |
| API Delegation lookup | delegator 有 active delegation → /effective-roles 解析正確 |
| Auth | login → cookie → 受保 endpoint 200 / 未登入 401 |
| SeedCli | `clear` + `--org` 跑完 DB 有預期 row 數 |

### 8.3 目標 coverage

- Step 1 完成時 ~50 個 test。Step 4 後（bpm-svc 接過來 313 個 test 改造）合計 ~360。

---

## 9. PR 拆解建議（incremental implementation）

每個 PR 一個 commit／milestone，方便獨立 review：

| PR | 內容 | 估時 |
|---|---|---|
| #1 | Solution + project skeleton + CI（lint / test） | 0.5d |
| #2 | AdminDbContext + Principal entity + first migration | 1d |
| #3 | 其餘六表 entity + migration + ISoftDeletable | 1.5d |
| #4 | EffectiveRoleResolver + unit tests | 1d |
| #5 | Principal API CRUD + integration tests | 1.5d |
| #6 | Role / PrincipalRole / Delegation API + tests | 1.5d |
| #7 | SeedCli `clear` + `--org` + dev-only guard | 1d |
| #8 | Auth login / logout / session middleware | 1.5d |
| #9 | Audit logger + interceptor | 1d |

合計 ~10 工作天 ≈ 1.5 週副業節奏。

---

## 10. Open Implementation Questions（開工時決定）

- Password hasher 用 ASP.NET Identity 的 `PasswordHasher<TUser>` 還 BCrypt.Net？建議：Identity 預設足夠
- Session storage 用 admin DB 還 distributed cache（Redis）？v0 用 admin DB
- API 版本前綴（`/api/v1/...`）要不要從 day 1 加？建議：加
- API JSON casing：camelCase via `System.Text.Json` defaults
- Migration naming convention：`{Date}_{Description}`（如 `20260517_PrincipalTables`）

---

## 11. 下一步

1. Jason 拍板此 skeleton plan
2. 開 PR #1 起 solution 骨架
3. Step 1 完成後同步啟動 Step 2（bpm-admin-ui 五大頁 skeleton），AI Kitchen 通往 Step 3
