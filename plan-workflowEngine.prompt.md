# Plan: Tenant Workflow Engine (Event → Transform → Action)

A multi-tenant workflow engine that lets departments (tenants) subscribe to existing system events, transform event data using user-defined Scriban templates, and execute configurable actions (send email, send SMS, call API, upload file to FTP/SFTP, upload file to S3). Each workflow run is fully audited and logged. All libraries are open-source. Workflow execution is asynchronous via RabbitMQ and processed in the `Workers.Console` project.

---

## Steps

### 1. Add Encryption Infrastructure

#### 1a. Config Values (`Resgrid.Config`)

Create `WorkflowConfig.cs` in `Resgrid.Config` with:

- `EncryptionKey` — AES-256 master key (string, loaded via `ConfigProcessor` / env vars).
- `EncryptionSaltValue` — Salt for key derivation (string).
- `DefaultMaxRetryCount` — Default retry count for failed workflow actions (int, default `3`).
- `RetryBackoffBaseSeconds` — Exponential backoff base in seconds (int, default `5`).
- `WorkflowQueueName` — RabbitMQ queue name (string, `"workflowqueue"` / `"workflowqueuetest"` for DEBUG).
- `MaxConcurrentWorkflows` — Throttle for concurrent workflow executions per worker (int, default `5`).

#### 1b. Generic `IEncryptionService` (`Resgrid.Model/Services`, `Resgrid.Services`)

Create `IEncryptionService` interface in `Resgrid.Model/Services`:

```
string Encrypt(string plainText)
string Decrypt(string cipherText)
string EncryptForDepartment(string plainText, int departmentId, string departmentCode)
string DecryptForDepartment(string cipherText, int departmentId, string departmentCode)
```

- `Encrypt`/`Decrypt` use the global `WorkflowConfig.EncryptionKey` + `WorkflowConfig.EncryptionSaltValue` for system-wide encryption.
- `EncryptForDepartment`/`DecryptForDepartment` derive a department-specific key by combining the global key with the department's `Code` (4-char) and `DepartmentId` (e.g., `HMACSHA256(globalKey, departmentId + departmentCode)`) so each department's secrets are isolated.
- Implementation: `EncryptionService` in `Resgrid.Services` using `System.Security.Cryptography.Aes` with AES-256-CBC, PBKDF2 key derivation (referencing the existing `SymmetricEncryptionConfig` pattern for iterations/key size).
- Register in `ServicesModule.cs` as `InstancePerLifetimeScope`.
- This service is generic and can be used anywhere in the system that needs encryption, not just workflows.

#### 1c. Unit Tests (`Resgrid.Tests`)

Create `Tests/Resgrid.Tests/Services/EncryptionServiceTests.cs` using NUnit + FluentAssertions (following existing test patterns):

- `Encrypt_then_Decrypt_returns_original_text`
- `EncryptForDepartment_then_DecryptForDepartment_returns_original_text`
- `Different_departments_produce_different_ciphertext`
- `Decrypt_with_wrong_department_fails`
- `Encrypt_handles_empty_string`
- `Encrypt_handles_unicode_content`
- `Decrypt_with_corrupted_ciphertext_throws`

---

### 2. Add Domain Model Entities (`Resgrid.Model`)

#### Enums

**`WorkflowTriggerEventType`** — Maps to events departments can subscribe to (see Step 9 for full list):

| Value | Name | Description |
|-------|------|-------------|
| 0 | `CallAdded` | New call/dispatch created |
| 1 | `CallUpdated` | Existing call updated |
| 2 | `CallClosed` | Call closed |
| 3 | `UnitStatusChanged` | Unit status changed |
| 4 | `PersonnelStaffingChanged` | Personnel staffing level changed |
| 5 | `PersonnelStatusChanged` | Personnel action status changed |
| 6 | `UserCreated` | New user added to department |
| 7 | `UserAssignedToGroup` | User assigned to a group |
| 8 | `DocumentAdded` | Document uploaded |
| 9 | `NoteAdded` | Note created |
| 10 | `UnitAdded` | Unit created |
| 11 | `LogAdded` | Log entry created |
| 12 | `CalendarEventAdded` | Calendar event created |
| 13 | `CalendarEventUpdated` | Calendar event updated |
| 14 | `ShiftCreated` | Shift created |
| 15 | `ShiftUpdated` | Shift updated |
| 16 | `ResourceOrderAdded` | Resource order created |
| 17 | `ShiftTradeRequested` | Shift trade requested |
| 18 | `ShiftTradeFilled` | Shift trade filled |
| 19 | `MessageSent` | New message sent *(new event — see Step 10)* |
| 20 | `TrainingAdded` | Training created *(new event)* |
| 21 | `TrainingUpdated` | Training updated *(new event)* |
| 22 | `InventoryAdjusted` | Inventory quantity changed *(new event)* |
| 23 | `CertificationExpiring` | Personnel certification nearing expiry *(new event)* |
| 24 | `FormSubmitted` | Form submitted *(new event)* |
| 25 | `PersonnelRoleChanged` | User role assignment changed *(new event)* |
| 26 | `GroupAdded` | Department group created *(new event)* |
| 27 | `GroupUpdated` | Department group updated *(new event)* |

**`WorkflowActionType`**:

| Value | Name |
|-------|------|
| 0 | `SendEmail` |
| 1 | `SendSms` |
| 2 | `CallApiGet` |
| 3 | `CallApiPost` |
| 4 | `CallApiPut` |
| 5 | `CallApiDelete` |
| 6 | `UploadFileFtp` |
| 7 | `UploadFileSftp` |
| 8 | `UploadFileS3` |
| 9 | `SendTeamsMessage` |
| 10 | `SendSlackMessage` |
| 11 | `SendDiscordMessage` |
| 12 | `UploadFileAzureBlob` |
| 13 | `UploadFileBox` |
| 14 | `UploadFileDropbox` |

**`WorkflowRunStatus`**:

| Value | Name |
|-------|------|
| 0 | `Pending` |
| 1 | `Running` |
| 2 | `Completed` |
| 3 | `Failed` |
| 4 | `Cancelled` |
| 5 | `Retrying` |

**`WorkflowCredentialType`**:

| Value | Name |
|-------|------|
| 0 | `Smtp` |
| 1 | `Twilio` |
| 2 | `Ftp` |
| 3 | `Sftp` |
| 4 | `AwsS3` |
| 5 | `HttpBearer` |
| 6 | `HttpBasic` |
| 7 | `HttpApiKey` |
| 8 | `MicrosoftTeams` |
| 9 | `Slack` |
| 10 | `Discord` |
| 11 | `AzureBlobStorage` |
| 12 | `Box` |
| 13 | `Dropbox` |

#### Entities (all implement `IEntity`)

**`Workflow`** (`[Table("Workflows")]`):

- `WorkflowId` (int, PK, identity)
- `DepartmentId` (int, FK → Departments, required)
- `Name` (string, max 250, required)
- `Description` (string, max 1000, nullable)
- `TriggerEventType` (int, maps to `WorkflowTriggerEventType`)
- `IsEnabled` (bool, default true)
- `MaxRetryCount` (int, default 3)
- `RetryBackoffBaseSeconds` (int, default 5)
- `CreatedByUserId` (string, required)
- `CreatedOn` (DateTime)
- `UpdatedOn` (DateTime, nullable)
- Navigation: `ICollection<WorkflowStep> Steps`

**`WorkflowStep`** (`[Table("WorkflowSteps")]`):

- `WorkflowStepId` (int, PK, identity)
- `WorkflowId` (int, FK → Workflows, required)
- `ActionType` (int, maps to `WorkflowActionType`)
- `StepOrder` (int, for sequencing multiple steps)
- `OutputTemplate` (string, max, required — Scriban template text)
- `ActionConfig` (string, nullable — JSON for action-specific settings: e.g., email subject/to, API URL/headers, filename/bucket, etc.)
- `WorkflowCredentialId` (int?, FK → WorkflowCredentials, nullable)
- `IsEnabled` (bool, default true)
- Navigation: `Workflow Workflow`, `WorkflowCredential Credential`

**`WorkflowCredential`** (`[Table("WorkflowCredentials")]`):

- `WorkflowCredentialId` (int, PK, identity)
- `DepartmentId` (int, FK → Departments, required)
- `Name` (string, max 250, required — user-friendly label)
- `CredentialType` (int, maps to `WorkflowCredentialType`)
- `EncryptedData` (string, max — AES-encrypted JSON blob of credentials, encrypted via `IEncryptionService.EncryptForDepartment`)
- `CreatedByUserId` (string, required)
- `CreatedOn` (DateTime)
- `UpdatedOn` (DateTime, nullable)

**`WorkflowRun`** (`[Table("WorkflowRuns")]`):

- `WorkflowRunId` (long, PK, identity)
- `WorkflowId` (int, FK → Workflows, required)
- `DepartmentId` (int, FK → Departments, required)
- `Status` (int, maps to `WorkflowRunStatus`)
- `TriggerEventType` (int)
- `InputPayload` (string, max — serialized event data JSON)
- `StartedOn` (DateTime)
- `CompletedOn` (DateTime, nullable)
- `ErrorMessage` (string, max 4000, nullable)
- `AttemptNumber` (int, default 1)
- `QueuedOn` (DateTime — when enqueued to RabbitMQ)
- Navigation: `ICollection<WorkflowRunLog> Logs`

**`WorkflowRunLog`** (`[Table("WorkflowRunLogs")]`):

- `WorkflowRunLogId` (long, PK, identity)
- `WorkflowRunId` (long, FK → WorkflowRuns, required)
- `WorkflowStepId` (int, FK → WorkflowSteps, required)
- `Status` (int, maps to `WorkflowRunStatus`)
- `RenderedOutput` (string, max — the Scriban-rendered content)
- `ActionResult` (string, max 4000, nullable — HTTP status, SMTP response, etc.)
- `ErrorMessage` (string, max 4000, nullable)
- `StartedOn` (DateTime)
- `CompletedOn` (DateTime, nullable)
- `DurationMs` (long, nullable)

---

### 3. Add Repository Layer

Create in `Resgrid.Model/Repositories`:

- `IWorkflowRepository : IRepository<Workflow>` — add `GetAllActiveByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType)`
- `IWorkflowStepRepository : IRepository<WorkflowStep>` — add `GetAllByWorkflowIdAsync(int workflowId)`
- `IWorkflowCredentialRepository : IRepository<WorkflowCredential>`
- `IWorkflowRunRepository : IRepository<WorkflowRun>` — add `GetByDepartmentIdPagedAsync(int departmentId, int page, int pageSize)`, `GetPendingAndRunningByDepartmentIdAsync(int departmentId)`, `GetRunsByWorkflowIdAsync(int workflowId, int page, int pageSize)`
- `IWorkflowRunLogRepository : IRepository<WorkflowRunLog>` — add `GetByWorkflowRunIdAsync(long workflowRunId)`

Create Dapper implementations in `Resgrid.Repositories.DataRepository`:

- `WorkflowRepository`, `WorkflowStepRepository`, `WorkflowCredentialRepository`, `WorkflowRunRepository`, `WorkflowRunLogRepository` — each extending `RepositoryBase<T>` and adding custom query methods.
- Add SQL query classes under `Queries/Workflows/` for both SQL Server and PostgreSQL (following existing dual-database pattern).

Register all five repositories in `DataModule.cs`.

---

### 4. Add Database Migration

Create `M0037_AddingWorkflows.cs` in `Resgrid.Providers.Migrations/Migrations` (FluentMigrator, migration number 37):

- `Workflows` table with indexes on `DepartmentId` and `(DepartmentId, TriggerEventType, IsEnabled)`
- `WorkflowSteps` table with FK to `Workflows`, index on `WorkflowId`
- `WorkflowCredentials` table with index on `DepartmentId`
- `WorkflowRuns` table with FK to `Workflows`, indexes on `(DepartmentId, Status)`, `(WorkflowId, StartedOn)`, and `QueuedOn`
- `WorkflowRunLogs` table with FK to `WorkflowRuns` and `WorkflowSteps`, index on `WorkflowRunId`

Also create `M0037_AddingWorkflowsPg.cs` in `Resgrid.Providers.MigrationsPg` if PostgreSQL migrations are maintained separately.

---

### 5. Add Service Layer

#### `IWorkflowService` (in `Resgrid.Model/Services`)

