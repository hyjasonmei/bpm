# Happy path: default route

1. **報廢事件** (`s`) - system starts the case.
2. **處份申請單 Disposal Request** (`dr`) - submitter `self` fills out the form and submits.
3. **固定資產判別 IT** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **領收確認 Confirmed** (`cf`) - submitter `self` fills out the form and submits.
5. **處份 Disposal** (`e`) - system completes the case.
