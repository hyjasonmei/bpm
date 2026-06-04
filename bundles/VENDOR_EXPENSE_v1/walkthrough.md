# Happy path: default route

1. **開始** (`start_1`) - system starts the case.
2. **填寫採購申請** (`task_fill`) - submitter `self` fills out the form and submits.
3. **主管審核** (`approval_supervisor`) - approver `expr:submitter.department.head` reviews and approves.
4. **主管審核結果** (`gateway_supervisor`) - gateway routes the case based on form data.
5. **採購審定** (`approval_procurement`) - approver `principal` reviews and approves.
6. **採購審定結果** (`gateway_procurement`) - gateway routes the case based on form data.
7. **簽核** (`approval_sign`) - approver `expr:submitter.department.head` reviews and approves.
8. **結束** (`end_1`) - system completes the case.
