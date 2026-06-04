# Happy path: default route

1. **出差事件發生** (`s`) - system starts the case.
2. **費用單 Travel Expense** (`exp`) - submitter `self` fills out the form and submits.
3. **簽核流程 Approval** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **財務審定 Finance Review** (`fin`) - approver `principal` reviews and approves.
5. **入帳** (`e`) - system completes the case.
