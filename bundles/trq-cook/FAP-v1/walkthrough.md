# Happy path: default route

1. **資產外購** (`s`) - system starts the case.
2. **請購單 Purchase Request** (`pr`) - submitter `self` fills out the form and submits.
3. **簽核流程 Approval** (`ap`) - approver `expr:submitter.manager` reviews and approves.
4. **採購單 Issue PO** (`po`) - serviceTask performs the step.
5. **驗收 Verification** (`vf`) - submitter `self` fills out the form and submits.
6. **入資產清冊 Book List** (`e`) - system completes the case.