```
// Workflow CRUD
Task<Workflow> GetWorkflowByIdAsync(int workflowId)
Task<List<Workflow>> GetWorkflowsByDepartmentIdAsync(int departmentId)
Task<Workflow> SaveWorkflowAsync(Workflow workflow, CancellationToken cancellationToken)
Task<bool> DeleteWorkflowAsync(int workflowId, CancellationToken cancellationToken)
Task<List<Workflow>> GetActiveWorkflowsByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType)

// Step CRUD
Task<WorkflowStep> SaveWorkflowStepAsync(WorkflowStep step, CancellationToken cancellationToken)
Task<bool> DeleteWorkflowStepAsync(int stepId, CancellationToken cancellationToken)
Task<List<WorkflowStep>> GetStepsByWorkflowIdAsync(int workflowId)

// Credential CRUD
Task<WorkflowCredential> GetCredentialByIdAsync(int credentialId)
Task<List<WorkflowCredential>> GetCredentialsByDepartmentIdAsync(int departmentId)
Task<WorkflowCredential> SaveCredentialAsync(WorkflowCredential credential, string departmentCode, CancellationToken cancellationToken)
Task<bool> DeleteCredentialAsync(int credentialId, CancellationToken cancellationToken)

// Execution
Task<WorkflowRun> ExecuteWorkflowAsync(int workflowId, string eventPayloadJson, int departmentId, string departmentCode, CancellationToken cancellationToken)
Task<bool> CancelWorkflowRunAsync(long workflowRunId, CancellationToken cancellationToken)

// Run Queries (Audit/Monitoring)
Task<WorkflowRun> GetWorkflowRunByIdAsync(long workflowRunId)
Task<List<WorkflowRun>> GetRunsByDepartmentIdAsync(int departmentId, int page, int pageSize)
Task<List<WorkflowRun>> GetRunsByWorkflowIdAsync(int workflowId, int page, int pageSize)
Task<List<WorkflowRun>> GetPendingAndRunningRunsByDepartmentIdAsync(int departmentId)
Task<List<WorkflowRunLog>> GetLogsForRunAsync(long workflowRunId)
Task<WorkflowHealthSummary> GetWorkflowHealthAsync(int workflowId)
Task<bool> ClearPendingRunsAsync(int departmentId, CancellationToken cancellationToken)
```

#### `WorkflowService` (in `Resgrid.Services`)

- Dependencies: `IWorkflowRepository`, `IWorkflowStepRepository`, `IWorkflowCredentialRepository`, `IWorkflowRunRepository`, `IWorkflowRunLogRepository`, `IEncryptionService`
- `ExecuteWorkflowAsync` loads the workflow + steps, renders each step's `OutputTemplate` via **Scriban** (`Scriban` NuGet, BSD-2) with the event payload deserialized as the template model, calls the appropriate `IWorkflowActionExecutor`, records `WorkflowRun` and per-step `WorkflowRunLog` entries. On failure, if `AttemptNumber < MaxRetryCount`, sets status to `Retrying` and re-enqueues to RabbitMQ with exponential backoff delay (`RetryBackoffBaseSeconds * 2^attempt` seconds).
- `GetWorkflowHealthAsync` returns a `WorkflowHealthSummary` record with counts of recent runs by status (last 24h, 7d, 30d), success rate percentage, average duration, last run timestamp, and last error.

Register in `ServicesModule.cs`.

---

### 6. Add Action Executors (`Resgrid.Providers.Workflow`)

Create a new project `Resgrid.Providers.Workflow` under `Providers/`.

#### `IWorkflowActionExecutor` interface (in `Resgrid.Model/Providers`)

```
Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
WorkflowActionType ActionType { get; }
```

Where `WorkflowActionContext` is a record containing: rendered content, decrypted credentials (as typed model), action config (deserialized from `ActionConfig` JSON), and `WorkflowActionResult` is a record with `bool Success`, `string ResultMessage`, `string ErrorDetail`.

#### Executor Implementations

| Executor | NuGet Package | License | Notes |
|----------|--------------|---------|-------|
| `SmtpEmailExecutor` | **MailKit** | MIT | Tenant supplies SMTP host, port, username, password, from address. `ActionConfig` specifies To, CC, Subject. Rendered template = email body (HTML). |
| `TwilioSmsExecutor` | **Twilio** | MIT | Tenant supplies Account SID, Auth Token, From number. `ActionConfig` specifies To number(s). Rendered template = SMS body. |
| `HttpApiExecutor` | `System.Net.Http` | Built-in | Supports GET, POST, PUT, DELETE. `ActionConfig` specifies URL, headers, content type. Rendered template = request body (for POST/PUT). Optional credential for Bearer/Basic/ApiKey auth. |
| `FtpFileExecutor` | **FluentFTP** | MIT | `ActionConfig` specifies remote path, filename template. Rendered template = file content. |
| `SftpFileExecutor` | **SSH.NET** | MIT | Same pattern as FTP. |
| `S3FileExecutor` | **AWSSDK.S3** | Apache 2.0 | Tenant supplies Access Key, Secret Key, Region, Bucket. `ActionConfig` specifies S3 key/path. Rendered template = file content. |
| `TeamsMessageExecutor` | `System.Net.Http` | Built-in | Uses Microsoft Teams Incoming Webhook URL (no OAuth app required). Tenant supplies the webhook URL as the credential. `ActionConfig` specifies optional title/theme color. Rendered template = message body (plain text or Adaptive Card JSON). Posts via `HttpClient` to the webhook endpoint with a JSON payload `{ "text": "..." }` or an Adaptive Card `{ "type": "message", "attachments": [...] }`. |
| `SlackMessageExecutor` | `System.Net.Http` | Built-in | Uses Slack Incoming Webhook URL. Tenant supplies the webhook URL as the credential. `ActionConfig` specifies optional channel override, username, icon_emoji. Rendered template = message text (supports Slack mrkdwn formatting). Posts via `HttpClient` to the webhook with JSON payload `{ "text": "...", "channel": "...", "username": "..." }`. |
| `DiscordMessageExecutor` | `System.Net.Http` | Built-in | Uses Discord Webhook URL. Tenant supplies the webhook URL as the credential. `ActionConfig` specifies optional username override, avatar_url. Rendered template = message content. Posts via `HttpClient` to the webhook with JSON payload `{ "content": "...", "username": "..." }`. Supports embed objects if rendered template is valid embed JSON. |
| `AzureBlobExecutor` | **Azure.Storage.Blobs** | MIT | Microsoft's official SDK. Tenant supplies Connection String (or Account Name + Account Key), Container Name. `ActionConfig` specifies blob name/path template, content type. Rendered template = file content. Uses `BlobServiceClient` → `BlobContainerClient` → `BlobClient.UploadAsync()`. |
| `BoxFileExecutor` | **Box.V2** | Apache 2.0 | Box's official open-source .NET SDK. Tenant supplies Developer Token or JWT credentials (Client ID, Client Secret, Enterprise ID, private key). `ActionConfig` specifies folder ID, filename template. Rendered template = file content. Uses `BoxClient` → `FilesManager.UploadAsync()`. For long-lived access, JWT server-to-server auth is recommended (no user interaction required). |
| `DropboxFileExecutor` | **Dropbox.Api** | MIT | Dropbox's official .NET SDK. Tenant supplies OAuth2 refresh token + app key/secret (obtained once via OAuth flow in the credential setup UI). `ActionConfig` specifies target path, filename template, write mode (overwrite/add). Rendered template = file content. Uses `DropboxClient` → `Files.UploadAsync()`. Token refresh is handled automatically by the SDK. |

#### `IWorkflowActionExecutorFactory` (in `Resgrid.Model/Providers`)

```
IWorkflowActionExecutor GetExecutor(WorkflowActionType actionType)
```

Implementation in `Resgrid.Providers.Workflow` registers all executors and resolves by action type.

Create `WorkflowProviderModule` (Autofac module) for DI registration. Register in test `Bootstrapper.cs` and all host projects.

---

### 7. Wire Into Event System (Async via RabbitMQ)

#### 7a. RabbitMQ Queue Configuration

Add to `ServiceBusConfig.cs`:

```csharp
#if DEBUG
public static string WorkflowQueueName = "workflowqueuetest";
#else
public static string WorkflowQueueName = "workflowqueue";
#endif
```

#### 7b. Queue Item Model

Create `WorkflowQueueItem` in `Resgrid.Model/Queue`:

- `WorkflowId` (int)
- `WorkflowRunId` (long)
- `DepartmentId` (int)
- `DepartmentCode` (string)
- `TriggerEventType` (int)
- `EventPayloadJson` (string)
- `AttemptNumber` (int)
- `EnqueuedOn` (DateTime)

#### 7c. Outbound Enqueue

Add `EnqueueWorkflowEvent(WorkflowQueueItem item)` to `IRabbitOutboundQueueProvider` and implement in `RabbitOutboundQueueProvider` following the existing `EnqueueNotification` pattern.

Add `EnqueueWorkflow(WorkflowQueueItem item)` to `IOutboundQueueProvider` and implement in `OutboundQueueProvider`.

#### 7d. WorkflowEventProvider (`Resgrid.Providers.Bus`)

Create `WorkflowEventProvider` (similar to `OutboundEventProvider`) that subscribes to all workflow-eligible events via `_eventAggregator.AddListener(...)`. For each event:

1. Extract `DepartmentId` and event type from the event object.
2. Query `IWorkflowService.GetActiveWorkflowsByDepartmentAndEventTypeAsync(departmentId, eventType)`.
3. For each matching workflow, serialize the event data to JSON, create a `WorkflowRun` record with `Status = Pending`, and enqueue a `WorkflowQueueItem` to RabbitMQ.

Register in `BusModule.cs`.

The provider listens to these event types (mapping to `_eventAggregator` message types):

- `CallAddedEvent` → `WorkflowTriggerEventType.CallAdded`
- `CallUpdatedEvent` → `WorkflowTriggerEventType.CallUpdated`
- `CallClosedEvent` → `WorkflowTriggerEventType.CallClosed`
- `UnitStatusEvent` → `WorkflowTriggerEventType.UnitStatusChanged`
- `UserStaffingEvent` → `WorkflowTriggerEventType.PersonnelStaffingChanged`
- `UserStatusEvent` → `WorkflowTriggerEventType.PersonnelStatusChanged`
- `UserCreatedEvent` → `WorkflowTriggerEventType.UserCreated`
- `UserAssignedToGroupEvent` → `WorkflowTriggerEventType.UserAssignedToGroup`
- `DocumentAddedEvent` → `WorkflowTriggerEventType.DocumentAdded`
- `NoteAddedEvent` → `WorkflowTriggerEventType.NoteAdded`
- `UnitAddedEvent` → `WorkflowTriggerEventType.UnitAdded`
- `LogAddedEvent` → `WorkflowTriggerEventType.LogAdded`
- `CalendarEventAddedEvent` → `WorkflowTriggerEventType.CalendarEventAdded`
- `CalendarEventUpdatedEvent` → `WorkflowTriggerEventType.CalendarEventUpdated`
- `ShiftCreatedEvent` → `WorkflowTriggerEventType.ShiftCreated`
- `ShiftUpdatedEvent` → `WorkflowTriggerEventType.ShiftUpdated`
- `ResourceOrderAddedEvent` → `WorkflowTriggerEventType.ResourceOrderAdded`
- `ShiftTradeRequestedEvent` → `WorkflowTriggerEventType.ShiftTradeRequested`
- `ShiftTradeFilledEvent` → `WorkflowTriggerEventType.ShiftTradeFilled`
- New events (see Step 10): `MessageSentEvent`, `TrainingAddedEvent`, `TrainingUpdatedEvent`, `InventoryAdjustedEvent`, `CertificationExpiringEvent`, `FormSubmittedEvent`, `PersonnelRoleChangedEvent`, `GroupAddedEvent`, `GroupUpdatedEvent`

#### 7e. Inbound Queue Processing (`Workers.Console`)

Add `Func<WorkflowQueueItem, Task> WorkflowQueueReceived` to `RabbitInboundQueueProvider` and wire up the queue consumer in `StartMonitoring()` following the existing pattern (e.g., `NotificationQueueReceived`).

Create `WorkflowQueueLogic` in `Resgrid.Workers.Framework/Logic`:

```
public static async Task ProcessWorkflowQueueItem(WorkflowQueueItem item, CancellationToken cancellationToken)
```

- Resolves `IWorkflowService` from the Autofac container (following `NotificationBroadcastLogic` pattern).
- Calls `_workflowService.ExecuteWorkflowAsync(item.WorkflowId, item.EventPayloadJson, item.DepartmentId, item.DepartmentCode, cancellationToken)`.
- On retry-eligible failure, re-enqueues with incremented `AttemptNumber`.

Wire into `QueuesProcessorTask.cs`:

```csharp
queue.WorkflowQueueReceived += OnWorkflowQueueReceived;
```

With handler:

```csharp
private async Task OnWorkflowQueueReceived(WorkflowQueueItem wqi)
{
    _logger.LogInformation($"{Name}: Workflow Queue Received for workflow {wqi.WorkflowId}, attempt {wqi.AttemptNumber}, starting processing...");
    await WorkflowQueueLogic.ProcessWorkflowQueueItem(wqi, _cancellationToken);
    _logger.LogInformation($"{Name}: Finished processing of workflow {wqi.WorkflowId}.");
}
```

---

### 8. Add API Controllers (`Resgrid.Web.Services/Controllers/v4`)

