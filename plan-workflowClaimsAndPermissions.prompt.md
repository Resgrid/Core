# Plan: Workflow Claims, Permissions & Authorization

Add a complete authorization layer for the Workflow system by introducing new `PermissionTypes`, claim resources/policies, ClaimsLogic, Security UI controls, and runtime `IPermissionsService` checks — mirroring the existing patterns used by Calls, Shifts, Contacts, etc.

---

## Steps

### 1. Add New `PermissionTypes` Enum Values

**File:** `Core/Resgrid.Model/PermissionTypes.cs`

Add three new entries to the `PermissionTypes` enum after `ContactDelete` (which is currently index 21):

```csharp
CreateWorkflow = 22,           // Who can create/edit/delete workflows and workflow steps
ManageWorkflowCredentials = 23, // Who can create/edit/delete encrypted workflow credentials
ViewWorkflowRuns = 24           // Who can view workflow run history, logs, and health
```

**Rationale for three separate permissions:**

- `CreateWorkflow` — Controls who can create, edit, and delete workflows and their steps. Default: `DepartmentAdminsOnly` (like Shifts). This separates workflow authoring (which is a powerful capability since workflows can fire external actions) from general usage.
- `ManageWorkflowCredentials` — Controls who can create, edit, and delete encrypted credentials (SMTP passwords, API keys, S3 secrets). Default: `DepartmentAdminsOnly`. Credentials contain highly sensitive secrets that should be separately gated from workflow authoring.
- `ViewWorkflowRuns` — Controls who can view workflow run history, step logs, health dashboards, and pending runs. Default: `DepartmentAdminsOnly`. Run data may contain sensitive rendered output (e.g., email bodies, API payloads with PII).

---

### 2. Add `Workflow`, `WorkflowCredential`, and `WorkflowRun` Resource Constants to `ResgridClaimTypes`

**File:** `Providers/Resgrid.Providers.Claims/ResgridClaimTypes.cs`

Add three entries to the `Resources` static class:

```csharp
public const string Workflow = "Workflow";
public const string WorkflowCredential = "WorkflowCredential";
public const string WorkflowRun = "WorkflowRun";
```

Place them after the existing `Contacts` entry (line 62) to maintain alphabetical/logical grouping. Each constant maps to one of the three `PermissionTypes` and enables independent claim-based policy evaluation per resource.

---

### 3. Add `Workflow_*`, `WorkflowCredential_*`, and `WorkflowRun_*` Policy Name Constants to `ResgridResources`

**File:** `Providers/Resgrid.Providers.Claims/ResgridResources.cs`

Add twelve new constants at the end (before the closing brace), following the existing `Resource_Action` naming pattern:

```csharp
public const string Workflow_View = "Workflow_View";
public const string Workflow_Update = "Workflow_Update";
public const string Workflow_Create = "Workflow_Create";
public const string Workflow_Delete = "Workflow_Delete";

public const string WorkflowCredential_View = "WorkflowCredential_View";
public const string WorkflowCredential_Update = "WorkflowCredential_Update";
public const string WorkflowCredential_Create = "WorkflowCredential_Create";
public const string WorkflowCredential_Delete = "WorkflowCredential_Delete";

public const string WorkflowRun_View = "WorkflowRun_View";
public const string WorkflowRun_Update = "WorkflowRun_Update";
public const string WorkflowRun_Create = "WorkflowRun_Create";
public const string WorkflowRun_Delete = "WorkflowRun_Delete";
```

---

### 4. Add `AddWorkflowClaims`, `AddWorkflowCredentialClaims`, and `AddWorkflowRunClaims` Methods in `ClaimsLogic`

**File:** `Providers/Resgrid.Providers.Claims/ClaimsLogic.cs`

Create three new static methods following the exact same pattern as `AddShiftClaims` (lines 242–337).

#### 4a. `AddWorkflowClaims`

- Signature: `public static void AddWorkflowClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)`
- Checks for `PermissionTypes.CreateWorkflow` in the permissions list.
- If a permission is found, evaluates `PermissionActions` values:
  - `DepartmentAdminsOnly && isAdmin` → grant all four claims (View, Update, Create, Delete) on `ResgridClaimTypes.Resources.Workflow`
  - `DepartmentAdminsOnly && !isAdmin` → grant View only
  - `DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin)` → grant all four
  - `DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin` → grant View only
  - `DepartmentAdminsAndSelectRoles && isAdmin` → grant all four
  - `DepartmentAdminsAndSelectRoles && !isAdmin` → check `permission.Data` for comma-separated role IDs; if user has matching role → grant all four, else → View only
  - `Everyone` → grant all four
