# Happy path: default route

1. **開始** (`start_1`) - system starts the case.
2. **填寫採購申請** (`task_apply`) - submitter `self` fills out the form and submits.
3. **部門主管審核** (`approval_dept_head`) - approver `expr:submitter.department.head` reviews and approves.
4. **主管審核結果** (`gateway_dept`) - gateway routes the case based on form data.
5. **財務審核** (`approval_finance`) - approver `principal` reviews and approves.
6. **財務審核結果** (`gateway_finance`) - gateway routes the case based on form data.
7. **結束** (`end_1`) - system completes the case.
