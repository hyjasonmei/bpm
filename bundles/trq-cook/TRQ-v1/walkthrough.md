# Happy path: default route

1. **出差意象** (`s`) - system starts the case.
2. **申請單 Travel Request** (`req`) - submitter `self` fills out the form and submits.
3. **簽核流程 Approval** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **結案** (`e`) - system completes the case.