#### `WorkflowsController` (extends `V4AuthenticatedApiControllerbase`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v4/workflows` | List all workflows for department |
| GET | `/api/v4/workflows/{id}` | Get workflow by ID (with steps) |
| POST | `/api/v4/workflows` | Create workflow |
| PUT | `/api/v4/workflows/{id}` | Update workflow |
| DELETE | `/api/v4/workflows/{id}` | Delete workflow |
| POST | `/api/v4/workflows/{id}/steps` | Add step to workflow |
| PUT | `/api/v4/workflows/{id}/steps/{stepId}` | Update step |
| DELETE | `/api/v4/workflows/{id}/steps/{stepId}` | Delete step |
| POST | `/api/v4/workflows/{id}/test` | Manual test-trigger with sample payload |
| GET | `/api/v4/workflows/{id}/runs` | List runs for workflow (paged) |
| GET | `/api/v4/workflows/{id}/health` | Get workflow health summary |
| GET | `/api/v4/workflows/runs` | List all runs for department (paged) |
| GET | `/api/v4/workflows/runs/pending` | List pending/running runs |
| POST | `/api/v4/workflows/runs/{runId}/cancel` | Cancel a pending run |
| POST | `/api/v4/workflows/runs/clear` | Clear all pending runs for department |
| GET | `/api/v4/workflows/runs/{runId}/logs` | Get logs for a specific run |
| GET | `/api/v4/workflows/eventtypes` | List available trigger event types |

#### `WorkflowCredentialsController` (extends `V4AuthenticatedApiControllerbase`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v4/workflows/credentials` | List credentials for department (secrets masked) |
| GET | `/api/v4/workflows/credentials/{id}` | Get credential by ID (secrets masked) |
| POST | `/api/v4/workflows/credentials` | Create credential (accepts plaintext secrets, encrypts before storage) |
| PUT | `/api/v4/workflows/credentials/{id}` | Update credential |
| DELETE | `/api/v4/workflows/credentials/{id}` | Delete credential |

Add request/response DTOs in `Resgrid.Web.Services/Models/v4/Workflows/`:

- `WorkflowInput`, `WorkflowResult`, `WorkflowStepInput`, `WorkflowStepResult`
- `WorkflowCredentialInput`, `WorkflowCredentialResult` (secrets write-only, never returned in responses)
- `WorkflowRunResult`, `WorkflowRunLogResult`, `WorkflowHealthResult`
- `WorkflowTestInput` (sample event payload for manual triggering)

---

### 9. Add Web UI for Workflow Management (`Resgrid.Web`)

#### Controller

Create `WorkflowsController` in `Web/Resgrid.Web/Areas/User/Controllers/` extending `SecureBaseController`. Admin-only access (following `NotificationsController` pattern with `ClaimsAuthorizationHelper.IsUserDepartmentAdmin()`).

#### Views (`Areas/User/Views/Workflows/`)

| View | Description |
|------|-------------|
| `Index.cshtml` | Dashboard listing all workflows with status indicators (enabled/disabled), last run status (green/yellow/red), success rate badge, quick enable/disable toggle. Links to create, edit, view runs. |
| `New.cshtml` | Create workflow form: name, description, trigger event type (dropdown from `WorkflowTriggerEventType` enum with display names), enable/disable toggle, retry settings. |
| `Edit.cshtml` | Edit workflow + manage steps inline. Each step has: action type selector, credential selector (filtered by compatible `WorkflowCredentialType`), action config fields (dynamic based on action type — e.g., To/Subject for email, URL/method for API), and a **Scriban template editor** with syntax highlighting (using CodeMirror or Ace editor JS library). Show available template variables based on selected trigger event type. |
| `Runs.cshtml` | Paginated table of workflow runs with: timestamp, workflow name, status (color-coded badge), duration, attempt number, error summary. Click to expand and see per-step `WorkflowRunLog` detail. Filter by status, date range, workflow. |
| `Health.cshtml` | Per-workflow health view: success/failure counts (24h/7d/30d), success rate chart, average duration, last error, recent run timeline. |
| `Pending.cshtml` | List of all pending/in-progress workflow runs for the department. Buttons to cancel individual runs or clear all pending. Confirmation dialogs for destructive actions. |
| `Credentials.cshtml` | Manage workflow credentials: list, add, edit (secrets shown as `••••••`), delete. Grouped by credential type. |
| `CredentialNew.cshtml` | Form to add a credential: name, type (dropdown), and type-specific fields (e.g., SMTP: host, port, username, password, use SSL; Twilio: SID, token, from number; S3: access key, secret, region, bucket; etc.). |
| `CredentialEdit.cshtml` | Edit credential. Existing secrets shown as masked. Only overwritten if user provides new values. |

#### Template Editor Features

- Embed **Ace Editor** (BSD license, loaded from CDN or bundled) for Scriban template editing with syntax highlighting.
- Side panel listing available template variables for the selected trigger event type (e.g., for `CallAdded`: `{{ call.name }}`, `{{ call.nature }}`, `{{ call.address }}`, `{{ call.priority }}`, `{{ department.name }}`, etc.).
- "Preview" button that renders the template with sample data and shows the output inline.
- "Test" button that triggers a real execution with sample data and shows the run result.

#### Navigation

Add "Workflows" link to the User area sidebar/navigation (following existing pattern for Notifications, Shifts, etc.), visible only to department admins.

---

### 10. New Events to Add (Event Gap Analysis)

Analysis of the existing codebase reveals the following service operations that **do not currently fire events** but would be valuable workflow triggers for departments. Each requires creating a new event class in `Resgrid.Model/Events` and adding `_eventAggregator.SendMessage<T>()` calls in the appropriate service methods.

#### Events that exist in `EventTypes` enum but have NO handler or are only partially wired:

| Gap | Current State | Action Needed |
|-----|--------------|---------------|
| `DocumentAdded` | Event class exists, handler in `OutboundEventProvider` exists, but `DocumentsService.SaveDocumentAsync` does NOT fire it | Add `_eventAggregator.SendMessage<DocumentAddedEvent>()` in `DocumentsService.SaveDocumentAsync` |
| `CalendarEventAdded/Updated/Upcoming` | Event classes exist, handlers exist, but `CalendarService` does NOT fire them | Add event triggers in `CalendarService` save/update methods |
| `ShiftCreated/Updated/DaysAdded` | Event classes exist, handlers exist, but `ShiftsService` does NOT fire them | Add event triggers in `ShiftsService` create/update methods |

#### Entirely new events to create:

| New Event | Event Class | Service to Modify | Method(s) | Payload |
|-----------|------------|-------------------|-----------|---------|
| **MessageSent** | `MessageSentEvent` | `MessageService` | `SendMessageAsync` | `DepartmentId`, `Message` (subject, body, recipients) |
| **TrainingAdded** | `TrainingAddedEvent` | `TrainingService` | `SaveAsync` (new training) | `DepartmentId`, `Training` |
| **TrainingUpdated** | `TrainingUpdatedEvent` | `TrainingService` | `SaveAsync` (existing training) | `DepartmentId`, `Training` |
| **InventoryAdjusted** | `InventoryAdjustedEvent` | `InventoryService` | `SaveInventoryAsync` | `DepartmentId`, `Inventory`, previous quantity, new quantity |
| **CertificationExpiring** | `CertificationExpiringEvent` | New scheduled task or `CertificationService` | Scheduled check (daily) | `DepartmentId`, `PersonnelCertification`, days until expiry |
| **FormSubmitted** | `FormSubmittedEvent` | `FormsService` | `SaveFormAsync` or call endpoint | `DepartmentId`, `Form`, form data |
| **PersonnelRoleChanged** | `PersonnelRoleChangedEvent` | `PersonnelRolesService` | `AddUserToRoleAsync`, `RemoveUserFromRoleAsync` | `DepartmentId`, `UserId`, `RoleName`, added/removed |
| **GroupAdded** | `GroupAddedEvent` | `DepartmentGroupsService` | `SaveAsync` (new group) | `DepartmentId`, `DepartmentGroup` |
| **GroupUpdated** | `GroupUpdatedEvent` | `DepartmentGroupsService` | `SaveAsync` (existing group) | `DepartmentId`, `DepartmentGroup` |

#### Implementation for new events:

1. Create event class files in `Resgrid.Model/Events/` (e.g., `MessageSentEvent.cs`) — simple POCOs with `DepartmentId` and relevant entity data.
2. Add `IEventAggregator` dependency to services that don't already have it (`MessageService`, `TrainingService`, `InventoryService`, `FormsService`, `PersonnelRolesService`).
3. Add `_eventAggregator.SendMessage<T>()` calls at the appropriate points in service methods.
4. For `CertificationExpiringEvent`, create a new scheduled task `CertificationExpiryCheckTask` in `Workers.Console/Tasks/` that runs daily, queries for certifications expiring within a configurable window (e.g., 30/14/7/1 days), and fires events for each.

---

### 11. Rate Limiting and Retry Configuration

#### Config (`Resgrid.Config/WorkflowConfig.cs`)

Already covered in Step 1a. Values:

```csharp
public static int DefaultMaxRetryCount = 3;
public static int RetryBackoffBaseSeconds = 5;
public static int MaxConcurrentWorkflows = 5;
public static int RateLimitPerDepartmentPerMinute = 60;
```

#### Retry Behavior (in `WorkflowService.ExecuteWorkflowAsync`)

1. On action executor failure, catch the exception and record it in `WorkflowRunLog`.
2. If `WorkflowRun.AttemptNumber < Workflow.MaxRetryCount`, set `WorkflowRun.Status = Retrying`, increment `AttemptNumber`, calculate delay = `RetryBackoffBaseSeconds * 2^(AttemptNumber - 1)` seconds, and re-enqueue the `WorkflowQueueItem` to RabbitMQ with a message TTL/delay.
3. If max retries exceeded, set `WorkflowRun.Status = Failed` with the final error message.
4. All attempts are visible in the run logs for auditing.

#### Rate Limiting (in `WorkflowEventProvider`)

Before enqueuing a workflow execution, check the count of `WorkflowRun` records for the department in the current minute. If it exceeds `RateLimitPerDepartmentPerMinute`, skip enqueue and log a warning. This prevents a single department from flooding the queue.

---

### 12. Template Variable System (Scriban Context Builder)

All workflow templates use Scriban syntax (`{{ variable.property }}`). When a workflow is executed, a `WorkflowTemplateContext` is built by the `IWorkflowTemplateContextBuilder` and passed to the Scriban engine as the template model. The context is composed of **common variables** (always available) plus **event-specific variables** (dependent on the trigger event type).

#### 12a. Architecture

Create in `Resgrid.Model/Providers`:

```
public interface IWorkflowTemplateContextBuilder
{
    Task<ScriptObject> BuildContextAsync(int departmentId, WorkflowTriggerEventType eventType, string eventPayloadJson, CancellationToken cancellationToken);
}
```

Implementation: `WorkflowTemplateContextBuilder` in `Resgrid.Services` (or `Resgrid.Providers.Workflow`).

Dependencies: `IDepartmentsService`, `IDepartmentSettingsService`, `IUserProfileService`, `IAddressService`

**Flow:**
1. Deserialize `eventPayloadJson` into the appropriate event class based on `eventType`.
2. Load the `Department` object (with `Address`) using `departmentId`.
3. Load the triggering user's `UserProfile` from the event data (e.g., `ReportingUserId` for calls, `UserId` for staffing events).
4. Build a `Scriban.Runtime.ScriptObject` with all common + event-specific variables.
5. Return the `ScriptObject` for the template engine to render against.

The `WorkflowService.ExecuteWorkflowAsync` calls this builder before rendering each step's template.

#### 12b. Common Template Variables (Always Available)

These are loaded from the `Department`, its `Address`, the department settings, and a timestamp context. Available in **every** workflow regardless of trigger type.

| Scriban Variable | Source | Type | Description |
|-----------------|--------|------|-------------|
| `{{ department.id }}` | `Department.DepartmentId` | int | Department ID |
| `{{ department.name }}` | `Department.Name` | string | Department name |
| `{{ department.code }}` | `Department.Code` | string | 4-char department code |
| `{{ department.type }}` | `Department.DepartmentType` | string | Department type (Fire, EMS, etc.) |
| `{{ department.time_zone }}` | `Department.TimeZone` | string | Department time zone ID |
| `{{ department.use_24_hour_time }}` | `Department.Use24HourTime` | bool | 24-hour time preference |
| `{{ department.created_on }}` | `Department.CreatedOn` | datetime | Department creation date |
| `{{ department.address.street }}` | `Department.Address.Address1` | string | Street address |
| `{{ department.address.city }}` | `Department.Address.City` | string | City |
| `{{ department.address.state }}` | `Department.Address.State` | string | State/Province |
| `{{ department.address.postal_code }}` | `Department.Address.PostalCode` | string | Postal/ZIP code |
| `{{ department.address.country }}` | `Department.Address.Country` | string | Country |
| `{{ department.address.full }}` | `Address.FormatAddress()` | string | Full formatted address |
| `{{ department.phone_number }}` | `DepartmentSettingsService` | string | Department phone number (from settings) |
| `{{ timestamp.utc_now }}` | `DateTime.UtcNow` | datetime | Current UTC timestamp |
| `{{ timestamp.department_now }}` | Converted via TimeZone | datetime | Current time in department's time zone |
| `{{ timestamp.date }}` | Formatted date | string | Current date (department TZ) as `yyyy-MM-dd` |
| `{{ timestamp.time }}` | Formatted time | string | Current time (department TZ) as `HH:mm:ss` or `hh:mm tt` |
| `{{ timestamp.day_of_week }}` | Day name | string | e.g., "Monday" |