- If no permission is found (null), default to admin-only: grant View to everyone, grant Update/Create/Delete only if `isAdmin`.

#### 4b. `AddWorkflowCredentialClaims`

- Signature: `public static void AddWorkflowCredentialClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)`
- Identical structure to `AddWorkflowClaims` but:
  - Checks for `PermissionTypes.ManageWorkflowCredentials` in the permissions list.
  - Adds claims on `ResgridClaimTypes.Resources.WorkflowCredential`.
- If no permission is found (null), default to admin-only: grant View to everyone, grant Update/Create/Delete only if `isAdmin`.

#### 4c. `AddWorkflowRunClaims`

- Signature: `public static void AddWorkflowRunClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)`
- Identical structure but:
  - Checks for `PermissionTypes.ViewWorkflowRuns` in the permissions list.
  - Adds claims on `ResgridClaimTypes.Resources.WorkflowRun`.
- If no permission is found (null), default to admin-only: grant View to everyone, grant Update/Create/Delete only if `isAdmin`.

**Why the default is admin-only (not Everyone):** Unlike Calls or Notes which default to Everyone, Workflows are a powerful automation feature that can send emails, call APIs, upload files — it's a much higher-risk surface. Defaulting to `DepartmentAdminsOnly` ensures new departments don't accidentally grant workflow creation to all users. This matches the default behavior of Shifts.

---

### 5. Wire `AddWorkflowClaims`, `AddWorkflowCredentialClaims`, and `AddWorkflowRunClaims` into Token/Principal Factories

Both authentication paths (cookie auth for Web, JWT for API) must emit the new Workflow, WorkflowCredential, and WorkflowRun claims.

#### 5a. `ClaimsPrincipalFactory.cs`

**File:** `Providers/Resgrid.Providers.Claims/ClaimsPrincipalFactory.cs`

After the existing `ClaimsLogic.AddContactsClaims(...)` call (line 199), add:

```csharp
ClaimsLogic.AddWorkflowClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
ClaimsLogic.AddWorkflowCredentialClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
ClaimsLogic.AddWorkflowRunClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
```

#### 5b. `JwtTokenProvider.cs`

**File:** `Providers/Resgrid.Providers.Claims/JwtTokenProvider.cs`

After the existing `ClaimsLogic.AddContactsClaims(...)` call (line 126), add:

```csharp
ClaimsLogic.AddWorkflowClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
ClaimsLogic.AddWorkflowCredentialClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
ClaimsLogic.AddWorkflowRunClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
```

---

### 6. Register Authorization Policies in Both Startup Files

Both the API project and the Web project have their own `AddAuthorization` blocks that must register the new policies.

#### 6a. API Project Startup

**File:** `Web/Resgrid.Web.Services/Startup.cs`

After the `Contacts_Delete` policy registration (around line 381), before the closing `});`, add:

```csharp
options.AddPolicy(ResgridResources.Workflow_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Workflow, ResgridClaimTypes.Actions.View));
options.AddPolicy(ResgridResources.Workflow_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Workflow, ResgridClaimTypes.Actions.Update));
options.AddPolicy(ResgridResources.Workflow_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Workflow, ResgridClaimTypes.Actions.Create));
options.AddPolicy(ResgridResources.Workflow_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Workflow, ResgridClaimTypes.Actions.Delete));

options.AddPolicy(ResgridResources.WorkflowCredential_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowCredential, ResgridClaimTypes.Actions.View));
options.AddPolicy(ResgridResources.WorkflowCredential_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowCredential, ResgridClaimTypes.Actions.Update));
options.AddPolicy(ResgridResources.WorkflowCredential_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowCredential, ResgridClaimTypes.Actions.Create));
options.AddPolicy(ResgridResources.WorkflowCredential_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowCredential, ResgridClaimTypes.Actions.Delete));

options.AddPolicy(ResgridResources.WorkflowRun_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowRun, ResgridClaimTypes.Actions.View));
options.AddPolicy(ResgridResources.WorkflowRun_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowRun, ResgridClaimTypes.Actions.Update));
options.AddPolicy(ResgridResources.WorkflowRun_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowRun, ResgridClaimTypes.Actions.Create));
options.AddPolicy(ResgridResources.WorkflowRun_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.WorkflowRun, ResgridClaimTypes.Actions.Delete));
```

