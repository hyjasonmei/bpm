# Happy path: 5 天特休、直屬主管核准

1. **開始** (`start_1`) - system starts the case.
2. **員工申請** (`task_apply`) - submitter `self` fills out the form and submits.
3. **主管核准** (`approval_manager`) - approver `expr:submitter.manager` reviews and approves.
4. **超過 7 天？** (`gateway_days`) - gateway routes the case based on form data.
5. **HR 備案** (`task_hr_archive`) - submitter `role:HR` fills out the form and submits.
6. **完成** (`end_1`) - system completes the case.