#### 12c. Common User Variables (Triggering User)

Loaded from the `UserProfile` of the user who triggered the event (e.g., the reporting user for a call, the user whose status changed, etc.). If no specific user is associated with the event, these are empty/null.

| Scriban Variable | Source | Type | Description |
|-----------------|--------|------|-------------|
| `{{ user.id }}` | `UserProfile.UserId` | string | User ID |
| `{{ user.first_name }}` | `UserProfile.FirstName` | string | First name |
| `{{ user.last_name }}` | `UserProfile.LastName` | string | Last name |
| `{{ user.full_name }}` | `UserProfile.FullName.AsFirstNameLastName` | string | Full name ("First Last") |
| `{{ user.email }}` | `IdentityUser.Email` | string | Email address |
| `{{ user.mobile_number }}` | `UserProfile.MobileNumber` | string | Mobile phone number |
| `{{ user.home_number }}` | `UserProfile.HomeNumber` | string | Home phone number |
| `{{ user.identification_number }}` | `UserProfile.IdentificationNumber` | string | ID/badge number |
| `{{ user.username }}` | `IdentityUser.UserName` | string | Login username |
| `{{ user.time_zone }}` | `UserProfile.TimeZone` | string | User's personal time zone |

#### 12d. Event-Specific Template Variables

Each trigger event type adds its own set of variables to the template context. The variable names use the event's domain object as the root key (e.g., `call`, `unit`, `log`).

---

##### `CallAdded` / `CallUpdated` / `CallClosed`

Source: `CallAddedEvent.Call`, `CallUpdatedEvent.Call`, `CallClosedEvent.Call` → `Call` model

Triggering user: `Call.ReportingUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ call.id }}` | `Call.CallId` | int |
| `{{ call.number }}` | `Call.Number` | string |
| `{{ call.name }}` | `Call.Name` | string |
| `{{ call.nature }}` | `Call.NatureOfCall` | string |
| `{{ call.notes }}` | `Call.Notes` | string |
| `{{ call.address }}` | `Call.Address` | string |
| `{{ call.geo_location }}` | `Call.GeoLocationData` | string |
| `{{ call.type }}` | `Call.Type` | string |
| `{{ call.incident_number }}` | `Call.IncidentNumber` | string |
| `{{ call.reference_number }}` | `Call.ReferenceNumber` | string |
| `{{ call.map_page }}` | `Call.MapPage` | string |
| `{{ call.priority }}` | `Call.Priority` | int |
| `{{ call.priority_text }}` | `Call.GetPriorityText()` | string |
| `{{ call.is_critical }}` | `Call.IsCritical` | bool |
| `{{ call.state }}` | `Call.State` | int |
| `{{ call.state_text }}` | `Call.GetStateText()` | string |
| `{{ call.source }}` | `Call.CallSource` | int |
| `{{ call.external_id }}` | `Call.ExternalIdentifier` | string |
| `{{ call.logged_on }}` | `Call.LoggedOn` | datetime |
| `{{ call.closed_on }}` | `Call.ClosedOn` | datetime? |
| `{{ call.completed_notes }}` | `Call.CompletedNotes` | string |
| `{{ call.contact_name }}` | `Call.ContactName` | string |
| `{{ call.contact_number }}` | `Call.ContactNumber` | string |
| `{{ call.w3w }}` | `Call.W3W` | string |
| `{{ call.dispatch_count }}` | `Call.DispatchCount` | int |
| `{{ call.dispatch_on }}` | `Call.DispatchOn` | datetime? |
| `{{ call.form_data }}` | `Call.CallFormData` | string |
| `{{ call.is_deleted }}` | `Call.IsDeleted` | bool |
| `{{ call.deleted_reason }}` | `Call.DeletedReason` | string |

---

##### `UnitStatusChanged`

Source: `UnitStatusEvent.Status` → `UnitState`, `UnitStatusEvent.PreviousStatus` → `UnitState`

Triggering user: none (unit-based event; `user.*` variables will be empty)

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ unit_status.id }}` | `UnitState.UnitStateId` | int |
| `{{ unit_status.state }}` | `UnitState.State` | int |
| `{{ unit_status.state_text }}` | `UnitState.GetStatusText()` | string |
| `{{ unit_status.timestamp }}` | `UnitState.Timestamp` | datetime |
| `{{ unit_status.note }}` | `UnitState.Note` | string |
| `{{ unit_status.latitude }}` | `UnitState.Latitude` | decimal? |
| `{{ unit_status.longitude }}` | `UnitState.Longitude` | decimal? |
| `{{ unit_status.destination_id }}` | `UnitState.DestinationId` | int? |
| `{{ unit.id }}` | `UnitState.Unit.UnitId` | int |
| `{{ unit.name }}` | `UnitState.Unit.Name` | string |
| `{{ unit.type }}` | `UnitState.Unit.Type` | string |
| `{{ unit.vin }}` | `UnitState.Unit.VIN` | string |
| `{{ unit.plate_number }}` | `UnitState.Unit.PlateNumber` | string |
| `{{ unit.station_group_id }}` | `UnitState.Unit.StationGroupId` | int? |
| `{{ previous_unit_status.state }}` | `PreviousStatus.State` | int |
| `{{ previous_unit_status.state_text }}` | `PreviousStatus.GetStatusText()` | string |
| `{{ previous_unit_status.timestamp }}` | `PreviousStatus.Timestamp` | datetime |

---

##### `PersonnelStaffingChanged`

Source: `UserStaffingEvent.Staffing` → `UserState`, `UserStaffingEvent.PreviousStaffing` → `UserState`

Triggering user: `UserState.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ staffing.id }}` | `UserState.UserStateId` | int |
| `{{ staffing.state }}` | `UserState.State` | int |
| `{{ staffing.state_text }}` | `UserState.GetStaffingText()` | string |
| `{{ staffing.timestamp }}` | `UserState.Timestamp` | datetime |
| `{{ staffing.note }}` | `UserState.Note` | string |
| `{{ previous_staffing.state }}` | `PreviousStaffing.State` | int |
| `{{ previous_staffing.state_text }}` | `PreviousStaffing.GetStaffingText()` | string |
| `{{ previous_staffing.timestamp }}` | `PreviousStaffing.Timestamp` | datetime |

---

##### `PersonnelStatusChanged`

Source: `UserStatusEvent.Status` → `ActionLog`, `UserStatusEvent.PreviousStatus` → `ActionLog`

Triggering user: `ActionLog.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ status.id }}` | `ActionLog.ActionLogId` | int |
| `{{ status.action_type }}` | `ActionLog.ActionTypeId` | int |
| `{{ status.action_text }}` | `ActionLog.GetActionText()` | string |
| `{{ status.timestamp }}` | `ActionLog.Timestamp` | datetime |
| `{{ status.geo_location }}` | `ActionLog.GeoLocationData` | string |
| `{{ status.destination_id }}` | `ActionLog.DestinationId` | int? |
| `{{ status.note }}` | `ActionLog.Note` | string |
| `{{ previous_status.action_type }}` | `PreviousStatus.ActionTypeId` | int |
| `{{ previous_status.action_text }}` | `PreviousStatus.GetActionText()` | string |
| `{{ previous_status.timestamp }}` | `PreviousStatus.Timestamp` | datetime |

---

##### `UserCreated`

Source: `UserCreatedEvent.User` → `IdentityUser`, `UserCreatedEvent.Name`

Triggering user: `IdentityUser.Id` (the newly created user)

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ new_user.id }}` | `IdentityUser.Id` | string |
| `{{ new_user.username }}` | `IdentityUser.UserName` | string |
| `{{ new_user.email }}` | `IdentityUser.Email` | string |
| `{{ new_user.name }}` | `UserCreatedEvent.Name` | string |

---

##### `UserAssignedToGroup`

Source: `UserAssignedToGroupEvent`

Triggering user: `UserAssignedToGroupEvent.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ assigned_user.id }}` | `Event.UserId` | string |
| `{{ assigned_user.name }}` | `Event.Name` | string |
| `{{ group.id }}` | `Event.Group.DepartmentGroupId` | int |
| `{{ group.name }}` | `Event.Group.Name` | string |
| `{{ group.type }}` | `Event.Group.Type` | int? |
| `{{ group.dispatch_email }}` | `Event.Group.DispatchEmail` | string |
| `{{ group.latitude }}` | `Event.Group.Latitude` | string |
| `{{ group.longitude }}` | `Event.Group.Longitude` | string |
| `{{ previous_group.id }}` | `Event.PreviousGroup.DepartmentGroupId` | int |
| `{{ previous_group.name }}` | `Event.PreviousGroup.Name` | string |

---

##### `DocumentAdded`

Source: `DocumentAddedEvent.Document` → `Document`

Triggering user: `Document.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ document.id }}` | `Document.DocumentId` | int |
| `{{ document.name }}` | `Document.Name` | string |
| `{{ document.category }}` | `Document.Category` | string |
| `{{ document.description }}` | `Document.Description` | string |
| `{{ document.type }}` | `Document.Type` | string |
| `{{ document.filename }}` | `Document.Filename` | string |
| `{{ document.admins_only }}` | `Document.AdminsOnly` | bool |
| `{{ document.added_on }}` | `Document.AddedOn` | datetime |

---

##### `NoteAdded`

Source: `NoteAddedEvent.Note` → `Note`

Triggering user: `Note.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ note.id }}` | `Note.NoteId` | int |
| `{{ note.title }}` | `Note.Title` | string |
| `{{ note.body }}` | `Note.Body` | string |
| `{{ note.color }}` | `Note.Color` | string |
| `{{ note.category }}` | `Note.Category` | string |
| `{{ note.is_admin_only }}` | `Note.IsAdminOnly` | bool |
| `{{ note.added_on }}` | `Note.AddedOn` | datetime |
| `{{ note.expires_on }}` | `Note.ExpiresOn` | datetime? |

---

##### `UnitAdded`

Source: `UnitAddedEvent.Unit` → `Unit`

Triggering user: none

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ unit.id }}` | `Unit.UnitId` | int |
| `{{ unit.name }}` | `Unit.Name` | string |
| `{{ unit.type }}` | `Unit.Type` | string |
| `{{ unit.vin }}` | `Unit.VIN` | string |
| `{{ unit.plate_number }}` | `Unit.PlateNumber` | string |
| `{{ unit.station_group_id }}` | `Unit.StationGroupId` | int? |
| `{{ unit.four_wheel }}` | `Unit.FourWheel` | bool? |
| `{{ unit.special_permit }}` | `Unit.SpecialPermit` | bool? |

---

##### `LogAdded`

Source: `LogAddedEvent.Log` → `Log`

Triggering user: `Log.LoggedByUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ log.id }}` | `Log.LogId` | int |
| `{{ log.narrative }}` | `Log.Narrative` | string |
| `{{ log.type }}` | `Log.Type` | string |
| `{{ log.log_type }}` | `Log.LogType` | int? |
| `{{ log.external_id }}` | `Log.ExternalId` | string |
| `{{ log.initial_report }}` | `Log.InitialReport` | string |
| `{{ log.course }}` | `Log.Course` | string |
| `{{ log.course_code }}` | `Log.CourseCode` | string |
| `{{ log.instructors }}` | `Log.Instructors` | string |
| `{{ log.cause }}` | `Log.Cause` | string |
| `{{ log.contact_name }}` | `Log.ContactName` | string |
| `{{ log.contact_number }}` | `Log.ContactNumber` | string |
| `{{ log.location }}` | `Log.Location` | string |
| `{{ log.started_on }}` | `Log.StartedOn` | datetime? |
| `{{ log.ended_on }}` | `Log.EndedOn` | datetime? |
| `{{ log.logged_on }}` | `Log.LoggedOn` | datetime |
| `{{ log.other_agencies }}` | `Log.OtherAgencies` | string |
| `{{ log.other_units }}` | `Log.OtherUnits` | string |
| `{{ log.other_personnel }}` | `Log.OtherPersonnel` | string |
| `{{ log.call_id }}` | `Log.CallId` | int? |

---

##### `CalendarEventAdded` / `CalendarEventUpdated`

Source: `CalendarEventAddedEvent.Item` / `CalendarEventUpdatedEvent.Item` → `CalendarItem`

Triggering user: `CalendarItem.CreatorUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ calendar.id }}` | `CalendarItem.CalendarItemId` | int |
| `{{ calendar.title }}` | `CalendarItem.Title` | string |
| `{{ calendar.description }}` | `CalendarItem.Description` | string |
| `{{ calendar.location }}` | `CalendarItem.Location` | string |
| `{{ calendar.start }}` | `CalendarItem.Start` | datetime |
| `{{ calendar.end }}` | `CalendarItem.End` | datetime |
| `{{ calendar.is_all_day }}` | `CalendarItem.IsAllDay` | bool |
| `{{ calendar.item_type }}` | `CalendarItem.ItemType` | int |
| `{{ calendar.signup_type }}` | `CalendarItem.SignupType` | int |
| `{{ calendar.is_public }}` | `CalendarItem.Public` | bool |

---

##### `ShiftCreated` / `ShiftUpdated`

Source: `ShiftCreatedEvent.Item` / `ShiftUpdatedEvent.Item` → `Shift`

Triggering user: none

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ shift.id }}` | `Shift.ShiftId` | int |
| `{{ shift.name }}` | `Shift.Name` | string |
| `{{ shift.code }}` | `Shift.Code` | string |
| `{{ shift.schedule_type }}` | `Shift.ScheduleType` | int |
| `{{ shift.assignment_type }}` | `Shift.AssignmentType` | int |
| `{{ shift.color }}` | `Shift.Color` | string |
| `{{ shift.start_day }}` | `Shift.StartDay` | datetime |
| `{{ shift.start_time }}` | `Shift.StartTime` | string |
| `{{ shift.end_time }}` | `Shift.EndTime` | string |
| `{{ shift.hours }}` | `Shift.Hours` | int? |
| `{{ shift.department_number }}` | `ShiftCreatedEvent.DepartmentNumber` | string |