#### 6b. Web Project Startup

**File:** `Web/Resgrid.Web/Startup.cs`

After the `Contacts_Delete` policy (or the last existing policy in the block, around line 319), add the same twelve `options.AddPolicy(...)` calls.

---

### 7. Apply Workflow-Specific `[Authorize(Policy = ...)]` to API Controllers

Currently both `WorkflowsController` and `WorkflowCredentialsController` use generic `Department_View`/`Department_Update` policies. Replace them with the appropriate Workflow-specific policies.

#### 7a. `WorkflowsController` (API)

**File:** `Web/Resgrid.Web.Services/Controllers/v4/WorkflowsController.cs`

| Action Method | Current Policy | New Policy | Rationale |
|---|---|---|---|
| `GetAll` | `Department_View` | `Workflow_View` | Read workflows |
| `GetById` | `Department_View` | `Workflow_View` | Read single workflow |
| `Save` | `Department_Update` | `Workflow_Create` | Create or update a workflow |
| `Delete` | `Department_Update` | `Workflow_Delete` | Delete a workflow |
| `SaveStep` | `Department_Update` | `Workflow_Create` | Create or update workflow step |
| `DeleteStep` | `Department_Update` | `Workflow_Delete` | Delete a step |
| `GetCredentials` | `Department_View` | `WorkflowCredential_View` | List credentials (names only) |
| `SaveCredential` | `Department_Update` | `WorkflowCredential_Create` | Create or update a credential |
| `DeleteCredential` | `Department_Update` | `WorkflowCredential_Delete` | Delete a credential |
| `GetRuns` | `Department_View` | `WorkflowRun_View` | View run history |
| `GetRunsByWorkflow` | `Department_View` | `WorkflowRun_View` | View run history for specific workflow |
| `GetPendingRuns` | `Department_View` | `WorkflowRun_View` | View pending runs |
| `GetRunLogs` | `Department_View` | `WorkflowRun_View` | View step-level logs |
| `GetHealth` | `Department_View` | `WorkflowRun_View` | View health dashboard |
| `CancelRun` | `Department_Update` | `WorkflowRun_Delete` | Cancel is a destructive action on runs |
| `ClearPending` | `Department_Update` | `WorkflowRun_Delete` | Clearing pending runs is destructive |
| `GetTemplateVariables` | `Department_View` | `Workflow_View` | Read template variable catalog |

#### 7b. `WorkflowCredentialsController` (API)

**File:** `Web/Resgrid.Web.Services/Controllers/v4/WorkflowCredentialsController.cs`

| Action Method | Current Policy | New Policy |
|---|---|---|
| `GetAll` | `Department_View` | `WorkflowCredential_View` |
| `GetById` | `Department_View` | `WorkflowCredential_View` |
| `Create` | `Department_View` (incorrect!) | `WorkflowCredential_Create` |
| `Update` | `Department_View` (incorrect!) | `WorkflowCredential_Update` |
| `Delete` | `Department_View` (incorrect!) | `WorkflowCredential_Delete` |

---

### 8. Add Runtime `IPermissionsService` Checks to API Controllers

Claims-based policies alone are evaluated against the JWT token (issued at login time). For real-time security enforcement (e.g., if an admin changes permissions after the user logged in), add runtime database-validated permission checks in the controller action bodies.

#### 8a. Inject Dependencies

Add these constructor dependencies to both `WorkflowsController` and `WorkflowCredentialsController`:

```csharp
private readonly IPermissionsService _permissionsService;
private readonly IDepartmentGroupsService _departmentGroupsService;
private readonly IPersonnelRolesService _personnelRolesService;
```

#### 8b. Add Private Helper Method

Add a reusable private method to each controller (or a shared base if preferred):

```csharp
private async Task<bool> CanUserManageWorkflowsAsync()
{
    var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateWorkflow);
    var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
    var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
    var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
    bool isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
    return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(UserId), isGroupAdmin, roles);
}
```

Similarly, `CanUserManageWorkflowCredentialsAsync()` using `PermissionTypes.ManageWorkflowCredentials`, and `CanUserViewWorkflowRunsAsync()` using `PermissionTypes.ViewWorkflowRuns`.

#### 8c. Apply Checks in Action Methods

Add checks at the beginning of each mutating action:

