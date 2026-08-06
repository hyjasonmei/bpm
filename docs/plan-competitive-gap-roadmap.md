# Plan — 競品功能缺口路線圖（MetaGuru 對照）

2026-07-18/19 以 MetaGuru BPM 產品簡報為對照做的競品分析結論 +
功能路線圖。缺口清單**全部逐項對 code 查證過**（非憑印象），
查證日 2026-07-19。

決策紀錄（TG 定案）：

- 週邊生態（HCM / EIP / BI / RPA）**不打**——不是同一場仗
- SSO 之後會做（與 CLAUDE.md 既定目標「跟微軟生態 Entra ID / AD
  整合」一致）
- 市場策略：先打導入顧問模式服務不起的中小企業（AI cook +
  快速上線 + 月費），累積真實案件量後再上中型市場

---

## 1. 查證後的現況盤點

### 已有、且可當賣點（demo 時主動秀）

| 能力 | 實作位置 | 備註 |
|---|---|---|
| 順簽 / 條件分支 | 各 chef-cooked flow state machine | gateway 條件寫在流程 code，表達力任意 |
| 並簽 / 會簽 | `ParallelApprovalService`（threshold 模型） | threshold=全員→並簽；M of N→會簽（達標後餘 slot Skipped）。載具 CONTRACT_REVIEW / COMMITTEE_REVIEW |
| 角色共享佇列 | `IActorAuthorizer.CanActAsync` role-aware | 一人多職 OK |
| 代理人（含接受機制） | admin-svc Delegation（Pending/Accepted/Declined）+ bpm-svc accept/decline 端點 | 比 MetaGuru 細：被代理人要按接受才生效 |
| 異常案件處理 | **Process Doctor**（`Api/Doctor`，admin-ui DoctorPage） | scan 偵測 resigned_approver / ownerless / stalled + 組織斷線（no_manager / no_dept_head / empty_role / empty_group）；reassign / batch-reassign / cancel + DoctorActionLog |
| OData 組織資料 | admin-svc `Odata/OrgEdmModel` | 10 entity sets（Users/Departments/Groups/GroupMembers/Roles/Memberships/UserDepartments/Managers/DepartmentHeads/DepartmentParents）+ Datasets 動態 EDM |
| 自定義資料集 | Dataset CRUD + `/api/datasets/resolve` + `DatasetSelect` + 設計器綁定 | 含連動（filterByParentFieldId） |
| Email 出站 | `SmtpNotifyDispatcher` | 預設關；env 設 `Bpm:Notifications:Smtp:*` 指 relay 即開。搭 in-app 鈴鐺 + NotificationDispatchAudit |
| 稽核素材 | AuditableEntity stamping / ActorResolutionAudit / NotificationDispatchAudit / DoctorActionLog / case 各關卡決策記錄 | 素材齊，缺統一查詢匯出面（見中期 ⑦） |
| 行動端 | PWA + RWD（2026-07-17 上線） | 免裝 App，對打 MetaGuru 的原生 APP |

### 確認缺（本計畫的工作項）

1. 轉簽（end-user；Doctor reassign 是管理員補救，非簽核人自助）
   — **已完成**（2026-07-19，已 merge main）
2. ~~批次簽核（user 面無 batch/bulk）~~ — **2026-08-06 TG 決議不做**，
   見 §4
3. 加簽（前 / 後 / 平行皆無）
4. 核決權限集中設定（threshold 常數散在 8+ 個 flow service，
   如 `LEAVE_V1_LeaveService.DaysGateThreshold`）
5. SSO / Entra ID（無任何 OIDC/SAML/LDAP code）
6. SLA 時效 / 催辦（`IScheduledJobKicker` seam 已預留、目前 no-op）
7. 多語系（bpm-ui 無 i18n）
8. 使用者時區（只有系統 clock 抽象）

---

## 2. 近期（功能補洞，依序執行）

每項開工時各自走 brainstorm → spec → plan
（`docs/superpowers/plans/`），此處只鎖方向與邊界。

### 2.1 轉簽（transfer）— 小，先做

簽核人把當前待簽單轉給另一人（「這不歸我管，該給 X」）。
與代理人互補：代理是事前設定，轉簽是當下臨時。

