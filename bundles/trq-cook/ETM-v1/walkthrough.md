# Happy path: default route

1. **離職意願發生** (`s`) - system starts the case.
2. **申請單 Employee Request** (`req`) - submitter `self` fills out the form and submits.
3. **簽核流程 Approval** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **交接 / 基本設定 Handover** (`ho`) - submitter `self` fills out the form and submits.
5. **結案** (`e`) - system completes the case.