- **Save, SaveStep:** `if (!await CanUserManageWorkflowsAsync()) return Forbid();`
- **Delete, DeleteStep:** `if (!await CanUserManageWorkflowsAsync()) return Forbid();`
- **SaveCredential:** `if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();`
- **DeleteCredential:** `if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();`
- **CancelRun, ClearPending:** `if (!await CanUserViewWorkflowRunsAsync()) return Forbid();` (destructive ops on runs, gated by the `ViewWorkflowRuns` permission)
- **GetRuns, GetRunsByWorkflow, GetPendingRuns, GetRunLogs, GetHealth:** `if (!await CanUserViewWorkflowRunsAsync()) return Forbid();`

#### 8d. Department Ownership Validation

Ensure **every** mutating endpoint validates `entity.DepartmentId == DepartmentId` before performing the operation. Currently missing on:

- `DeleteStep` — the step is deleted by ID without verifying the parent workflow belongs to the current department. Fix: load the step, load its workflow, verify `workflow.DepartmentId == DepartmentId`.
- `DeleteCredential` (on `WorkflowsController`) — similar issue. Fix: load the credential, verify `credential.DepartmentId == DepartmentId`.
- `ClearPending` — already passes `DepartmentId` to the service, so this is fine.
- `SaveCredential` (on `WorkflowsController`) — sets `DepartmentId = DepartmentId` on creation which is correct, but for update path (non-empty `WorkflowCredentialId`), should load existing and verify ownership.

---

### 9. Add `IAuthorizationService` Methods for Workflows

**File:** `Core/Resgrid.Model/Services/IAuthorizationService.cs`

Add three new method signatures (following the pattern of `CanUserCreateCallAsync`):

```csharp
Task<bool> CanUserCreateWorkflowAsync(string userId, int departmentId);
Task<bool> CanUserManageWorkflowCredentialAsync(string userId, int departmentId);
Task<bool> CanUserViewWorkflowRunsAsync(string userId, int departmentId);
```

**File:** `Core/Resgrid.Services/AuthorizationService.cs`

Implement these methods following the exact pattern of `CanUserCreateCallAsync` (lines 397–410):

```csharp
public async Task<bool> CanUserCreateWorkflowAsync(string userId, int departmentId)
{
    var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateWorkflow);
    bool isGroupAdmin = false;
    var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
    var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
    var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
    if (group != null)
        isGroupAdmin = group.IsUserGroupAdmin(userId);
    return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
}
```

Same pattern for the other two methods, using their respective `PermissionTypes`.

---

### 10. Update Web UI Controller to Use Permission Checks

**File:** `Web/Resgrid.Web/Areas/User/Controllers/WorkflowsController.cs`

Currently the controller uses `ClaimsAuthorizationHelper.IsUserDepartmentAdmin()` as the sole guard on every action. Replace with permission-based checks to allow non-admin users with appropriate permissions to manage workflows.

#### 10a. Inject Dependencies

Add `IPermissionsService`, `IDepartmentGroupsService`, and `IPersonnelRolesService` to the constructor.

#### 10b. Add Private Helper

```csharp
private async Task<bool> CanUserManageWorkflowsAsync()
{
    var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateWorkflow);
    var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
    var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
    var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
    bool isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
    return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(UserId), isGroupAdmin, roles);
}
```

#### 10c. Replace Guards

Replace all `ClaimsAuthorizationHelper.IsUserDepartmentAdmin()` checks with the appropriate permission helper:

- `Index`, `New`, `Edit`, `Delete`, `Credentials`, `CredentialNew`, `CredentialEdit`, `DeleteCredential` → `CanUserManageWorkflowsAsync()`
- `Runs`, `Health`, `Pending`, `CancelRun`, `ClearPending` → Either `CanUserManageWorkflowsAsync()` for destructive ops or a separate `CanUserViewWorkflowRunsAsync()` for read-only views.

---

### 11. Add Workflow Permissions to Security UI

#### 11a. Update PermissionsView Model

**File:** `Web/Resgrid.Web/Areas/User/Models/Security/PermissionsView.cs`

Add six new properties (following the `CreateShift`/`CreateShiftPermissions` pattern):

```csharp
public int CreateWorkflow { get; set; }
public SelectList CreateWorkflowPermissions { get; set; }

public int ManageWorkflowCredentials { get; set; }
public SelectList ManageWorkflowCredentialsPermissions { get; set; }

public int ViewWorkflowRuns { get; set; }
public SelectList ViewWorkflowRunsPermissions { get; set; }
```

