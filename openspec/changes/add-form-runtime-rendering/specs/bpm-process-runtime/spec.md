## ADDED Requirements

### Requirement: TaskExecution screen consumes process-runtime endpoints

The system SHALL provide a `/tasks/:id` route rendering the `TaskExecution` screen that consumes existing process-runtime endpoints:

- `GET /api/tasks/{id}` to load the task
- `GET /api/processes/{id}` to load instance state and spec snapshot
- `POST /api/tasks/{id}/submit` to submit the form patch
- `POST /api/tasks/{id}/return` for Approval kind return action

The screen renders `<DynamicForm>` against the userTask spec read from the snapshot. No new backend endpoints are required.

#### Scenario: Open task executes form

- **WHEN** Wilson navigates to `/tasks/{taskId}` for an open task
- **THEN** the screen loads task + instance + snapshot, mounts DynamicForm with the userTask spec, and presents fillable inputs

#### Scenario: Approval kind shows three buttons

- **GIVEN** the task is Approval kind
- **WHEN** the screen renders
- **THEN** Approve / Reject / Return buttons + comment textarea are shown alongside the readonly form data summary
