# Happy path: default route

1. **預支需求** (`s`) - system starts the case.
2. **預支申請單 Advance Payment** (`ape`) - submitter `self` fills out the form and submits.
3. **簽核流程 Approval** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **預支現金 Cash in Advance** (`e`) - system completes the case.