#### 11b. Update SecurityController Index Action

**File:** `Web/Resgrid.Web/Areas/User/Controllers/SecurityController.cs`

In the `Index()` method, after the existing `ContactDelete` permission loading block (around line 321), add:

**CreateWorkflow** (default: DepartmentAdminsOnly = 0):

```csharp
if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateWorkflow))
    model.CreateWorkflow = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateWorkflow).Action;

var createWorkflowPermissions = new List<dynamic>();
createWorkflowPermissions.Add(new { Id = 0, Name = "Department Admins" });
createWorkflowPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
createWorkflowPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
model.CreateWorkflowPermissions = new SelectList(createWorkflowPermissions, "Id", "Name");
```

Note: No "Everyone" option (Id = 3) for `CreateWorkflow` by design — same as Shifts and Trainings. Workflow creation is a high-privilege operation.

**ManageWorkflowCredentials** (default: DepartmentAdminsOnly = 0):

```csharp
if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ManageWorkflowCredentials))
    model.ManageWorkflowCredentials = permissions.First(x => x.PermissionType == (int)PermissionTypes.ManageWorkflowCredentials).Action;

var manageWorkflowCredentialsPermissions = new List<dynamic>();
manageWorkflowCredentialsPermissions.Add(new { Id = 0, Name = "Department Admins" });
manageWorkflowCredentialsPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
manageWorkflowCredentialsPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
model.ManageWorkflowCredentialsPermissions = new SelectList(manageWorkflowCredentialsPermissions, "Id", "Name");
```

No "Everyone" option for credentials management either — credentials contain sensitive secrets.

**ViewWorkflowRuns** (default: DepartmentAdminsOnly = 0):

```csharp
if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ViewWorkflowRuns))
    model.ViewWorkflowRuns = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewWorkflowRuns).Action;

var viewWorkflowRunsPermissions = new List<dynamic>();
viewWorkflowRunsPermissions.Add(new { Id = 0, Name = "Department Admins" });
viewWorkflowRunsPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
viewWorkflowRunsPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
viewWorkflowRunsPermissions.Add(new { Id = 3, Name = "Everyone" });
model.ViewWorkflowRunsPermissions = new SelectList(viewWorkflowRunsPermissions, "Id", "Name");
```

`ViewWorkflowRuns` includes "Everyone" since viewing run history is less sensitive than authoring workflows.

#### 11c. Update Security Index View

**File:** `Web/Resgrid.Web/Areas/User/Views/Security/Index.cshtml`

Add three new `<tr>` rows to the permissions table, after the "Who can delete Contacts" row and before the closing `</tbody>`. Follow the exact existing row structure:

**Row 1 — "Who can Create/Edit Workflows":**

```html
<tr>
    <td>Who can Create/Edit Workflows</td>
    <td style="max-width: 350px">
        This option determines who can create, edit, and delete workflows and workflow steps. By default only Department Admins can manage workflows.
    </td>
    <td>@Html.DropDownListFor(m => m.CreateWorkflow, Model.CreateWorkflowPermissions)</td>
    <td>N/A</td>
    <td>
        <span id="workflowCreateNoRolesSpan">No Roles</span>
        <div id="workflowCreateRolesDiv" style="display: none;">
            <select id="workflowCreateRoles" name="workflowCreateRoles"></select>
        </div>
    </td>
</tr>
```

**Row 2 — "Who can Manage Workflow Credentials":**

```html
<tr>
    <td>Who can Manage Workflow Credentials</td>
    <td style="max-width: 350px">
        This option determines who can create, edit, and delete encrypted credentials used by workflow actions (e.g., SMTP passwords, API keys). By default only Department Admins can manage credentials.
    </td>
    <td>@Html.DropDownListFor(m => m.ManageWorkflowCredentials, Model.ManageWorkflowCredentialsPermissions)</td>
    <td>N/A</td>
    <td>
        <span id="workflowCredentialsNoRolesSpan">No Roles</span>
        <div id="workflowCredentialsRolesDiv" style="display: none;">
            <select id="workflowCredentialsRoles" name="workflowCredentialsRoles"></select>
        </div>
    </td>
</tr>
```

**Row 3 — "Who can View Workflow Runs":**

