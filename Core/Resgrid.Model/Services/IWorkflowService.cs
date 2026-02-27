using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Resgrid.Model.Services
{
public interface IWorkflowService
{
// ── Workflow CRUD ──────────────────────────────────────────────────────────────
Task<Workflow> GetWorkflowByIdAsync(string workflowId, CancellationToken cancellationToken = default);
Task<List<Workflow>> GetWorkflowsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default);
Task<Workflow> SaveWorkflowAsync(Workflow workflow, CancellationToken cancellationToken = default);
Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
Task<List<Workflow>> GetActiveWorkflowsByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType, CancellationToken cancellationToken = default);
/// <summary>Returns true when a workflow already exists for the given trigger event type in the department.</summary>
Task<bool> WorkflowExistsForEventTypeAsync(int departmentId, int triggerEventType, CancellationToken cancellationToken = default);
/// <summary>Returns the set of <see cref="WorkflowTriggerEventType"/> integer values already claimed by a workflow in the department.</summary>
Task<IReadOnlyCollection<int>> GetUsedEventTypesForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);
/// <summary>Returns true when the department is allowed to create an additional workflow (enforces plan-based caps).</summary>
Task<bool> CanAddWorkflowAsync(int departmentId, bool isFreePlan, CancellationToken cancellationToken = default);
/// <summary>Returns true when the workflow is allowed to have an additional step added (enforces plan-based caps).</summary>
Task<bool> CanAddStepAsync(string workflowId, bool isFreePlan, CancellationToken cancellationToken = default);
// ── Step CRUD ──────────────────────────────────────────────────────────────────
Task<WorkflowStep> GetStepByIdAsync(string stepId, CancellationToken cancellationToken = default);
Task<WorkflowStep> SaveWorkflowStepAsync(WorkflowStep step, CancellationToken cancellationToken = default);
Task<bool> DeleteWorkflowStepAsync(string stepId, CancellationToken cancellationToken = default);
Task<List<WorkflowStep>> GetStepsByWorkflowIdAsync(string workflowId, CancellationToken cancellationToken = default);
// ── Credential CRUD ────────────────────────────────────────────────────────────
Task<WorkflowCredential> GetCredentialByIdAsync(string credentialId, CancellationToken cancellationToken = default);
Task<List<WorkflowCredential>> GetCredentialsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default);
/// <summary>
/// Saves the credential, encrypting <see cref="WorkflowCredential.EncryptedData"/> using
/// the department-specific key derived from <paramref name="departmentCode"/>.
/// </summary>
Task<WorkflowCredential> SaveCredentialAsync(WorkflowCredential credential, string departmentCode, CancellationToken cancellationToken = default);
Task<bool> DeleteCredentialAsync(string credentialId, CancellationToken cancellationToken = default);
// ── Execution ─────────────────────────────────────────────────────────────────
/// <summary>
/// Executes all enabled steps of the workflow against the provided event payload.
/// Creates / updates WorkflowRun and WorkflowRunLog records.
/// </summary>
Task<WorkflowRun> ExecuteWorkflowAsync(string workflowId, string eventPayloadJson, int departmentId, string departmentCode, int attemptNumber = 1, string existingRunId = null, CancellationToken cancellationToken = default);
Task<bool> CancelWorkflowRunAsync(string workflowRunId, CancellationToken cancellationToken = default);
// ── Run Queries (Audit / Monitoring) ──────────────────────────────────────────
Task<WorkflowRun> GetWorkflowRunByIdAsync(string workflowRunId, CancellationToken cancellationToken = default);
Task<List<WorkflowRun>> GetRunsByDepartmentIdAsync(int departmentId, int page, int pageSize, CancellationToken cancellationToken = default);
Task<List<WorkflowRun>> GetRunsByWorkflowIdAsync(string workflowId, int page, int pageSize, CancellationToken cancellationToken = default);
Task<List<WorkflowRun>> GetPendingAndRunningRunsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default);
Task<List<WorkflowRunLog>> GetLogsForRunAsync(string workflowRunId, CancellationToken cancellationToken = default);
Task<WorkflowHealthSummary> GetWorkflowHealthAsync(string workflowId, CancellationToken cancellationToken = default);
Task<bool> ClearPendingRunsAsync(int departmentId, CancellationToken cancellationToken = default);
}
}