- 共用 primitive（lead 範圍）：transfer action + 軌跡記錄；
  已有 `CurrentAssigneeUserId` / `CurrentAssigneeRoleCode` 模型可沿用
- Doctor 的 reassign 已是同型操作，可參考其授權與 log 形狀
- UI：CaseDetail ActionFooter 加「轉簽」（styled confirm modal，
  含選人器——Doctor candidates 端點的 picker 可重用）
- chef conventions 補一段，讓新 cook 的流程自帶轉簽

### 2.2 批次簽核 — **不做**（2026-08-06 TG 決議）

移到 §4。原規劃內容留在 git 歷史（`b5c8ed7` 之前的版本），要復活再取回。

### 2.3 核決權限 dataset — 中

把散在各 flow 的 threshold 常數改為查 authority dataset
（表單別 × 區間 × 簽核層級），admin 改門檻立即生效、免重 cook。

- dataset resolve API 已在；主要工作是（a）定 authority dataset
  的 schema 慣例（b）flow service 讀取處抽象成
  `IDecisionThresholdProvider`（預設 fallback 回 code 常數，
  沒設定照舊）（c）chef conventions 補「門檻類條件參照
  authority dataset」
- 賣點話術：對齊台灣企業內控的「核決權限表」文件，
  一處改全流程生效，同時保留 gateway 任意條件的彈性

### 2.4 平行加簽 — 中

臨時把某人拉進當前關卡一起簽。

- 直接重用 `ParallelApprovalService` 的 slot 模型：
  對進行中的 group 動態加 slot（threshold 同步調整規則要定義）
- 若當前關卡不是 parallel group，「加簽」= 就地把單人關卡
  升級成 group（原簽核人 + 新增者）
- 前 / 後加簽（插入新關卡）動到 cook 出來的固定狀態機，
  複雜度高一級 → **二期**，看客戶反饋再決定

---

## 3. 中期（商用成熟度 / 企業採購 checklist）

依序：

1. **SLA 時效 / 催辦** — 逾時提醒、自動催辦、升級。
   `IScheduledJobKicker` seam 已預留（註解明寫等 SLA-timer 實作）；
   sandbox time-advance 剛好可以驗收 SLA 行為。順手支援時區。
2. **SSO / Entra ID**（已定案）— 對齊 CLAUDE.md 微軟生態目標；
   多語系需求屆時一併評估（外商客戶通常兩者同時要）。
3. **稽核 log 統一匯出** — 素材都在（見 §1），做整合查詢 + 匯出面
   （admin Audit 頁擴充）。
4. **備份還原 + 壓測** — 量級驗證（年數萬案件），資安問卷會問。
   Postgres 遷移 conventions 已守（CLAUDE.md DB conventions）。

---

## 4. 不做 / 延後

| 項目 | 理由 |
|---|---|
| 保留（顯式擱置標記） | 「先不簽」天然支援，無價值 |
| **批次簽核** | 2026-08-06 TG 決議拿掉（原 §2.2） |
| 前 / 後加簽 | 平行加簽先上（§2.4），看反饋 |
| 多語系 | 目標市場台灣；隨 SSO 波評估 |
| HCM / EIP / BI / RPA 生態 | 不打這場；BPM 核心 + AI cook 是主軸 |
| 跨時區時效 | 多國企業才需要；SLA 實作時順手留欄位即可 |

---

## 5. 競品定位結論（給業務話術用）

- **功能面差距小**：核心簽核能力對 MetaGuru 是齊的，部分更細
  （代理人接受機制、Doctor、AI cook、PWA）。demo 現場直接
  cook 一條流程是最強武器；並簽/會簽（M of N）可當亮點。
- **商用差距在非功能面**：邊角打磨（真實客戶量）、客戶自助維運
  （chef 模式 vs 賣工具）、採購 checklist（SSO/稽核/備份）、
  壓測、信任背書。
- **市場切法**：50 人以下無 IT 部門先行（顧問導入模式的空隙），
  中型市場等 §3 補完再上——與 CLAUDE.md「中小企業起點、
  成功案例敲門大企業」一致。