```html
<tr>
    <td>Who can View Workflow Runs</td>
    <td style="max-width: 350px">
        This option determines who can view workflow execution history, run logs, and health dashboards. By default only Department Admins can view workflow runs.
    </td>
    <td>@Html.DropDownListFor(m => m.ViewWorkflowRuns, Model.ViewWorkflowRunsPermissions)</td>
    <td>N/A</td>
    <td>
        <span id="workflowRunsNoRolesSpan">No Roles</span>
        <div id="workflowRunsRolesDiv" style="display: none;">
            <select id="workflowRunsRoles" name="workflowRunsRoles"></select>
        </div>
    </td>
</tr>
```

#### 11d. Update Security Permissions JavaScript

**File:** `Web/Resgrid.Web/wwwroot/js/app/internal/security/resgrid.security.permissions.js`

Add change handlers and roles multi-selects for the three new permission types. The `type` parameter values correspond to the enum integer values: `CreateWorkflow = 22`, `ManageWorkflowCredentials = 23`, `ViewWorkflowRuns = 24`.

Add the following blocks before the closing `});` of the `$(document).ready(...)` function (after the `DeleteContacts` block at line ~1197):

**CreateWorkflow (type=22):**

```javascript
// Create/Edit Workflows
$('#CreateWorkflow').change(function () {
    var val = this.value;
    $.ajax({
        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=22&perm=' + val,
        type: 'GET'
    }).done(function (results) { });
    if (val === "2") {
        $('#workflowCreateNoRolesSpan').hide();
        $('#workflowCreateRolesDiv').show();
    } else {
        $('#workflowCreateNoRolesSpan').show();
        $('#workflowCreateRolesDiv').hide();
    }
});
if ($("#CreateWorkflow").val() === "2") {
    $('#workflowCreateNoRolesSpan').hide();
    $('#workflowCreateRolesDiv').show();
} else {
    $('#workflowCreateNoRolesSpan').show();
    $('#workflowCreateRolesDiv').hide();
}
$("#workflowCreateRoles").kendoMultiSelect({
    placeholder: "Select roles...",
    dataTextField: "Name",
    dataValueField: "RoleId",
    change: function () {
        var multiSelect = $("#workflowCreateRoles").data("kendoMultiSelect");
        $.ajax({
            url: resgrid.absoluteBaseUrl + '/User/Security/SetPermissionData?type=22&data=' + encodeURIComponent(multiSelect.value()),
            type: 'GET'
        }).done(function (results) { });
    },
    autoBind: false,
    dataSource: { transport: { read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles' } }
});
$.ajax({
    url: resgrid.absoluteBaseUrl + '/User/Security/GetRolesForPermission?type=22',
    contentType: 'application/json', type: 'GET'
}).done(function (data) {
    if (data) {
        var multiSelect = $("#workflowCreateRoles").data("kendoMultiSelect");
        multiSelect.value(data.split(","));
    }
});
```

**ManageWorkflowCredentials (type=23):**

```javascript
// Manage Workflow Credentials
$('#ManageWorkflowCredentials').change(function () {
    var val = this.value;
    $.ajax({
        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=23&perm=' + val,
        type: 'GET'
    }).done(function (results) { });
    if (val === "2") {
        $('#workflowCredentialsNoRolesSpan').hide();
        $('#workflowCredentialsRolesDiv').show();
    } else {
        $('#workflowCredentialsNoRolesSpan').show();
        $('#workflowCredentialsRolesDiv').hide();
    }
});
if ($("#ManageWorkflowCredentials").val() === "2") {
    $('#workflowCredentialsNoRolesSpan').hide();
    $('#workflowCredentialsRolesDiv').show();
} else {
    $('#workflowCredentialsNoRolesSpan').show();
    $('#workflowCredentialsRolesDiv').hide();
}
$("#workflowCredentialsRoles").kendoMultiSelect({
    placeholder: "Select roles...",
    dataTextField: "Name",
    dataValueField: "RoleId",
    change: function () {
        var multiSelect = $("#workflowCredentialsRoles").data("kendoMultiSelect");
        $.ajax({
            url: resgrid.absoluteBaseUrl + '/User/Security/SetPermissionData?type=23&data=' + encodeURIComponent(multiSelect.value()),
            type: 'GET'
        }).done(function (results) { });
    },
    autoBind: false,
    dataSource: { transport: { read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles' } }
});
$.ajax({
    url: resgrid.absoluteBaseUrl + '/User/Security/GetRolesForPermission?type=23',
    contentType: 'application/json', type: 'GET'
}).done(function (data) {
    if (data) {
        var multiSelect = $("#workflowCredentialsRoles").data("kendoMultiSelect");
        multiSelect.value(data.split(","));
    }
});
```

