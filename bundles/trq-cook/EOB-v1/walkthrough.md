# Happy path: default route

1. **招聘意願發生** (`s`) - system starts the case.
2. **申請單 Employee Request** (`req`) - submitter `self` fills out the form and submits.
3. **簽核流程 Approval** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **基本設定 Employee Setup** (`su`) - submitter `self` fills out the form and submits.
5. **結案** (`e`) - system completes the case.