---

##### `ResourceOrderAdded`

Source: `ResourceOrderAddedEvent.Order` → `ResourceOrder`

Triggering user: none (department-level)

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ order.id }}` | `ResourceOrder.ResourceOrderId` | int |
| `{{ order.title }}` | `ResourceOrder.Title` | string |
| `{{ order.incident_number }}` | `ResourceOrder.IncidentNumber` | string |
| `{{ order.incident_name }}` | `ResourceOrder.IncidentName` | string |
| `{{ order.incident_address }}` | `ResourceOrder.IncidentAddress` | string |
| `{{ order.summary }}` | `ResourceOrder.Summary` | string |
| `{{ order.open_date }}` | `ResourceOrder.OpenDate` | datetime |
| `{{ order.needed_by }}` | `ResourceOrder.NeededBy` | datetime |
| `{{ order.contact_name }}` | `ResourceOrder.ContactName` | string |
| `{{ order.contact_number }}` | `ResourceOrder.ContactNumber` | string |
| `{{ order.special_instructions }}` | `ResourceOrder.SpecialInstructions` | string |
| `{{ order.meetup_location }}` | `ResourceOrder.MeetupLocation` | string |
| `{{ order.financial_code }}` | `ResourceOrder.FinancialCode` | string |

---

##### `ShiftTradeRequested`

Source: `ShiftTradeRequestedEvent`

Triggering user: none (resolved from `ShiftSignupTradeId` in processing)

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ shift_trade.id }}` | `Event.ShiftSignupTradeId` | int |
| `{{ shift_trade.department_number }}` | `Event.DepartmentNumber` | string |

---

##### `ShiftTradeFilled`

Source: `ShiftTradeFilledEvent`

Triggering user: `ShiftTradeFilledEvent.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ shift_trade.id }}` | `Event.ShiftSignupTradeId` | int |
| `{{ shift_trade.filled_by_user_id }}` | `Event.UserId` | string |
| `{{ shift_trade.department_number }}` | `Event.DepartmentNumber` | string |

---

##### `MessageSent` *(new event)*

Source: `MessageSentEvent.Message` → `Message`

Triggering user: `Message.SendingUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ message.id }}` | `Message.MessageId` | int |
| `{{ message.subject }}` | `Message.Subject` | string |
| `{{ message.body }}` | `Message.Body` | string |
| `{{ message.is_broadcast }}` | `Message.IsBroadcast` | bool |
| `{{ message.sent_on }}` | `Message.SentOn` | datetime |
| `{{ message.type }}` | `Message.Type` | int |
| `{{ message.recipients }}` | `Message.Recipients` | string |
| `{{ message.expire_on }}` | `Message.ExpireOn` | datetime? |

---

##### `TrainingAdded` / `TrainingUpdated` *(new events)*

Source: `TrainingAddedEvent.Training` / `TrainingUpdatedEvent.Training` → `Training`

Triggering user: `Training.CreatedByUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ training.id }}` | `Training.TrainingId` | int |
| `{{ training.name }}` | `Training.Name` | string |
| `{{ training.description }}` | `Training.Description` | string |
| `{{ training.training_text }}` | `Training.TrainingText` | string |
| `{{ training.minimum_score }}` | `Training.MinimumScore` | double |
| `{{ training.created_on }}` | `Training.CreatedOn` | datetime |
| `{{ training.to_be_completed_by }}` | `Training.ToBeCompletedBy` | datetime? |

---

##### `InventoryAdjusted` *(new event)*

Source: `InventoryAdjustedEvent.Inventory` → `Inventory`, plus `PreviousAmount`, `NewAmount`

Triggering user: `Inventory.AddedByUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ inventory.id }}` | `Inventory.InventoryId` | int |
| `{{ inventory.type_name }}` | `Inventory.Type.Type` | string |
| `{{ inventory.type_description }}` | `Inventory.Type.Description` | string |
| `{{ inventory.unit_of_measure }}` | `Inventory.Type.UnitOfMesasure` | string |
| `{{ inventory.batch }}` | `Inventory.Batch` | string |
| `{{ inventory.note }}` | `Inventory.Note` | string |
| `{{ inventory.location }}` | `Inventory.Location` | string |
| `{{ inventory.amount }}` | `Inventory.Amount` | double |
| `{{ inventory.previous_amount }}` | `InventoryAdjustedEvent.PreviousAmount` | double |
| `{{ inventory.timestamp }}` | `Inventory.TimeStamp` | datetime |
| `{{ inventory.group_id }}` | `Inventory.GroupId` | int |

---

##### `CertificationExpiring` *(new event)*

Source: `CertificationExpiringEvent.Certification` → `PersonnelCertification`, plus `DaysUntilExpiry`

Triggering user: `PersonnelCertification.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ certification.id }}` | `PersonnelCertification.PersonnelCertificationId` | int |
| `{{ certification.name }}` | `PersonnelCertification.Name` | string |
| `{{ certification.number }}` | `PersonnelCertification.Number` | string |
| `{{ certification.type }}` | `PersonnelCertification.Type` | string |
| `{{ certification.area }}` | `PersonnelCertification.Area` | string |
| `{{ certification.issued_by }}` | `PersonnelCertification.IssuedBy` | string |
| `{{ certification.expires_on }}` | `PersonnelCertification.ExpiresOn` | datetime? |
| `{{ certification.received_on }}` | `PersonnelCertification.RecievedOn` | datetime? |
| `{{ certification.days_until_expiry }}` | `CertificationExpiringEvent.DaysUntilExpiry` | int |

---

##### `FormSubmitted` *(new event)*

Source: `FormSubmittedEvent.Form` → `Form`, plus `SubmittedData` (JSON)

Triggering user: `FormSubmittedEvent.SubmittedByUserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ form.id }}` | `Form.FormId` | string |
| `{{ form.name }}` | `Form.Name` | string |
| `{{ form.type }}` | `Form.Type` | int |
| `{{ form.submitted_data }}` | `FormSubmittedEvent.SubmittedData` | string (JSON) |
| `{{ form.submitted_by_user_id }}` | `FormSubmittedEvent.SubmittedByUserId` | string |
| `{{ form.submitted_on }}` | `FormSubmittedEvent.SubmittedOn` | datetime |

---

##### `PersonnelRoleChanged` *(new event)*

Source: `PersonnelRoleChangedEvent`

Triggering user: `PersonnelRoleChangedEvent.UserId`

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ role_change.user_id }}` | `Event.UserId` | string |
| `{{ role_change.role_id }}` | `Event.PersonnelRoleId` | int |
| `{{ role_change.role_name }}` | `Event.RoleName` | string |
| `{{ role_change.role_description }}` | `Event.RoleDescription` | string |
| `{{ role_change.action }}` | `Event.Action` ("Added" / "Removed") | string |

---

##### `GroupAdded` / `GroupUpdated` *(new events)*

Source: `GroupAddedEvent.Group` / `GroupUpdatedEvent.Group` → `DepartmentGroup`

Triggering user: none

| Scriban Variable | Source Property | Type |
|-----------------|----------------|------|
| `{{ group.id }}` | `DepartmentGroup.DepartmentGroupId` | int |
| `{{ group.name }}` | `DepartmentGroup.Name` | string |
| `{{ group.type }}` | `DepartmentGroup.Type` | int? |
| `{{ group.dispatch_email }}` | `DepartmentGroup.DispatchEmail` | string |
| `{{ group.message_email }}` | `DepartmentGroup.MessageEmail` | string |
| `{{ group.latitude }}` | `DepartmentGroup.Latitude` | string |
| `{{ group.longitude }}` | `DepartmentGroup.Longitude` | string |
| `{{ group.what3words }}` | `DepartmentGroup.What3Words` | string |
| `{{ group.address.street }}` | `DepartmentGroup.Address.Address1` | string |
| `{{ group.address.city }}` | `DepartmentGroup.Address.City` | string |
| `{{ group.address.state }}` | `DepartmentGroup.Address.State` | string |
| `{{ group.address.postal_code }}` | `DepartmentGroup.Address.PostalCode` | string |
| `{{ group.address.country }}` | `DepartmentGroup.Address.Country` | string |

---

#### 12e. Implementation Details

**`WorkflowTemplateContextBuilder`** class (in `Resgrid.Services`):

```
public sealed class WorkflowTemplateContextBuilder(
    IDepartmentsService departmentsService,
    IDepartmentSettingsService departmentSettingsService,
    IUserProfileService userProfileService) : IWorkflowTemplateContextBuilder
```

Key methods:

1. `BuildContextAsync(...)` — orchestrates the full context build:
   - Calls `AddCommonDepartmentVariables(scriptObject, department)`
   - Calls `AddCommonTimestampVariables(scriptObject, department.TimeZone)`
   - Calls `AddEventSpecificVariables(scriptObject, eventType, eventPayloadJson)` — switches on `WorkflowTriggerEventType` and deserializes into the correct event class, then maps properties to Scriban variable names
   - Calls `AddCommonUserVariables(scriptObject, triggeringUserId)` — loads user profile and sets `user.*` variables

2. `AddCommonDepartmentVariables(ScriptObject obj, Department dept)` — pure mapping function, no side effects
3. `AddCommonTimestampVariables(ScriptObject obj, string timeZoneId)` — pure mapping function
4. `AddCommonUserVariables(ScriptObject obj, string userId)` — loads profile, maps to `user.*`
5. `AddEventSpecificVariables(ScriptObject obj, WorkflowTriggerEventType type, string json)` — dispatcher that calls type-specific mappers

Each event type has a dedicated private static method like:
- `MapCallVariables(ScriptObject obj, Call call)`
- `MapUnitStatusVariables(ScriptObject obj, UnitState status, UnitState previousStatus)`
- `MapStaffingVariables(ScriptObject obj, UserState staffing, UserState previousStaffing)`
- etc.

These mapper methods are **pure functions** — they take the domain object and the `ScriptObject` and simply set key-value pairs. No database calls, no side effects. This makes them easy to unit test.

**Scriban rendering** in `WorkflowService.ExecuteWorkflowAsync`:

```csharp
var context = await _contextBuilder.BuildContextAsync(departmentId, eventType, eventPayloadJson, cancellationToken);
var template = Template.Parse(step.OutputTemplate);
var result = template.Render(context);
```

#### 12f. Template Variable Documentation (Runtime)

Add a static class `WorkflowTemplateVariableCatalog` in `Resgrid.Model` that provides:

```
static Dictionary<WorkflowTriggerEventType, List<TemplateVariableDescriptor>> GetVariableCatalog()
```

Where `TemplateVariableDescriptor` is:
```
public sealed record TemplateVariableDescriptor(string Name, string Description, string DataType, bool IsCommon);
```

This catalog is used by:
- The API endpoint `GET /api/v4/workflows/eventtypes` to return available variables per event type
- The Web UI Edit view to populate the side panel of available template variables
- The template "Preview" feature to generate sample data for rendering

Each `TemplateVariableDescriptor` entry corresponds to exactly one row in the tables above. The catalog is built statically at compile time (no database access needed).

#### 12g. Sample Data Generator

Add `WorkflowSampleDataGenerator` in `Resgrid.Services` that generates realistic sample `ScriptObject` instances for each event type, used for:
- Template preview in the UI (renders against fake data to show what the output will look like)
- Template validation (ensures the template compiles and renders without errors)
- Test triggering from the UI and API

Each event type has a corresponding sample generator method that returns a pre-populated `ScriptObject` with realistic placeholder values (e.g., `department.name = "Sample Fire Department"`, `call.name = "Structure Fire"`, `user.full_name = "John Smith"`, etc.).

---

### 13. Unit Tests (`Resgrid.Tests`)

All tests use NUnit + FluentAssertions + Moq, following the existing nested-namespace/context-base pattern (e.g., `namespace WorkflowServiceTests { public class with_the_workflow_service : TestBase { ... } }`). Tests mock repository and service interfaces; no database access required.

#### Test Helpers (new files in `Tests/Resgrid.Tests/Helpers/`)

**`WorkflowHelpers.cs`** — Factory methods for building test domain objects:

- `CreateTestWorkflow(int departmentId = 1, WorkflowTriggerEventType triggerType = WorkflowTriggerEventType.CallAdded)` → returns a fully populated `Workflow` with one step
- `CreateTestWorkflowStep(int workflowId = 1, WorkflowActionType actionType = WorkflowActionType.SendEmail)` → returns a `WorkflowStep` with sample Scriban template
- `CreateTestWorkflowCredential(int departmentId = 1, WorkflowCredentialType type = WorkflowCredentialType.Smtp)` → returns a `WorkflowCredential`
- `CreateTestWorkflowRun(int workflowId = 1)` → returns a `WorkflowRun` with `Status = Pending`
- `CreateTestCall()` → returns a fully populated `Call` with dispatches, notes, priority
- `CreateTestUserProfile(string userId = TestData.Users.TestUser1Id)` → returns a `UserProfile` with all fields set
- `CreateTestDepartmentWithAddress()` → returns a `Department` with linked `Address` (extends `DepartmentHelpers`)
- `CreateTestUnitState()`, `CreateTestUserState()`, `CreateTestActionLog()`, `CreateTestLog()`, `CreateTestNote()`, `CreateTestDocument()`, `CreateTestCalendarItem()`, `CreateTestShift()`, `CreateTestResourceOrder()`, `CreateTestMessage()`, `CreateTestTraining()`, `CreateTestInventory()`, `CreateTestCertification()`, `CreateTestForm()`, `CreateTestDepartmentGroup()`, `CreateTestPersonnelRole()`

**`WorkflowTestData.cs`** — Static JSON payloads for each event type, serialized from the helper objects above. Used by both `WorkflowServiceTests` and `WorkflowTemplateContextBuilderTests`.

---

#### 13a. `EncryptionServiceTests.cs`

File: `Tests/Resgrid.Tests/Services/EncryptionServiceTests.cs`

```
namespace EncryptionServiceTests
{
    public class with_the_encryption_service : TestBase
    {
        // Mocks: none (EncryptionService is pure crypto, uses Config values)
        // Setup: set WorkflowConfig.EncryptionKey and EncryptionSaltValue to test values
    }