**ViewWorkflowRuns (type=24):**

```javascript
// View Workflow Runs
$('#ViewWorkflowRuns').change(function () {
    var val = this.value;
    $.ajax({
        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=24&perm=' + val,
        type: 'GET'
    }).done(function (results) { });
    if (val === "2") {
        $('#workflowRunsNoRolesSpan').hide();
        $('#workflowRunsRolesDiv').show();
    } else {
        $('#workflowRunsNoRolesSpan').show();
        $('#workflowRunsRolesDiv').hide();
    }
});
if ($("#ViewWorkflowRuns").val() === "2") {
    $('#workflowRunsNoRolesSpan').hide();
    $('#workflowRunsRolesDiv').show();
} else {
    $('#workflowRunsNoRolesSpan').show();
    $('#workflowRunsRolesDiv').hide();
}
$("#workflowRunsRoles").kendoMultiSelect({
    placeholder: "Select roles...",
    dataTextField: "Name",
    dataValueField: "RoleId",
    change: function () {
        var multiSelect = $("#workflowRunsRoles").data("kendoMultiSelect");
        $.ajax({
            url: resgrid.absoluteBaseUrl + '/User/Security/SetPermissionData?type=24&data=' + encodeURIComponent(multiSelect.value()),
            type: 'GET'
        }).done(function (results) { });
    },
    autoBind: false,
    dataSource: { transport: { read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles' } }
});
$.ajax({
    url: resgrid.absoluteBaseUrl + '/User/Security/GetRolesForPermission?type=24',
    contentType: 'application/json', type: 'GET'
}).done(function (data) {
    if (data) {
        var multiSelect = $("#workflowRunsRoles").data("kendoMultiSelect");
        multiSelect.value(data.split(","));
    }
});
```

---

### 12. Extend API Security Controller Response

**File:** `Web/Resgrid.Web.Services/Models/v4/Security/DepartmentRightsResult.cs`

Add three new boolean properties to `DepartmentRightsResultData`:

```csharp
/// <summary>
/// Can the user create/edit workflows
/// </summary>
public bool CanCreateWorkflow { get; set; }

/// <summary>
/// Can the user manage workflow credentials
/// </summary>
public bool CanManageWorkflowCredentials { get; set; }

/// <summary>
/// Can the user view workflow runs
/// </summary>
public bool CanViewWorkflowRuns { get; set; }
```

**File:** `Web/Resgrid.Web.Services/Controllers/v4/SecurityController.cs`

In `GetCurrentUsersRights()`, after the existing permission checks (line ~101), add:

```csharp
var createWorkflowPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateWorkflow);
var manageWorkflowCredentialPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ManageWorkflowCredentials);
var viewWorkflowRunsPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ViewWorkflowRuns);

result.Data.CanCreateWorkflow = _permissionsService.IsUserAllowed(createWorkflowPermission, result.Data.IsAdmin, isGroupAdmin, roles);
result.Data.CanManageWorkflowCredentials = _permissionsService.IsUserAllowed(manageWorkflowCredentialPermission, result.Data.IsAdmin, isGroupAdmin, roles);
result.Data.CanViewWorkflowRuns = _permissionsService.IsUserAllowed(viewWorkflowRunsPermission, result.Data.IsAdmin, isGroupAdmin, roles);
```

---

### 13. Update `SecurityLogic` in Workers Framework

**File:** `Workers/Resgrid.Workers.Framework/Logic/SecurityLogic.cs`

The `SecurityLogic` class has methods that evaluate permissions for worker processes. If any worker needs to check workflow permissions (e.g., the workflow queue processor validating that a department still has permission to run workflows), add handling for the three new `PermissionTypes` values in the relevant `switch`/`if` blocks. Follow the existing pattern for handling `PermissionActions` evaluation.

This step may not be required initially since the worker processes the queue and the permission was already validated at enqueue time (API/Web layer). However, for defense-in-depth, consider adding a permission check in `WorkflowQueueLogic.ProcessWorkflowQueueItem` that verifies the workflow's `DepartmentId` still has `CreateWorkflow` permission enabled before executing.

---

## Summary of Files Changed

