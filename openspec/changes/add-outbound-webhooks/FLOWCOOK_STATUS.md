# Flowcook Realign — 2026-05-17

This proposal pre-dates the **flowcook** architecture pivot (see `.docs/flowcook-doc/2026-05-16-flowcook-pivot-design.md`). Concept remains applicable but should be reframed before implementation.

**Realign target:** AI Kitchen wizard 內兩處可能涵蓋：
- NOTIFY step（純信號 webhook，例：「approved 後 ping Slack」）
- INTEGRATIONS step（結構化 webhook 帶 payload + OpenAPI 描述，例：「打 ERP 建單」）

Sandbox 內 webhook 已決：v0 不攔，真的打（§6.1）。

**Status:** active, pending reframe.