    [TestFixture]
    public class when_encrypting_system_wide : with_the_encryption_service
    {
        // Tests:
    }

    [TestFixture]
    public class when_encrypting_for_department : with_the_encryption_service
    {
        // Tests:
    }
}
```

| Test | Description |
|------|-------------|
| `Encrypt_then_Decrypt_returns_original_text` | Round-trip test with ASCII text |
| `Encrypt_then_Decrypt_returns_original_unicode_text` | Round-trip test with Unicode (emoji, CJK, diacritics) |
| `Encrypt_produces_different_ciphertext_each_call` | Verifies IV randomness (two encryptions of same plaintext differ) |
| `Decrypt_with_wrong_key_throws` | Tamper with config key between encrypt/decrypt |
| `Decrypt_with_corrupted_ciphertext_throws` | Pass garbage string to Decrypt |
| `Encrypt_handles_empty_string` | Empty string round-trips successfully |
| `Encrypt_handles_long_string` | 100KB+ string round-trips successfully |
| `EncryptForDepartment_then_DecryptForDepartment_returns_original_text` | Department-scoped round-trip |
| `Different_departments_produce_different_ciphertext` | Same plaintext, dept 1/code "ABCD" vs dept 2/code "EFGH" → different ciphertext |
| `DecryptForDepartment_with_wrong_department_id_fails` | Encrypt with dept 1, decrypt with dept 2 → throws |
| `DecryptForDepartment_with_wrong_department_code_fails` | Encrypt with code "ABCD", decrypt with code "WXYZ" → throws |
| `EncryptForDepartment_with_null_code_uses_fallback` | Verifies behavior when department code is null (falls back to id-only derivation) |

---

#### 13b. `WorkflowServiceTests.cs`

File: `Tests/Resgrid.Tests/Services/WorkflowServiceTests.cs`

```
namespace WorkflowServiceTests
{
    public class with_the_workflow_service : TestBase
    {
        // Mocks:
        //   Mock<IWorkflowRepository>
        //   Mock<IWorkflowStepRepository>
        //   Mock<IWorkflowCredentialRepository>
        //   Mock<IWorkflowRunRepository>
        //   Mock<IWorkflowRunLogRepository>
        //   Mock<IEncryptionService>
        //   Mock<IWorkflowActionExecutorFactory>
        //   Mock<IWorkflowTemplateContextBuilder>
        //
        // Instantiate WorkflowService with mocks
    }
}
```

##### `when_managing_workflows` (CRUD)

| Test | Description |
|------|-------------|
| `GetWorkflowByIdAsync_returns_workflow_when_exists` | Repository mock returns a workflow; service returns it |
| `GetWorkflowByIdAsync_returns_null_when_not_found` | Repository returns null; service returns null |
| `GetWorkflowsByDepartmentIdAsync_returns_list` | Repository returns 3 workflows; service returns list of 3 |
| `GetWorkflowsByDepartmentIdAsync_returns_empty_for_no_workflows` | Repository returns empty; service returns empty list |
| `SaveWorkflowAsync_calls_repository_insert_for_new` | Workflow with Id 0 calls InsertAsync |
| `SaveWorkflowAsync_calls_repository_update_for_existing` | Workflow with existing Id calls UpdateAsync |
| `SaveWorkflowAsync_sets_created_on_for_new` | CreatedOn is set to UtcNow for new workflow |
| `SaveWorkflowAsync_sets_updated_on_for_existing` | UpdatedOn is set to UtcNow for update |
| `DeleteWorkflowAsync_calls_repository_delete` | Verifies DeleteAsync is called |
| `DeleteWorkflowAsync_returns_false_when_not_found` | Repository returns null for GetById; delete returns false |
| `GetActiveWorkflowsByDepartmentAndEventTypeAsync_filters_correctly` | Repository returns only enabled workflows matching type |

##### `when_managing_steps` (CRUD)

| Test | Description |
|------|-------------|
| `SaveWorkflowStepAsync_saves_and_returns_step` | Step saved via repository mock |
| `DeleteWorkflowStepAsync_deletes_successfully` | Step deleted via repository mock |
| `GetStepsByWorkflowIdAsync_returns_ordered_steps` | Returns steps ordered by StepOrder |

##### `when_managing_credentials`

| Test | Description |
|------|-------------|
| `SaveCredentialAsync_encrypts_data_before_save` | Verifies `IEncryptionService.EncryptForDepartment` is called with raw data |
| `SaveCredentialAsync_stores_encrypted_blob` | The saved entity's EncryptedData matches encryption output |
| `GetCredentialsByDepartmentIdAsync_returns_credentials` | Returns list from repository |
| `DeleteCredentialAsync_deletes_successfully` | Credential deleted |

##### `when_executing_workflows`

| Test | Description |
|------|-------------|
| `ExecuteWorkflowAsync_creates_run_record_with_running_status` | WorkflowRun is inserted with Status = Running |
| `ExecuteWorkflowAsync_renders_template_and_calls_executor` | Verifies template context builder called, then executor called with rendered output |
| `ExecuteWorkflowAsync_records_run_log_per_step` | For a workflow with 3 steps, 3 WorkflowRunLog records are inserted |
| `ExecuteWorkflowAsync_sets_completed_status_on_success` | Final WorkflowRun status = Completed |
| `ExecuteWorkflowAsync_sets_failed_status_on_executor_failure` | Executor throws → WorkflowRun status = Failed, ErrorMessage set |
| `ExecuteWorkflowAsync_sets_retrying_status_when_retries_remain` | AttemptNumber < MaxRetryCount → status = Retrying |
| `ExecuteWorkflowAsync_sets_failed_when_max_retries_exceeded` | AttemptNumber >= MaxRetryCount → status = Failed |
| `ExecuteWorkflowAsync_skips_disabled_steps` | Step with IsEnabled = false is not executed |
| `ExecuteWorkflowAsync_executes_steps_in_order` | Steps executed in StepOrder sequence, verified via invocation order |
| `ExecuteWorkflowAsync_records_duration_ms_in_log` | WorkflowRunLog.DurationMs > 0 |
| `ExecuteWorkflowAsync_decrypts_credentials_before_passing_to_executor` | `IEncryptionService.DecryptForDepartment` called, decrypted value passed in context |
| `ExecuteWorkflowAsync_handles_template_parse_error_gracefully` | Invalid Scriban template → step fails, logged, other steps still execute |
| `ExecuteWorkflowAsync_with_no_steps_completes_immediately` | Workflow with empty Steps list → Completed status, no logs |

##### `when_managing_runs` (Audit/Monitoring)

| Test | Description |
|------|-------------|
| `GetRunsByDepartmentIdAsync_returns_paged_results` | Paging works correctly |
| `GetPendingAndRunningRunsByDepartmentIdAsync_returns_active_runs` | Only Pending/Running status returned |
| `CancelWorkflowRunAsync_sets_status_to_cancelled` | Pending run → status becomes Cancelled |
| `CancelWorkflowRunAsync_returns_false_for_completed_run` | Already completed → returns false, no change |
| `ClearPendingRunsAsync_cancels_all_pending_for_department` | All Pending runs for dept set to Cancelled |
| `GetWorkflowHealthAsync_returns_correct_summary` | Mocked runs → correct counts, success rate, average duration |
| `GetLogsForRunAsync_returns_ordered_logs` | Logs returned for given run ID |

---

#### 13c. `WorkflowTemplateContextBuilderTests.cs`

File: `Tests/Resgrid.Tests/Services/WorkflowTemplateContextBuilderTests.cs`

```
namespace WorkflowTemplateContextBuilderTests
{
    public class with_the_context_builder : TestBase
    {
        // Mocks:
        //   Mock<IDepartmentsService> → returns CreateTestDepartmentWithAddress()
        //   Mock<IDepartmentSettingsService> → returns phone number
        //   Mock<IUserProfileService> → returns CreateTestUserProfile()
        //
        // Instantiate WorkflowTemplateContextBuilder with mocks
    }
}
```

##### `when_building_common_variables`

| Test | Description |
|------|-------------|
| `BuildContextAsync_includes_department_name` | `department.name` == "Test Department" |
| `BuildContextAsync_includes_department_code` | `department.code` == "XXXX" |
| `BuildContextAsync_includes_department_address_street` | `department.address.street` == expected Address1 |
| `BuildContextAsync_includes_department_address_city` | `department.address.city` == expected City |
| `BuildContextAsync_includes_department_address_full` | `department.address.full` == formatted full address |
| `BuildContextAsync_includes_department_time_zone` | `department.time_zone` == "Eastern Standard Time" |
| `BuildContextAsync_includes_department_phone_number` | `department.phone_number` == phone from settings mock |
| `BuildContextAsync_includes_timestamp_utc_now` | `timestamp.utc_now` is within 5 seconds of DateTime.UtcNow |
| `BuildContextAsync_includes_timestamp_department_now` | `timestamp.department_now` is in the correct timezone |
| `BuildContextAsync_includes_timestamp_date` | `timestamp.date` matches expected format `yyyy-MM-dd` |
| `BuildContextAsync_includes_timestamp_day_of_week` | `timestamp.day_of_week` is a valid day name |
| `BuildContextAsync_handles_null_department_address` | When department has no address, `department.address.*` are empty/null, no exception |
| `BuildContextAsync_handles_null_department_settings` | When phone number setting is null, `department.phone_number` is empty |

##### `when_building_user_variables`

| Test | Description |
|------|-------------|
| `BuildContextAsync_includes_user_first_name` | `user.first_name` == profile's FirstName |
| `BuildContextAsync_includes_user_last_name` | `user.last_name` == profile's LastName |
| `BuildContextAsync_includes_user_full_name` | `user.full_name` == "First Last" |
| `BuildContextAsync_includes_user_email` | `user.email` == identity user's Email |
| `BuildContextAsync_includes_user_mobile_number` | `user.mobile_number` == profile's MobileNumber |
| `BuildContextAsync_includes_user_identification_number` | `user.identification_number` == profile's IdentificationNumber |
| `BuildContextAsync_handles_null_user_profile` | When no profile found, all `user.*` are empty/null, no exception |
| `BuildContextAsync_handles_event_with_no_triggering_user` | UnitAdded has no user → `user.*` are all empty |

##### `when_building_call_event_variables`

| Test | Description |
|------|-------------|
| `CallAdded_includes_call_id` | `call.id` == Call.CallId |
| `CallAdded_includes_call_name` | `call.name` == Call.Name |
| `CallAdded_includes_call_nature` | `call.nature` == Call.NatureOfCall |
| `CallAdded_includes_call_address` | `call.address` == Call.Address |
| `CallAdded_includes_call_priority_text` | `call.priority_text` == "Low"/"Medium"/"High"/"Emergency" |
| `CallAdded_includes_call_state_text` | `call.state_text` == "Active"/"Closed" etc. |
| `CallAdded_includes_call_logged_on` | `call.logged_on` == Call.LoggedOn |
| `CallAdded_includes_call_geo_location` | `call.geo_location` == Call.GeoLocationData |
| `CallAdded_includes_call_contact_info` | `call.contact_name` and `call.contact_number` set |
| `CallAdded_includes_call_incident_number` | `call.incident_number` == Call.IncidentNumber |
| `CallAdded_includes_call_w3w` | `call.w3w` == Call.W3W |
| `CallAdded_includes_call_form_data` | `call.form_data` == Call.CallFormData |
| `CallAdded_sets_user_from_reporting_user_id` | `user.first_name` is populated from ReportingUserId |
| `CallClosed_includes_call_closed_on` | `call.closed_on` == Call.ClosedOn |
| `CallClosed_includes_call_completed_notes` | `call.completed_notes` == Call.CompletedNotes |

##### `when_building_unit_status_variables`

| Test | Description |
|------|-------------|
| `UnitStatusChanged_includes_unit_status_state` | `unit_status.state` == UnitState.State |
| `UnitStatusChanged_includes_unit_status_state_text` | `unit_status.state_text` == "Available"/"Committed"/etc. |
| `UnitStatusChanged_includes_unit_name` | `unit.name` == Unit.Name |
| `UnitStatusChanged_includes_unit_type` | `unit.type` == Unit.Type |
| `UnitStatusChanged_includes_previous_status` | `previous_unit_status.state` and `previous_unit_status.state_text` set |
| `UnitStatusChanged_handles_null_previous_status` | `previous_unit_status.*` are null/empty, no exception |
| `UnitStatusChanged_includes_coordinates` | `unit_status.latitude` and `unit_status.longitude` set |

##### `when_building_personnel_staffing_variables`

| Test | Description |
|------|-------------|
| `PersonnelStaffingChanged_includes_staffing_state` | `staffing.state` == UserState.State |
| `PersonnelStaffingChanged_includes_staffing_state_text` | `staffing.state_text` == "Available"/"Delayed"/etc. |
| `PersonnelStaffingChanged_includes_previous_staffing` | `previous_staffing.state` and `previous_staffing.state_text` set |
| `PersonnelStaffingChanged_sets_user_from_user_id` | `user.*` populated from UserState.UserId |

##### `when_building_personnel_status_variables`

| Test | Description |
|------|-------------|
| `PersonnelStatusChanged_includes_status_action_type` | `status.action_type` == ActionLog.ActionTypeId |
| `PersonnelStatusChanged_includes_status_action_text` | `status.action_text` == "Standing By"/"Responding"/etc. |
| `PersonnelStatusChanged_includes_previous_status` | `previous_status.*` populated |
| `PersonnelStatusChanged_sets_user_from_user_id` | `user.*` populated from ActionLog.UserId |

##### `when_building_user_created_variables`

| Test | Description |
|------|-------------|
| `UserCreated_includes_new_user_id` | `new_user.id` == IdentityUser.Id |
| `UserCreated_includes_new_user_email` | `new_user.email` == IdentityUser.Email |
| `UserCreated_includes_new_user_name` | `new_user.name` == Event.Name |

##### `when_building_user_assigned_to_group_variables`

| Test | Description |
|------|-------------|
| `UserAssignedToGroup_includes_group_name` | `group.name` == DepartmentGroup.Name |
| `UserAssignedToGroup_includes_previous_group` | `previous_group.name` set when PreviousGroup is not null |
| `UserAssignedToGroup_handles_null_previous_group` | `previous_group.*` are empty when PreviousGroup is null |

##### `when_building_document_variables`

| Test | Description |
|------|-------------|
| `DocumentAdded_includes_document_name` | `document.name` == Document.Name |
| `DocumentAdded_includes_document_category` | `document.category` == Document.Category |
| `DocumentAdded_includes_document_filename` | `document.filename` == Document.Filename |
| `DocumentAdded_sets_user_from_user_id` | `user.*` populated from Document.UserId |

##### `when_building_note_variables`

| Test | Description |
|------|-------------|
| `NoteAdded_includes_note_title` | `note.title` == Note.Title |
| `NoteAdded_includes_note_body` | `note.body` == Note.Body |
| `NoteAdded_includes_note_category` | `note.category` == Note.Category |

##### `when_building_unit_added_variables`

| Test | Description |
|------|-------------|
| `UnitAdded_includes_unit_name` | `unit.name` == Unit.Name |
| `UnitAdded_includes_unit_type` | `unit.type` == Unit.Type |
| `UnitAdded_includes_unit_vin` | `unit.vin` == Unit.VIN |

##### `when_building_log_variables`

| Test | Description |
|------|-------------|
| `LogAdded_includes_log_narrative` | `log.narrative` == Log.Narrative |
| `LogAdded_includes_log_type` | `log.type` == Log.Type |
| `LogAdded_includes_log_location` | `log.location` == Log.Location |
| `LogAdded_includes_log_contact_info` | `log.contact_name` and `log.contact_number` set |
| `LogAdded_sets_user_from_logged_by_user_id` | `user.*` populated from Log.LoggedByUserId |

##### `when_building_calendar_event_variables`

| Test | Description |
|------|-------------|
| `CalendarEventAdded_includes_calendar_title` | `calendar.title` == CalendarItem.Title |
| `CalendarEventAdded_includes_calendar_start_end` | `calendar.start` and `calendar.end` set |
| `CalendarEventAdded_includes_calendar_location` | `calendar.location` == CalendarItem.Location |

##### `when_building_shift_variables`

| Test | Description |
|------|-------------|
| `ShiftCreated_includes_shift_name` | `shift.name` == Shift.Name |
| `ShiftCreated_includes_shift_code` | `shift.code` == Shift.Code |
| `ShiftCreated_includes_shift_times` | `shift.start_time` and `shift.end_time` set |

##### `when_building_resource_order_variables`

| Test | Description |
|------|-------------|
| `ResourceOrderAdded_includes_order_title` | `order.title` == ResourceOrder.Title |
| `ResourceOrderAdded_includes_order_incident_info` | `order.incident_name` and `order.incident_address` set |
| `ResourceOrderAdded_includes_order_contact_info` | `order.contact_name` and `order.contact_number` set |

##### `when_building_message_variables` (new event)

| Test | Description |
|------|-------------|
| `MessageSent_includes_message_subject` | `message.subject` == Message.Subject |
| `MessageSent_includes_message_body` | `message.body` == Message.Body |
| `MessageSent_sets_user_from_sending_user_id` | `user.*` populated from Message.SendingUserId |

##### `when_building_training_variables` (new event)

| Test | Description |
|------|-------------|
| `TrainingAdded_includes_training_name` | `training.name` == Training.Name |
| `TrainingAdded_includes_training_description` | `training.description` == Training.Description |
| `TrainingAdded_includes_training_completion_date` | `training.to_be_completed_by` set |

##### `when_building_inventory_variables` (new event)

| Test | Description |
|------|-------------|
| `InventoryAdjusted_includes_inventory_amount` | `inventory.amount` == Inventory.Amount |
| `InventoryAdjusted_includes_inventory_previous_amount` | `inventory.previous_amount` set from event |
| `InventoryAdjusted_includes_inventory_type_name` | `inventory.type_name` == InventoryType.Type |

##### `when_building_certification_variables` (new event)

| Test | Description |
|------|-------------|
| `CertificationExpiring_includes_cert_name` | `certification.name` == PersonnelCertification.Name |
| `CertificationExpiring_includes_days_until_expiry` | `certification.days_until_expiry` == event value |
| `CertificationExpiring_includes_expires_on` | `certification.expires_on` set |
| `CertificationExpiring_sets_user_from_user_id` | `user.*` populated from PersonnelCertification.UserId |

##### `when_building_form_submitted_variables` (new event)

| Test | Description |
|------|-------------|
| `FormSubmitted_includes_form_name` | `form.name` == Form.Name |
| `FormSubmitted_includes_submitted_data` | `form.submitted_data` == JSON string from event |

##### `when_building_role_changed_variables` (new event)

| Test | Description |
|------|-------------|
| `PersonnelRoleChanged_includes_role_name` | `role_change.role_name` == event role name |
| `PersonnelRoleChanged_includes_action` | `role_change.action` == "Added" or "Removed" |

##### `when_building_group_variables` (new event)

| Test | Description |
|------|-------------|
| `GroupAdded_includes_group_name` | `group.name` == DepartmentGroup.Name |
| `GroupAdded_includes_group_coordinates` | `group.latitude` and `group.longitude` set |
| `GroupAdded_includes_group_address` | `group.address.street`, `group.address.city` set when Address present |

---

#### 13d. `WorkflowTemplateRenderingTests.cs`

File: `Tests/Resgrid.Tests/Services/WorkflowTemplateRenderingTests.cs`

End-to-end Scriban rendering tests — builds a context, renders a template, and asserts the output string. No mocks for the rendering itself; uses `WorkflowSampleDataGenerator` or `WorkflowHelpers` to build the `ScriptObject`.

| Test | Description |
|------|-------------|
| `Simple_variable_substitution_renders_correctly` | Template `"Hello {{ user.full_name }}"` → `"Hello John Smith"` |
| `Department_variables_render_in_email_body` | Multi-line HTML template with `{{ department.name }}`, `{{ department.address.full }}` → correct output |
| `Call_variables_render_in_api_post_body` | JSON template with `{{ call.name }}`, `{{ call.priority_text }}` → valid JSON output |
| `Conditional_logic_renders_correctly` | `{% if call.is_critical %}CRITICAL{% else %}Normal{% end %}` → correct branch |
| `Loop_renders_correctly` | Template iterating over a custom array variable → correct repeated output |
| `Missing_variable_renders_as_empty` | `{{ call.nonexistent_field }}` → empty string (no crash) |
| `Invalid_template_syntax_returns_error` | `{{ unclosed` → Scriban parse error captured |
| `Nested_object_access_renders_correctly` | `{{ department.address.city }}` → correct city value |
| `Date_formatting_works` | `{{ call.logged_on \| date.to_string "%Y-%m-%d" }}` → formatted date string |
| `Html_special_chars_in_variables_preserved` | Variable value with `<script>` is preserved in raw output (not auto-escaped) |
| `Large_template_renders_within_time_limit` | 10KB template with 50+ variables → renders in < 500ms |
| `All_event_types_render_sample_template_without_error` | Iterate over all `WorkflowTriggerEventType` values, build context via `WorkflowSampleDataGenerator`, render a generic template → no exceptions for any event type |

---

#### 13e. `WorkflowTemplateVariableCatalogTests.cs`

File: `Tests/Resgrid.Tests/Services/WorkflowTemplateVariableCatalogTests.cs`

| Test | Description |
|------|-------------|
| `GetVariableCatalog_returns_entries_for_all_event_types` | Every `WorkflowTriggerEventType` enum value has at least one entry in the catalog |
| `All_event_types_include_common_department_variables` | Every event type's catalog includes `department.name`, `department.code`, etc. (marked `IsCommon = true`) |
| `All_event_types_include_common_timestamp_variables` | Every event type includes `timestamp.utc_now`, `timestamp.department_now` |
| `All_event_types_include_common_user_variables` | Every event type includes `user.first_name`, `user.full_name`, etc. |
| `CallAdded_catalog_includes_call_specific_variables` | CallAdded entry contains `call.name`, `call.nature`, `call.priority_text`, etc. |
| `UnitStatusChanged_catalog_includes_unit_variables` | Contains `unit.name`, `unit_status.state_text`, `previous_unit_status.state_text` |
| `Each_variable_has_non_empty_name_and_description` | No catalog entry has blank Name or Description |
| `Each_variable_has_valid_data_type` | DataType is one of "string", "int", "bool", "datetime", "decimal", "double", "long" |
| `No_duplicate_variable_names_per_event_type` | No two entries share the same Name within an event type |
| `Catalog_variable_names_match_context_builder_output` | For each event type, build context via sample data generator, verify every catalog variable name exists as a key in the rendered context |

---

#### 13f. `WorkflowSampleDataGeneratorTests.cs`

File: `Tests/Resgrid.Tests/Services/WorkflowSampleDataGeneratorTests.cs`

| Test | Description |
|------|-------------|
| `GenerateSampleData_returns_non_null_for_all_event_types` | Iterate all `WorkflowTriggerEventType` values → no nulls |
| `GenerateSampleData_includes_department_variables` | Sample data has `department.name` as a non-empty string |
| `GenerateSampleData_includes_user_variables` | Sample data has `user.full_name` as a non-empty string |
| `GenerateSampleData_CallAdded_includes_call_variables` | Has `call.name`, `call.nature`, `call.priority_text` |
| `GenerateSampleData_UnitStatusChanged_includes_unit_variables` | Has `unit.name`, `unit_status.state_text` |
| `GenerateSampleData_PersonnelStaffingChanged_includes_staffing_variables` | Has `staffing.state_text` |
| `GenerateSampleData_can_render_template_without_error` | For each event type, render `"{{ department.name }} - {{ timestamp.utc_now }}"` → no exception |
| `GenerateSampleData_values_are_realistic` | `department.name` is not "string" or "test" but a realistic name like "Sample Fire Department" |

---

#### 13g. `WorkflowActionExecutorTests.cs`

File: `Tests/Resgrid.Tests/Providers/WorkflowActionExecutorTests.cs`

Tests for each action executor — these mock external dependencies (SMTP, HTTP, etc.) and verify the executor logic.

##### `when_using_http_api_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_sends_post_request_with_rendered_body` | Mock `HttpMessageHandler` → verify POST body matches rendered content |
| `ExecuteAsync_sends_get_request_without_body` | GET action → no body sent |
| `ExecuteAsync_sends_put_request_with_rendered_body` | PUT action → body sent |
| `ExecuteAsync_sends_delete_request` | DELETE action → correct method used |
| `ExecuteAsync_sets_custom_headers_from_action_config` | ActionConfig headers appear in request |
| `ExecuteAsync_sets_bearer_auth_from_credential` | Authorization header set with Bearer token |
| `ExecuteAsync_sets_basic_auth_from_credential` | Authorization header set with Basic encoded value |
| `ExecuteAsync_sets_api_key_header_from_credential` | Custom API key header set |
| `ExecuteAsync_returns_success_on_2xx_response` | 200/201 → Success = true |
| `ExecuteAsync_returns_failure_on_4xx_response` | 400/404 → Success = false, ResultMessage has status code |
| `ExecuteAsync_returns_failure_on_5xx_response` | 500 → Success = false |
| `ExecuteAsync_returns_failure_on_timeout` | HttpClient timeout → Success = false, ErrorDetail has timeout message |
| `ExecuteAsync_includes_response_body_in_result_message` | ResultMessage includes first 4000 chars of response body |

##### `when_using_smtp_email_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_sends_email_with_correct_to_address` | ActionConfig To → MimeMessage.To matches |
| `ExecuteAsync_sends_email_with_correct_subject` | ActionConfig Subject → MimeMessage.Subject matches |
| `ExecuteAsync_sends_email_with_rendered_html_body` | Rendered content → MimeMessage.HtmlBody matches |
| `ExecuteAsync_sends_email_with_cc_when_configured` | ActionConfig CC → MimeMessage.Cc populated |
| `ExecuteAsync_returns_success_on_send` | Mock SmtpClient success → Success = true |
| `ExecuteAsync_returns_failure_on_smtp_error` | Mock SmtpClient throws → Success = false, ErrorDetail set |
| `ExecuteAsync_uses_correct_smtp_credentials` | Decrypted host/port/user/pass used to connect |

##### `when_using_twilio_sms_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_sends_sms_with_correct_to_number` | ActionConfig To → Twilio API called with correct number |
| `ExecuteAsync_sends_sms_with_rendered_body` | Rendered content → message body matches |
| `ExecuteAsync_returns_success_on_send` | Mock Twilio success → Success = true |
| `ExecuteAsync_returns_failure_on_api_error` | Mock Twilio error → Success = false |
| `ExecuteAsync_uses_correct_twilio_credentials` | SID, Token, From number from decrypted credential |

##### `when_using_teams_message_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_posts_to_webhook_url_from_credential` | Mock `HttpMessageHandler` → request URL matches credential webhook URL |
| `ExecuteAsync_sends_text_payload` | Plain text rendered content → JSON payload `{ "text": "..." }` sent |
| `ExecuteAsync_sends_adaptive_card_when_template_is_json` | Rendered content is valid Adaptive Card JSON → posted as attachments payload |
| `ExecuteAsync_sets_title_and_theme_color_from_config` | ActionConfig title/themeColor appear in MessageCard payload |
| `ExecuteAsync_returns_success_on_2xx` | 200 response → Success = true |
| `ExecuteAsync_returns_failure_on_non_2xx` | 400/500 → Success = false, ErrorDetail has status |
| `ExecuteAsync_returns_failure_on_timeout` | HttpClient timeout → Success = false |

##### `when_using_slack_message_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_posts_to_webhook_url_from_credential` | Request URL matches credential webhook URL |
| `ExecuteAsync_sends_text_payload` | JSON payload includes `"text"` field with rendered content |
| `ExecuteAsync_includes_channel_override_when_configured` | ActionConfig channel → payload includes `"channel"` |
| `ExecuteAsync_includes_username_when_configured` | ActionConfig username → payload includes `"username"` |
| `ExecuteAsync_includes_icon_emoji_when_configured` | ActionConfig icon_emoji → payload includes `"icon_emoji"` |
| `ExecuteAsync_returns_success_on_ok_response` | Slack returns "ok" → Success = true |
| `ExecuteAsync_returns_failure_on_error_response` | Slack returns error text → Success = false |

##### `when_using_discord_message_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_posts_to_webhook_url_from_credential` | Request URL matches credential webhook URL |
| `ExecuteAsync_sends_content_payload` | JSON payload includes `"content"` field with rendered content |
| `ExecuteAsync_includes_username_override_when_configured` | ActionConfig username → payload includes `"username"` |
| `ExecuteAsync_includes_avatar_url_when_configured` | ActionConfig avatar_url → payload includes `"avatar_url"` |
| `ExecuteAsync_sends_embed_when_template_is_embed_json` | Rendered content is valid embed JSON → payload includes `"embeds"` array |
| `ExecuteAsync_returns_success_on_204` | Discord returns 204 No Content → Success = true |
| `ExecuteAsync_returns_failure_on_rate_limit` | 429 response → Success = false, ErrorDetail includes retry_after |

##### `when_using_azure_blob_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_uploads_to_correct_container_and_blob` | Mock `BlobContainerClient` → blob name matches ActionConfig path template |
| `ExecuteAsync_uploads_rendered_content_as_blob` | Uploaded content matches rendered template |
| `ExecuteAsync_sets_content_type_from_config` | ActionConfig content type → `BlobHttpHeaders.ContentType` set |
| `ExecuteAsync_uses_connection_string_from_credential` | `BlobServiceClient` constructed with decrypted connection string |
| `ExecuteAsync_returns_success_on_upload` | Mock success → Success = true |
| `ExecuteAsync_returns_failure_on_storage_exception` | `RequestFailedException` → Success = false, ErrorDetail set |
| `ExecuteAsync_creates_container_if_not_exists` | When container doesn't exist, `CreateIfNotExistsAsync` called |

##### `when_using_box_file_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_uploads_to_correct_folder_id` | Mock `BoxClient` → upload call targets folder ID from ActionConfig |
| `ExecuteAsync_uses_correct_filename_from_config` | Filename matches ActionConfig template |
| `ExecuteAsync_uploads_rendered_content` | Uploaded stream content matches rendered template |
| `ExecuteAsync_authenticates_with_jwt_credentials` | `BoxClient` created with JWT config from decrypted credential |
| `ExecuteAsync_returns_success_on_upload` | Mock success → Success = true, ResultMessage has file ID |
| `ExecuteAsync_returns_failure_on_box_exception` | `BoxException` → Success = false, ErrorDetail set |

##### `when_using_dropbox_file_executor`

| Test | Description |
|------|-------------|
| `ExecuteAsync_uploads_to_correct_path` | Mock `DropboxClient` → upload path matches ActionConfig target path |
| `ExecuteAsync_uses_correct_filename_from_config` | Path includes filename from ActionConfig template |
| `ExecuteAsync_uploads_rendered_content` | Uploaded content matches rendered template |
| `ExecuteAsync_uses_refresh_token_from_credential` | `DropboxClient` constructed with app key/secret + refresh token |
| `ExecuteAsync_uses_write_mode_from_config` | ActionConfig writeMode "overwrite" → `WriteMode.Overwrite` used |
| `ExecuteAsync_returns_success_on_upload` | Mock success → Success = true, ResultMessage has file metadata |
| `ExecuteAsync_returns_failure_on_api_exception` | `ApiException` → Success = false, ErrorDetail set |

##### `when_using_workflow_action_executor_factory`

| Test | Description |
|------|-------------|
| `GetExecutor_returns_correct_executor_for_each_action_type` | Each `WorkflowActionType` value (all 15) → correct executor type returned |
| `GetExecutor_throws_for_unknown_action_type` | Invalid int value → ArgumentException |

---

#### 13h. Test Data Files

Add to `Tests/Resgrid.Tests/Data/`:

- `WorkflowCallAddedEvent.json` — Serialized `CallAddedEvent` with full Call object
- `WorkflowUnitStatusEvent.json` — Serialized `UnitStatusEvent` with current and previous UnitState
- `WorkflowUserStaffingEvent.json` — Serialized `UserStaffingEvent`
- `WorkflowUserStatusEvent.json` — Serialized `UserStatusEvent` with ActionLog
- `WorkflowSampleTemplate.scriban` — A multi-line Scriban template using all common + call variables for rendering tests

---

#### Test Count Summary

| Test File | Test Count |
|-----------|-----------|
| `EncryptionServiceTests.cs` | 12 |
| `WorkflowServiceTests.cs` | 30 |
| `WorkflowTemplateContextBuilderTests.cs` | 72 |
| `WorkflowTemplateRenderingTests.cs` | 12 |
| `WorkflowTemplateVariableCatalogTests.cs` | 10 |
| `WorkflowSampleDataGeneratorTests.cs` | 8 |
| `WorkflowActionExecutorTests.cs` | 72 |
| **Total** | **216** |

---

### Summary of Projects Modified

| Project | Changes |
|---------|---------|
| `Resgrid.Config` | Add `WorkflowConfig.cs` |
| `Resgrid.Model` | Add 5 entity classes, 4 enums, 5 repository interfaces, `IWorkflowService`, `IEncryptionService`, `IWorkflowActionExecutor`, `IWorkflowActionExecutorFactory`, `IWorkflowTemplateContextBuilder`, `WorkflowQueueItem`, ~9 new event classes, `WorkflowActionContext`, `WorkflowActionResult`, `WorkflowHealthSummary`, `TemplateVariableDescriptor`, `WorkflowTemplateVariableCatalog` |
| `Resgrid.Services` | Add `WorkflowService`, `EncryptionService`, `WorkflowTemplateContextBuilder`, `WorkflowSampleDataGenerator`. Modify `DocumentsService`, `CalendarService`, `ShiftsService`, `MessageService`, `TrainingService`, `InventoryService`, `FormsService`, `PersonnelRolesService`, `DepartmentGroupsService` to fire new events |
| `Resgrid.Repositories.DataRepository` | Add 5 repository implementations, `Queries/Workflows/` folder, register in `DataModule.cs` |
| `Resgrid.Providers.Migrations` | Add `M0037_AddingWorkflows.cs` |
| `Resgrid.Providers.MigrationsPg` | Add PostgreSQL equivalent migration |
| `Resgrid.Providers.Bus` | Add `WorkflowEventProvider`, modify `OutboundQueueProvider` |
| `Resgrid.Providers.Bus.Rabbit` | Add `EnqueueWorkflowEvent` to `RabbitOutboundQueueProvider`, add queue consumer to `RabbitInboundQueueProvider` |
| **`Resgrid.Providers.Workflow`** *(new project)* | All action executors, `WorkflowActionExecutorFactory`, `WorkflowProviderModule` |
| `Resgrid.Workers.Framework` | Add `WorkflowQueueLogic` |
| `Resgrid.Workers.Console` | Add handler in `QueuesProcessorTask`, optionally `CertificationExpiryCheckTask` |
| `Resgrid.Web.Services` | Add `WorkflowsController`, `WorkflowCredentialsController`, DTOs |
| `Resgrid.Web` | Add `WorkflowsController` (User area), Views (Index, New, Edit, Runs, Health, Pending, Credentials, CredentialNew, CredentialEdit), navigation link |
| `Resgrid.Tests` | Add `EncryptionServiceTests.cs`, `WorkflowServiceTests.cs`, `WorkflowTemplateContextBuilderTests.cs`, `WorkflowTemplateRenderingTests.cs`, `WorkflowTemplateVariableCatalogTests.cs`, `WorkflowSampleDataGeneratorTests.cs`, `WorkflowActionExecutorTests.cs`, `WorkflowHelpers.cs`, `WorkflowTestData.cs`, test data JSON files (216 total tests) |

### NuGet Packages to Add

| Package | License | Used By |
|---------|---------|---------|
| `Scriban` | BSD-2 | `Resgrid.Services` (template rendering) |
| `MailKit` | MIT | `Resgrid.Providers.Workflow` (SMTP email) |
| `FluentFTP` | MIT | `Resgrid.Providers.Workflow` (FTP upload) |
| `SSH.NET` | MIT | `Resgrid.Providers.Workflow` (SFTP upload) |
| `AWSSDK.S3` | Apache 2.0 | `Resgrid.Providers.Workflow` (S3 upload) |
| `Twilio` | MIT | `Resgrid.Providers.Workflow` (SMS) |
| `Azure.Storage.Blobs` | MIT | `Resgrid.Providers.Workflow` (Azure Blob Storage upload) |
| `Box.V2` | Apache 2.0 | `Resgrid.Providers.Workflow` (Box file upload) |
| `Dropbox.Api` | MIT | `Resgrid.Providers.Workflow` (Dropbox file upload) |