| File | Change Description |
|---|---|
| `Core/Resgrid.Model/PermissionTypes.cs` | Add `CreateWorkflow`, `ManageWorkflowCredentials`, `ViewWorkflowRuns` enum values |
| `Providers/Resgrid.Providers.Claims/ResgridClaimTypes.cs` | Add `Workflow`, `WorkflowCredential`, `WorkflowRun` resource constants |
| `Providers/Resgrid.Providers.Claims/ResgridResources.cs` | Add `Workflow_View/Update/Create/Delete`, `WorkflowCredential_View/Update/Create/Delete`, `WorkflowRun_View/Update/Create/Delete` policy name constants (12 total) |
| `Providers/Resgrid.Providers.Claims/ClaimsLogic.cs` | Add `AddWorkflowClaims`, `AddWorkflowCredentialClaims`, `AddWorkflowRunClaims` methods |
| `Providers/Resgrid.Providers.Claims/ClaimsPrincipalFactory.cs` | Call all three `AddWorkflow*Claims` methods |
| `Providers/Resgrid.Providers.Claims/JwtTokenProvider.cs` | Call all three `AddWorkflow*Claims` methods |
| `Web/Resgrid.Web.Services/Startup.cs` | Register twelve Workflow/WorkflowCredential/WorkflowRun authorization policies |
| `Web/Resgrid.Web/Startup.cs` | Register twelve Workflow/WorkflowCredential/WorkflowRun authorization policies |
| `Web/Resgrid.Web.Services/Controllers/v4/WorkflowsController.cs` | Replace policies with `Workflow_*`, `WorkflowCredential_*`, `WorkflowRun_*`; add `IPermissionsService` checks; add department ownership validation |
| `Web/Resgrid.Web.Services/Controllers/v4/WorkflowCredentialsController.cs` | Replace policies with `WorkflowCredential_*`; add `IPermissionsService` checks |
| `Core/Resgrid.Model/Services/IAuthorizationService.cs` | Add `CanUserCreateWorkflowAsync`, `CanUserManageWorkflowCredentialAsync`, `CanUserViewWorkflowRunsAsync` |
| `Core/Resgrid.Services/AuthorizationService.cs` | Implement the three new methods |
| `Web/Resgrid.Web/Areas/User/Controllers/WorkflowsController.cs` | Inject dependencies, replace admin-only checks with permission-based checks |
| `Web/Resgrid.Web/Areas/User/Models/Security/PermissionsView.cs` | Add model properties for three workflow permission dropdowns |
| `Web/Resgrid.Web/Areas/User/Controllers/SecurityController.cs` | Load and populate workflow permission dropdowns in `Index()` |
| `Web/Resgrid.Web/Areas/User/Views/Security/Index.cshtml` | Add three permission table rows |
| `Web/Resgrid.Web/wwwroot/js/app/internal/security/resgrid.security.permissions.js` | Add JS change handlers and role multi-selects for workflow permissions |
| `Web/Resgrid.Web.Services/Models/v4/Security/DepartmentRightsResult.cs` | Add `CanCreateWorkflow`, `CanManageWorkflowCredentials`, `CanViewWorkflowRuns` properties |
| `Web/Resgrid.Web.Services/Controllers/v4/SecurityController.cs` | Return workflow permission flags in `GetCurrentUsersRights()` |

---

## Design Decisions & Rationale

1. **Three separate permissions instead of one:** Workflow creation, credential management, and run viewing are distinct concerns with different security implications. A user might need to view run health for monitoring without being able to author workflows or manage encrypted secrets.

2. **Default to DepartmentAdminsOnly:** Unlike Calls or Notes that default to Everyone, workflows can trigger external side effects (send emails, call APIs, upload files). Restricting by default prevents accidental exposure.

3. **No "Everyone" option for CreateWorkflow and ManageWorkflowCredentials:** These are high-privilege operations. The most permissive option is "Department Admins and Select Roles" which still requires explicit role assignment. `ViewWorkflowRuns` does include "Everyone" since run data is read-only monitoring.

4. **Dual-layer authorization (claims + runtime checks):** Claims are evaluated from the token (fast, cached at login). Runtime `IPermissionsService` checks hit the database (slower but always current). This matches the existing architecture where both layers are used together.

5. **Credential permissions separated from workflow permissions:** Even if a user can create workflows, they may not be trusted with raw credential management (passwords, API keys). This allows departments to have "workflow editors" who select from existing credentials but cannot create or view credential secrets.









