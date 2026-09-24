using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Scriban;

namespace Resgrid.Services
{
	public partial class WorkflowService : IWorkflowService
	{
		private readonly IWorkflowRepository _workflowRepository;
		private readonly IWorkflowStepRepository _stepRepository;
		private readonly IWorkflowCredentialRepository _credentialRepository;
		private readonly IWorkflowRunRepository _runRepository;
		private readonly IWorkflowRunLogRepository _runLogRepository;
		private readonly IWorkflowDailyUsageRepository _dailyUsageRepository;
		private readonly IEncryptionService _encryptionService;
		private readonly IWorkflowActionExecutorFactory _executorFactory;
		private readonly IWorkflowTemplateContextBuilder _contextBuilder;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IRecordsExportService _recordsExportService;
		private readonly Lazy<IProtectedProjectionService> _protectedProjection;
		private readonly Lazy<IReadinessHistoryProtectionService> _history;
		private IReadinessHistoryProtectionService History => _history?.Value ?? throw new InvalidOperationException("Readiness history protection is unavailable.");
		private readonly IProtectedWorkflowRuntime _protectedRuntime;
		private readonly IProtectedWorkflowService _protectedWorkflows;

		public WorkflowService(
			IWorkflowRepository workflowRepository,
			IWorkflowStepRepository stepRepository,
			IWorkflowCredentialRepository credentialRepository,
			IWorkflowRunRepository runRepository,
			IWorkflowRunLogRepository runLogRepository,
			IWorkflowDailyUsageRepository dailyUsageRepository,
			IEncryptionService encryptionService,
			IWorkflowActionExecutorFactory executorFactory,
			IWorkflowTemplateContextBuilder contextBuilder,
			ISubscriptionsService subscriptionsService,
			IRecordsExportService recordsExportService, Lazy<IProtectedProjectionService> protectedProjection = null, Lazy<IReadinessHistoryProtectionService> history = null,
			IProtectedWorkflowRuntime protectedRuntime = null, IProtectedWorkflowService protectedWorkflows = null)
		{
			_protectedRuntime = protectedRuntime;
			_protectedWorkflows = protectedWorkflows;
			_recordsExportService = recordsExportService;
			_protectedProjection = protectedProjection;
			_history = history;
			_workflowRepository = workflowRepository;
			_stepRepository = stepRepository;
			_credentialRepository = credentialRepository;
			_runRepository = runRepository;
			_runLogRepository = runLogRepository;
			_dailyUsageRepository = dailyUsageRepository;
			_encryptionService = encryptionService;
			_executorFactory = executorFactory;
			_contextBuilder = contextBuilder;
			_subscriptionsService = subscriptionsService;
		}

		// ── Workflow CRUD ─────────────────────────────────────────────────────────────

		public async Task<Workflow> GetWorkflowByIdAsync(string workflowId, CancellationToken cancellationToken = default)
			=> await _workflowRepository.GetByIdAsync(workflowId);

		public async Task<List<Workflow>> GetWorkflowsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var results = await _workflowRepository.GetAllByDepartmentIdAsync(departmentId);
			return results?.ToList() ?? new List<Workflow>();
		}

		public async Task<Workflow> SaveWorkflowAsync(Workflow workflow, CancellationToken cancellationToken = default)
		{
			// Enforce MaxRetryCount ceiling regardless of plan
			if (workflow.MaxRetryCount > WorkflowConfig.MaxAllowedRetryCount)
				workflow.MaxRetryCount = WorkflowConfig.MaxAllowedRetryCount;

			if (string.IsNullOrEmpty(workflow.WorkflowId))
			{
				workflow.WorkflowId = Guid.NewGuid().ToString();
				workflow.CreatedOn = DateTime.UtcNow;
				return await _workflowRepository.InsertAsync(workflow, cancellationToken);
			}
			else
			{
				workflow.UpdatedOn = DateTime.UtcNow;
				await _workflowRepository.UpdateAsync(workflow, cancellationToken);
				await OnProtectedConfigurationChangedAsync(workflow.WorkflowId, null, cancellationToken);
				return workflow;
			}
		}

		public async Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
		{
			var workflow = await _workflowRepository.GetByIdAsync(workflowId);
			if (workflow == null) return false;

			// Atomically delete all child records (WorkflowRunLogs → WorkflowRuns → WorkflowSteps)
			// and the workflow itself within a single database transaction. This prevents the
			// FK_WorkflowRuns_Workflows constraint violation that occurs when a background worker
			// inserts a new WorkflowRun between the sequential per-table deletes.
			await _workflowRepository.DeleteWorkflowWithAllDependenciesAsync(workflowId);
			await OnProtectedWorkflowDeletedAsync(workflow, cancellationToken);

			return true;
		}

		public async Task<List<Workflow>> GetActiveWorkflowsByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType, CancellationToken cancellationToken = default)
		{
			var results = await _workflowRepository.GetAllActiveByDepartmentAndEventTypeAsync(departmentId, triggerEventType);
			return results?.ToList() ?? new List<Workflow>();
		}

		public async Task<bool> CanAddWorkflowAsync(int departmentId, bool isFreePlan, CancellationToken cancellationToken = default)
		{
			var max = isFreePlan ? WorkflowConfig.FreeMaxWorkflowsPerDepartment : WorkflowConfig.MaxWorkflowsPerDepartment;
			var existing = await _workflowRepository.GetAllByDepartmentIdAsync(departmentId);
			return (existing?.Count() ?? 0) < max;
		}

		public async Task<bool> CanAddStepAsync(string workflowId, bool isFreePlan, CancellationToken cancellationToken = default)
		{
			var max = isFreePlan ? WorkflowConfig.FreeMaxStepsPerWorkflow : WorkflowConfig.MaxStepsPerWorkflow;
			var existing = await _stepRepository.GetAllByWorkflowIdAsync(workflowId);
			return (existing?.Count() ?? 0) < max;
		}

		// ── Step CRUD ─────────────────────────────────────────────────────────────────

		public async Task<WorkflowStep> GetStepByIdAsync(string stepId, CancellationToken cancellationToken = default)
			=> await _stepRepository.GetByIdAsync(stepId);

		public async Task<WorkflowStep> SaveWorkflowStepAsync(WorkflowStep step, CancellationToken cancellationToken = default)
		{
			// Normalise line endings to \n so templates are stored consistently
			// regardless of client platform (avoids \r\n being round-tripped back
			// as literal \r characters when the template is rendered or re-loaded).
			if (!string.IsNullOrEmpty(step.OutputTemplate))
				step.OutputTemplate = step.OutputTemplate.Replace("\r\n", "\n").Replace("\r", "\n");

			// Enforce OutputTemplate size cap
			if (!string.IsNullOrEmpty(step.OutputTemplate) && step.OutputTemplate.Length > WorkflowConfig.MaxOutputTemplateLength)
				step.OutputTemplate = step.OutputTemplate.Substring(0, WorkflowConfig.MaxOutputTemplateLength);

			// Normalise ConditionExpression line endings
			if (!string.IsNullOrEmpty(step.ConditionExpression))
				step.ConditionExpression = step.ConditionExpression.Replace("\r\n", "\n").Replace("\r", "\n");

			// Enforce ConditionExpression size cap
			if (!string.IsNullOrEmpty(step.ConditionExpression) && step.ConditionExpression.Length > WorkflowConfig.MaxConditionExpressionLength)
				step.ConditionExpression = step.ConditionExpression.Substring(0, WorkflowConfig.MaxConditionExpressionLength);

			// Validate Scriban syntax — clear to null rather than storing an unparseable expression
			if (!string.IsNullOrWhiteSpace(step.ConditionExpression))
			{
				var parsedCondition = Template.Parse(step.ConditionExpression);
				if (parsedCondition.HasErrors)
					step.ConditionExpression = null;
			}

			if (string.IsNullOrEmpty(step.WorkflowStepId))
			{
				step.WorkflowStepId = Guid.NewGuid().ToString();
				step.CreatedOn = DateTime.UtcNow;
				var inserted = await _stepRepository.InsertAsync(step, cancellationToken);
				await OnProtectedConfigurationChangedAsync(step.WorkflowId, step.CreatedByUserId, cancellationToken);
				return inserted;
			}

			// Fetch the existing record to preserve immutable audit fields (CreatedOn, CreatedByUserId).
			var existing = await _stepRepository.GetByIdAsync(step.WorkflowStepId);
			if (existing == null)
				throw new KeyNotFoundException($"WorkflowStep '{step.WorkflowStepId}' was not found. It may have been deleted or the ID is stale.");

			step.CreatedOn = existing.CreatedOn;
			step.CreatedByUserId = existing.CreatedByUserId;
			step.UpdatedOn = DateTime.UtcNow;
			await _stepRepository.UpdateAsync(step, cancellationToken);
			await OnProtectedConfigurationChangedAsync(step.WorkflowId, step.UpdatedByUserId ?? step.CreatedByUserId, cancellationToken);
			// A step moved to another workflow changes the workflow it left, too.
			if (!string.Equals(existing.WorkflowId, step.WorkflowId, StringComparison.OrdinalIgnoreCase))
				await OnProtectedConfigurationChangedAsync(existing.WorkflowId, step.UpdatedByUserId ?? step.CreatedByUserId, cancellationToken);
			return step;
		}

		public async Task<bool> DeleteWorkflowStepAsync(string stepId, CancellationToken cancellationToken = default)
		{
			var step = await _stepRepository.GetByIdAsync(stepId);
			if (step == null) return false;
			await _stepRepository.DeleteAsync(step, cancellationToken);
			await OnProtectedConfigurationChangedAsync(step.WorkflowId, null, cancellationToken);
			return true;
		}

		public async Task<List<WorkflowStep>> GetStepsByWorkflowIdAsync(string workflowId, CancellationToken cancellationToken = default)
		{
			var results = await _stepRepository.GetAllByWorkflowIdAsync(workflowId);
			return results?.OrderBy(s => s.StepOrder).ToList() ?? new List<WorkflowStep>();
		}

		// ── Credential CRUD ───────────────────────────────────────────────────────────

		public async Task<WorkflowCredential> GetCredentialByIdAsync(string credentialId, CancellationToken cancellationToken = default)
			=> await _credentialRepository.GetByIdAsync(credentialId);

		public async Task<List<WorkflowCredential>> GetCredentialsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var results = await _credentialRepository.GetAllByDepartmentIdAsync(departmentId);
			return results?.ToList() ?? new List<WorkflowCredential>();
		}

		public Task<WorkflowCredential> SaveCredentialAsync(WorkflowCredential credential, string departmentCode, CancellationToken cancellationToken = default) =>
			SaveCredentialInternalAsync(credential, departmentCode, rotateSigningKey: false, cancellationToken);

		public async Task<WorkflowCredential> RotateCredentialSigningKeyAsync(string credentialId, int departmentId, string departmentCode, string userId,
			CancellationToken cancellationToken = default)
		{
			var credential = await _credentialRepository.GetByIdAsync(credentialId);
			if (credential == null || credential.DepartmentId != departmentId ||
				credential.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials)
				return null;

			credential.EncryptedData = _encryptionService.DecryptForDepartment(credential.EncryptedData, departmentId, departmentCode);
			if (WorkflowJwtKeys.NormalizeAuthMethod(ReadJsonString(credential.EncryptedData, "authMethod")) != WorkflowJwtKeys.PrivateKeyJwt)
				return null;

			credential.UpdatedByUserId = userId;
			return await SaveCredentialInternalAsync(credential, departmentCode, rotateSigningKey: true, cancellationToken);
		}

		private async Task<WorkflowCredential> SaveCredentialInternalAsync(WorkflowCredential credential, string departmentCode, bool rotateSigningKey,
			CancellationToken cancellationToken)
		{
			// OAuth2 private_key_jwt: the signing keys are generated and kept here, inside the encrypted data. The editor never
			// sends or sees them; only the public halves go to PublicJwks.
			var keys = await PrepareSigningKeysAsync(credential, departmentCode, rotateSigningKey);
			credential.EncryptedData = keys.Json;

			// Protected Workflows pin a credential by id: capture what the stored row was BEFORE it is overwritten, so a
			// type, OAuth2 token-host or client-authentication change suspends pinned releases and a secret-only rotation
			// is recorded.
			var change = await CaptureCredentialChangeAsync(credential, departmentCode);
			if (change != null)
				change.RotatedKeyId = keys.RotatedKeyId;

			credential.EncryptedData = _encryptionService.EncryptForDepartment(
				credential.EncryptedData, credential.DepartmentId, departmentCode);

			if (string.IsNullOrEmpty(credential.WorkflowCredentialId))
			{
				credential.WorkflowCredentialId = Guid.NewGuid().ToString();
				credential.CreatedOn = DateTime.UtcNow;
				return await _credentialRepository.InsertAsync(credential, cancellationToken);
			}
			else
			{
				credential.UpdatedOn = DateTime.UtcNow;
				await _credentialRepository.UpdateAsync(credential, cancellationToken);
				await OnProtectedCredentialSavedAsync(credential, change, cancellationToken);
				return credential;
			}
		}

		/// <summary>
		/// Normalizes an OAuth2 credential's key material. client_secret: no keys, no JWKS. private_key_jwt: keys carried over
		/// from the stored credential (never from the caller), a first key generated when there is none, a new key when
		/// rotating or when the algorithm changes (the previous one retired but published for the overlap window), and
		/// expired keys dropped. Returns the JSON to encrypt and the kid of a key that REPLACED another one.
		/// </summary>
		private async Task<(string Json, string RotatedKeyId)> PrepareSigningKeysAsync(WorkflowCredential credential, string departmentCode, bool rotate)
		{
			if (credential.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials)
			{
				credential.PublicJwks = null;
				return (credential.EncryptedData, null);
			}

			JObject incoming;
			try { incoming = JObject.Parse(credential.EncryptedData ?? "{}"); }
			catch (JsonException) { credential.PublicJwks = null; return (credential.EncryptedData, null); }

			var method = WorkflowJwtKeys.NormalizeAuthMethod((string)incoming.GetValue("authMethod", StringComparison.OrdinalIgnoreCase));
			incoming.Remove("signingKeys");
			if (method != WorkflowJwtKeys.PrivateKeyJwt)
			{
				credential.PublicJwks = null;
				return (incoming.ToString(Formatting.None), null);
			}

			var keys = new List<WorkflowSigningKey>();
			if (!string.IsNullOrEmpty(credential.WorkflowCredentialId))
			{
				var stored = await _credentialRepository.GetByIdAsync(credential.WorkflowCredentialId);
				if (stored != null && stored.DepartmentId == credential.DepartmentId && stored.CredentialType == credential.CredentialType)
				{
					try
					{
						var previous = JObject.Parse(_encryptionService.DecryptForDepartment(stored.EncryptedData, stored.DepartmentId, departmentCode));
						keys = previous.GetValue("signingKeys", StringComparison.OrdinalIgnoreCase)?.ToObject<List<WorkflowSigningKey>>() ?? keys;
					}
					catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is System.Security.Cryptography.CryptographicException)
					{
						Logging.LogError($"Workflow credential {credential.WorkflowCredentialId}: stored signing keys unreadable ({ex.GetType().FullName}); generating a new key.");
					}
				}
			}

			var now = DateTime.UtcNow;
			var alg = WorkflowJwtKeys.NormalizeAlgorithm((string)incoming.GetValue("signingAlg", StringComparison.OrdinalIgnoreCase));
			var current = WorkflowJwtKeys.Current(keys);
			string rotatedKeyId = null;
			if (current == null || rotate || !string.Equals(current.Alg, alg, StringComparison.Ordinal))
			{
				foreach (var key in keys.Where(k => !k.RetiredOn.HasValue))
					key.RetiredOn = now;
				var (signing, _) = WorkflowJwtKeys.Generate(alg, now);
				keys.Add(signing);
				if (current != null)
					rotatedKeyId = signing.Kid;
			}

			keys = keys.Where(k => WorkflowJwtKeys.IsPublished(k.RetiredOn, now, DataProtectionConfig.WorkflowJwksOverlapDays)).ToList();
			incoming.Remove("clientSecret");
			incoming["authMethod"] = WorkflowJwtKeys.PrivateKeyJwt;
			incoming["signingAlg"] = alg;
			incoming["signingKeys"] = JArray.FromObject(keys);
			credential.PublicJwks = WorkflowJwtKeys.WritePublicKeys(keys.Select(WorkflowJwtKeys.PublicFor));
			return (incoming.ToString(Formatting.None), rotatedKeyId);
		}

		private static string ReadJsonString(string json, string name)
		{
			try { return (string)JObject.Parse(json ?? "{}").GetValue(name, StringComparison.OrdinalIgnoreCase); }
			catch (Exception) { return null; }
		}

		public async Task<bool> DeleteCredentialAsync(string credentialId, CancellationToken cancellationToken = default)
		{
			var cred = await _credentialRepository.GetByIdAsync(credentialId);
			if (cred == null) return false;
			await _credentialRepository.DeleteAsync(cred, cancellationToken);
			await OnProtectedCredentialDeletedAsync(cred, cancellationToken);
			return true;
		}

		// ── Execution ─────────────────────────────────────────────────────────────────

		/// <summary>The export template a step's ActionConfig names (designer key <c>recordsExportTemplateId</c>), or null.</summary>
		public static string ReadExportTemplateId(string actionConfigJson)
		{
			if (string.IsNullOrWhiteSpace(actionConfigJson))
				return null;
			try
			{
				var config = JObject.Parse(actionConfigJson);
				var token = config.GetValue("recordsExportTemplateId", StringComparison.OrdinalIgnoreCase);
				var value = token?.Type == JTokenType.String ? (string)token : null;
				return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			}
			catch (JsonException)
			{
				return null;
			}
		}

		/// <summary>record.id / record.kind / export.run_id from the dispatched Records payload, for the export render.</summary>
		public static (string recordId, RmsRecordKind? recordKind, string scheduledRunId) ReadExportSubject(string eventPayloadJson)
		{
			try
			{
				var evt = string.IsNullOrWhiteSpace(eventPayloadJson) ? null : JsonConvert.DeserializeObject<RecordsWorkflowEvent>(eventPayloadJson);
				var payload = evt?.Payload;
				if (payload == null)
					return (null, null, null);

				var record = payload["record"] as JObject;
				var recordId = record?["id"]?.Type == JTokenType.String ? (string)record["id"] : null;
				RmsRecordKind? kind = null;
				var kindName = record?["kind"]?.Type == JTokenType.String ? (string)record["kind"] : null;
				if (Enum.TryParse<RmsRecordKind>(kindName, true, out var parsed))
					kind = parsed;
				var export = payload["export"] as JObject;
				var runId = export?["run_id"]?.Type == JTokenType.String ? (string)export["run_id"] : null;
				return (recordId, kind, runId);
			}
			catch (JsonException)
			{
				return (null, null, null);
			}
		}

		public async Task<WorkflowRun> ExecuteWorkflowAsync(
			string workflowId,
			string eventPayloadJson,
			int departmentId,
			string departmentCode,
			int attemptNumber = 1,
			string existingRunId = null,
			CancellationToken cancellationToken = default)
		{
			var workflow = await _workflowRepository.GetByIdAsync(workflowId);
			if (workflow == null) return null;
			if (workflow.DepartmentId != departmentId) throw new InvalidOperationException("Workflow tenant mismatch.");
			var checklist = ChecklistWorkflowPayload.IsChecklist(workflow.TriggerEventType);
			if (checklist)
			{
				if (string.IsNullOrEmpty(existingRunId)) throw new InvalidOperationException("Checklist workflows require a durable event run.");
				eventPayloadJson = await ChecklistWorkflowPayload.ProjectAsync(departmentId, Resgrid.Model.Inventories.InventoryWorkflowPayload.Parse(eventPayloadJson), _protectedProjection?.Value, wrapped: true);
				var persistedInput = new WorkflowRun { InputPayload = eventPayloadJson };
				await History.ProtectAsync(departmentId, existingRunId, persistedInput, ReadinessHistoryFields.Runs, cancellationToken);
				if (!await _runRepository.TryStartChecklistRunAsync(existingRunId, workflowId, departmentId, attemptNumber, persistedInput.InputPayload))
				{
					var duplicate = await _runRepository.GetByIdAsync(existingRunId);
					return duplicate?.DepartmentId == departmentId && duplicate.WorkflowId == workflowId ? duplicate : null;
				}
			}

			// Resolve whether this is a free-plan department once per execution
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);
			var isFreePlan = plan?.IsFree ?? false;

			// Create or update the WorkflowRun record
			WorkflowRun run;
			if (!string.IsNullOrEmpty(existingRunId))
			{
				run = await _runRepository.GetByIdAsync(existingRunId);
				if (run == null)
				{
					if (checklist) throw new InvalidOperationException("The claimed checklist workflow run no longer exists.");
					Logging.LogError($"WorkflowService.ExecuteWorkflowAsync: WorkflowRun '{existingRunId}' not found for workflowId '{workflowId}'. Recreating run record.");
					run = new WorkflowRun
					{
						WorkflowRunId  = existingRunId,
						WorkflowId     = workflowId,
						DepartmentId   = departmentId,
						Status         = (int)WorkflowRunStatus.Running,
						TriggerEventType = workflow.TriggerEventType,
						InputPayload   = eventPayloadJson,
						StartedOn      = DateTime.UtcNow,
						QueuedOn       = DateTime.UtcNow,
						AttemptNumber  = attemptNumber
					};
					run = await _runRepository.InsertAsync(run, cancellationToken);
				}
				else
				{
					if (run.DepartmentId != departmentId || run.WorkflowId != workflowId) throw new InvalidOperationException("Workflow run tenant mismatch.");
					if (checklist) run.InputPayload = eventPayloadJson;
					run.Status        = (int)WorkflowRunStatus.Running;
					run.AttemptNumber = attemptNumber;
					await UpdateRunAsync(run, cancellationToken);
				}
			}
			else
			{
				run = new WorkflowRun
				{
					WorkflowRunId  = Guid.NewGuid().ToString(),
					WorkflowId     = workflowId,
					DepartmentId   = departmentId,
					Status         = (int)WorkflowRunStatus.Running,
					TriggerEventType = workflow.TriggerEventType,
					InputPayload   = eventPayloadJson,
					StartedOn      = DateTime.UtcNow,
					QueuedOn       = DateTime.UtcNow,
					AttemptNumber  = attemptNumber
				};
				run = await _runRepository.InsertAsync(run, cancellationToken);
			}

			// ── Protected Workflows run gate ─────────────────────────────────────
			// A workflow whose release is not Active is skipped, never run unprotected (its destination expects
			// plaintext; REDACTED would overwrite the real record). An Active release sends every step through the
			// protected path: fresh preconditions, an allow-listed decrypt, the pinned host, value-free logs.
			ProtectedRunGate protectedGate;
			try
			{
				protectedGate = _protectedRuntime == null ? ProtectedRunGate.NotProtected : await _protectedRuntime.GetRunGateAsync(workflow, cancellationToken);
			}
			catch (Exception gateEx) when (!(gateEx is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				// Unknown protection state: fail closed. Nothing runs; the run fails (and retries) without sending.
				Logging.LogError($"Protected workflow gate unavailable for run {run.WorkflowRunId}: {gateEx.GetType().FullName}.");
				run.Status       = (int)WorkflowRunStatus.Failed;
				run.ErrorMessage = "protected_gate_unavailable";
				run.CompletedOn  = DateTime.UtcNow;
				await UpdateRunAsync(run, cancellationToken);
				return run;
			}

			if (protectedGate.SkipReason != null)
			{
				run.Status      = (int)WorkflowRunStatus.Skipped;
				run.SkipReason  = protectedGate.SkipReason;
				run.CompletedOn = DateTime.UtcNow;
				await UpdateRunAsync(run, cancellationToken);
				return run;
			}
			// ── End protected run gate ───────────────────────────────────────────

			// Build template context once for all steps
			var triggerEventType = (WorkflowTriggerEventType)workflow.TriggerEventType;
			object scriptObject = null;
			try
			{
				scriptObject = await _contextBuilder.BuildContextAsync(departmentId, triggerEventType, eventPayloadJson, cancellationToken);
			}
			catch (Exception ex)
			{
				if (checklist || protectedGate.IsProtected) Logging.LogError($"Workflow context failed for run {run.WorkflowRunId}: {ex.GetType().FullName}."); else Logging.LogException(ex);
				run.Status       = (int)WorkflowRunStatus.Failed;
				run.ErrorMessage = protectedGate.IsProtected
					? ProtectedWorkflowLogText.Error("context_failed", ex)
					: $"Failed to build template context: {ex.Message}";
				run.CompletedOn  = DateTime.UtcNow;
				await UpdateRunAsync(run, cancellationToken);
				return run;
			}

			if (scriptObject == null)
			{
				Logging.LogError($"WorkflowService.ExecuteWorkflowAsync: BuildContextAsync returned null for workflowId '{workflowId}', departmentId {departmentId}, eventType {triggerEventType}.");
				run.Status       = (int)WorkflowRunStatus.Failed;
				run.ErrorMessage = "Template context builder returned a null context object.";
				run.CompletedOn  = DateTime.UtcNow;
				await UpdateRunAsync(run, cancellationToken);
				return run;
			}

			var steps = await GetStepsByWorkflowIdAsync(workflowId, cancellationToken);
			var anyFailure = false;
			var anyRetryable = false;
			var utcToday = DateTime.UtcNow.Date;
			string lastProtectedError = null;

			foreach (var step in steps.Where(s => s.IsEnabled))
			{
				// run.* for this step: run.idempotency_key is the same on every retry of this delivery, so a destination that
				// honors it (an Idempotency-Key header, FHIR conditional create, HL7 MSH-10) never records it twice.
				var idempotencyKey = WorkflowIdempotency.Key(workflowId, run.EventId, run.WorkflowRunId, step.WorkflowStepId);
				((Scriban.Runtime.ScriptObject)scriptObject)["run"] = new Scriban.Runtime.ScriptObject
				{
					["id"] = run.WorkflowRunId,
					["attempt"] = attemptNumber,
					["idempotency_key"] = idempotencyKey
				};

				if (protectedGate.IsProtected)
				{
					var protectedStep = await ExecuteProtectedStepAsync(workflow, run, step, (Scriban.Runtime.ScriptObject)scriptObject,
						eventPayloadJson, departmentId, departmentCode, isFreePlan, idempotencyKey, cancellationToken);
					await InsertLogAsync(departmentId, checklist, protectedStep.Log, cancellationToken);
					if (protectedStep.Failed)
					{
						anyFailure = true;
						anyRetryable |= protectedStep.Retryable;
						lastProtectedError = protectedStep.ErrorCode ?? lastProtectedError;
					}
					continue;
				}

				var logEntry = new WorkflowRunLog
				{
					WorkflowRunLogId = Guid.NewGuid().ToString(),
					WorkflowRunId    = run.WorkflowRunId,
					WorkflowStepId   = step.WorkflowStepId,
					Status           = (int)WorkflowRunStatus.Running,
					StartedOn        = DateTime.UtcNow
				};

				var sw = Stopwatch.StartNew();
				try
				{
					// ── Condition expression evaluation ──────────────────────────────
					if (!string.IsNullOrWhiteSpace(step.ConditionExpression))
					{
						var conditionContext = new Scriban.TemplateContext
						{
							LoopLimit       = WorkflowConfig.ScribanLoopLimit,
							StrictVariables = false,
							// protected.* never exists outside an approved release: it must read as empty, not throw.
							EnableRelaxedTargetAccess = ProtectedWorkflowValidator.ReferencesProtectedNamespace(step.ConditionExpression)
						};
						conditionContext.PushGlobal((Scriban.Runtime.ScriptObject)scriptObject);

						var conditionTemplate = Template.Parse(step.ConditionExpression);
						if (conditionTemplate.HasErrors)
						{
							var parseErrors = string.Join("; ", conditionTemplate.Messages);
							sw.Stop();
							logEntry.Status        = (int)WorkflowRunStatus.Skipped;
							logEntry.ErrorMessage  = $"Step skipped: condition expression has parse errors — {parseErrors}";
							logEntry.RenderedOutput = step.ConditionExpression;
							logEntry.DurationMs    = sw.ElapsedMilliseconds;
							logEntry.CompletedOn   = DateTime.UtcNow;
							await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
							continue;
						}

						string conditionResult;
						try
						{
							conditionResult = (await conditionTemplate.RenderAsync(conditionContext))?.Trim() ?? string.Empty;
						}
						catch (Exception condEx)
						{
							sw.Stop();
							logEntry.Status        = (int)WorkflowRunStatus.Skipped;
							logEntry.ErrorMessage  = $"Step skipped: condition expression render error — {condEx.Message}";
							logEntry.RenderedOutput = step.ConditionExpression;
							logEntry.DurationMs    = sw.ElapsedMilliseconds;
							logEntry.CompletedOn   = DateTime.UtcNow;
							await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
							continue;
						}

						bool conditionIsFalsy = string.IsNullOrWhiteSpace(conditionResult)
							|| string.Equals(conditionResult, "false", StringComparison.OrdinalIgnoreCase);

						if (conditionIsFalsy)
						{
							sw.Stop();
							logEntry.Status        = (int)WorkflowRunStatus.Skipped;
							logEntry.ErrorMessage  = $"Step skipped: condition evaluated to '{conditionResult}'.";
							logEntry.RenderedOutput = conditionResult;
							logEntry.DurationMs    = sw.ElapsedMilliseconds;
							logEntry.CompletedOn   = DateTime.UtcNow;
							await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
							continue;
						}
					}
					// ── End condition expression evaluation ──────────────────────────

					// ── Daily send limit check (Email and SMS only) ──────────────────
					var actionType = (WorkflowActionType)step.ActionType;
					if (actionType == WorkflowActionType.SendEmail || actionType == WorkflowActionType.SendSms)
					{
						int dailyLimit = actionType == WorkflowActionType.SendEmail
							? (isFreePlan ? WorkflowConfig.FreeMaxDailyEmailSendsPerDepartment : WorkflowConfig.MaxDailyEmailSendsPerDepartment)
							: (isFreePlan ? WorkflowConfig.FreeMaxDailySmsPerDepartment : WorkflowConfig.MaxDailySmsPerDepartment);

						var dailyCount = await _dailyUsageRepository.GetDailySendCountAsync(departmentId, step.ActionType, utcToday);
						if (dailyCount >= dailyLimit)
						{
							sw.Stop();
							logEntry.Status       = (int)WorkflowRunStatus.Failed;
							logEntry.ErrorMessage = $"Daily {actionType} send limit of {dailyLimit} reached for this department. Step skipped.";
							logEntry.DurationMs   = sw.ElapsedMilliseconds;
							logEntry.CompletedOn  = DateTime.UtcNow;
							await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
							anyFailure = true;
							continue;
						}
					}
					// ── End daily send limit check ───────────────────────────────────

					// ── Build sandboxed Scriban context ──────────────────────────────
					var scribanContext = new Scriban.TemplateContext
					{
						LoopLimit       = WorkflowConfig.ScribanLoopLimit,
						StrictVariables = false,
						// A protected.* reference in a workflow without an Active release renders as an empty string.
						EnableRelaxedTargetAccess = ProtectedWorkflowValidator.ReferencesProtectedNamespace(step.OutputTemplate) ||
							ProtectedWorkflowValidator.ReferencesProtectedNamespace(step.ActionConfig)
					};
					scribanContext.PushGlobal((Scriban.Runtime.ScriptObject)scriptObject);
					// ── End sandboxed Scriban context ────────────────────────────────

					// ── Render OutputTemplate ────────────────────────────────────────
					string renderedContent;
					try
					{
						var template = Template.Parse(step.OutputTemplate ?? string.Empty);
						if (template.HasErrors)
							throw new InvalidOperationException($"Template parse errors: {string.Join("; ", template.Messages)}");

						renderedContent = await template.RenderAsync(scribanContext);
					}
					catch (Exception tex)
					{
						logEntry.Status       = (int)WorkflowRunStatus.Failed;
						logEntry.ErrorMessage = $"Template render error: {tex.Message}";
						sw.Stop();
						logEntry.DurationMs  = sw.ElapsedMilliseconds;
						logEntry.CompletedOn = DateTime.UtcNow;
						await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
						anyFailure = true;
						continue;
					}

					// Enforce rendered content size cap
					if (renderedContent?.Length > WorkflowConfig.MaxRenderedContentLength)
						renderedContent = renderedContent.Substring(0, WorkflowConfig.MaxRenderedContentLength);

					logEntry.RenderedOutput = renderedContent?.Length > 4000
						? renderedContent.Substring(0, 4000)
						: renderedContent;
					// ── End render OutputTemplate ────────────────────────────────────

					// ── Render ActionConfig through Scriban (step 9) ─────────────────
					string renderedActionConfig = step.ActionConfig;
					if (!string.IsNullOrWhiteSpace(step.ActionConfig))
					{
						try
						{
							var configTemplate = Template.Parse(step.ActionConfig);
							if (!configTemplate.HasErrors)
							{
								// Use a fresh context push so the render doesn't mutate state
								var configRendered = await configTemplate.RenderAsync(scribanContext);
								if (configRendered?.Length > WorkflowConfig.MaxRenderedContentLength)
									configRendered = configRendered.Substring(0, WorkflowConfig.MaxRenderedContentLength);
								renderedActionConfig = configRendered;
							}
						}
						catch
						{
							// If ActionConfig render fails, fall back to raw config — don't fail the whole step
							renderedActionConfig = step.ActionConfig;
						}
					}
					// ── End ActionConfig render ──────────────────────────────────────

					// Decrypt credential if one is attached
					string decryptedCredJson = null;
					int? credentialType = null;
					if (!string.IsNullOrEmpty(step.WorkflowCredentialId))
					{
						var cred = await _credentialRepository.GetByIdAsync(step.WorkflowCredentialId);
						if (cred != null)
						{
							credentialType = cred.CredentialType;
							decryptedCredJson = _encryptionService.DecryptForDepartment(
								cred.EncryptedData, departmentId, departmentCode);
						}
					}

					// ── Records report export attachment (RMS plan section 5.6) ─────
					// A step that names an export template carries the rendered file: email actions attach it,
					// file actions upload it. The render happens here, inside the run, so the run log records what
					// was sent and an ADP-redacted export is visible as such.
					WorkflowAttachment attachment = null;
					var exportTemplateId = ReadExportTemplateId(renderedActionConfig);
					if (!string.IsNullOrWhiteSpace(exportTemplateId))
					{
						if (!WorkflowTriggerEventTypes.IsRecordsTrigger(triggerEventType))
						{
							sw.Stop();
							logEntry.Status       = (int)WorkflowRunStatus.Failed;
							logEntry.ErrorMessage = "A report export can only be attached to a Records trigger.";
							logEntry.DurationMs   = sw.ElapsedMilliseconds;
							logEntry.CompletedOn  = DateTime.UtcNow;
							await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
							anyFailure = true;
							continue;
						}

						try
						{
							var (recordId, recordKind, scheduledRunId) = ReadExportSubject(eventPayloadJson);
							var exportRun = await _recordsExportService.ResolveForWorkflowAsync(departmentId, exportTemplateId, recordId, recordKind, scheduledRunId, run.WorkflowRunId, cancellationToken);
							attachment = new WorkflowAttachment
							{
								FileName    = exportRun.FileName,
								ContentType = exportRun.ContentType,
								Data        = exportRun.Data,
								Redacted    = exportRun.Redacted,
								ExportRunId = exportRun.RmsExportRunId
							};
						}
						catch (Exception exportEx) when (!(exportEx is OperationCanceledException && cancellationToken.IsCancellationRequested))
						{
							sw.Stop();
							logEntry.Status       = (int)WorkflowRunStatus.Failed;
							logEntry.ErrorMessage = $"Report export failed: {exportEx.Message}";
							logEntry.DurationMs   = sw.ElapsedMilliseconds;
							logEntry.CompletedOn  = DateTime.UtcNow;
							await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
							anyFailure = true;
							Logging.LogException(exportEx);
							continue;
						}
					}
					// ── End export attachment ────────────────────────────────────────

					var context = new WorkflowActionContext
					{
						RenderedContent        = renderedContent,
						DecryptedCredentialJson = decryptedCredJson,
						ActionConfigJson       = renderedActionConfig,
						WorkflowId             = workflowId,
						WorkflowStepId         = step.WorkflowStepId,
						WorkflowRunId          = run.WorkflowRunId,
						DepartmentId           = departmentId,
						ActionType             = step.ActionType,
						IsFreePlanDepartment   = isFreePlan,
						Attachment             = attachment,
						CredentialType         = credentialType,
						IdempotencyKey         = idempotencyKey
					};

					var executor = _executorFactory.GetExecutor((WorkflowActionType)step.ActionType);
					var result   = await executor.ExecuteAsync(context, cancellationToken);

					sw.Stop();
					logEntry.DurationMs  = sw.ElapsedMilliseconds;
					logEntry.CompletedOn = DateTime.UtcNow;

					if (result.Success)
					{
						logEntry.Status       = (int)WorkflowRunStatus.Completed;
						var resultMessage = attachment == null
							? result.ResultMessage
							: $"{result.ResultMessage} [export {attachment.ExportRunId}: {attachment.FileName}, {attachment.Data?.Length ?? 0} bytes{(attachment.Redacted ? ", protected fields withheld" : string.Empty)}]";
						logEntry.ActionResult = resultMessage?.Length > 4000
							? resultMessage.Substring(0, 4000)
							: resultMessage;

						// Record daily usage for outbound messaging actions
						if (actionType == WorkflowActionType.SendEmail || actionType == WorkflowActionType.SendSms)
							await _dailyUsageRepository.IncrementAsync(departmentId, step.ActionType, utcToday, cancellationToken);
					}
					else
					{
						logEntry.Status       = (int)WorkflowRunStatus.Failed;
						logEntry.ActionResult = result.ResultMessage;
						logEntry.ErrorMessage = result.ErrorDetail?.Length > 4000
							? result.ErrorDetail.Substring(0, 4000)
							: result.ErrorDetail;
						anyFailure = true;
					}
				}
				catch (Exception ex)
				{
					sw.Stop();
					logEntry.Status       = (int)WorkflowRunStatus.Failed;
					logEntry.ErrorMessage = ex.Message.Length > 4000 ? ex.Message.Substring(0, 4000) : ex.Message;
					logEntry.DurationMs   = sw.ElapsedMilliseconds;
					logEntry.CompletedOn  = DateTime.UtcNow;
					anyFailure = true;
					if (checklist) Logging.LogError($"Checklist workflow failed for run {run.WorkflowRunId}: {ex.GetType().FullName}."); else Logging.LogException(ex);
				}

				await InsertLogAsync(departmentId, checklist, logEntry, cancellationToken);
			}

			// Determine retry or final status
			if (anyFailure)
			{
				var maxRetries = workflow.MaxRetryCount > 0
					? workflow.MaxRetryCount
					: WorkflowConfig.DefaultMaxRetryCount;

				// A protected run retries only for failures another attempt could fix (transport, 5xx, 429); a rejected
				// acknowledgement, a 4xx or a refused send stops at once and alerts.
				if (attemptNumber < maxRetries && (!protectedGate.IsProtected || anyRetryable))
					run.Status = (int)WorkflowRunStatus.Retrying;
				else
				{
					run.Status       = (int)WorkflowRunStatus.Failed;
					run.ErrorMessage = protectedGate.IsProtected && !anyRetryable && attemptNumber < maxRetries
						? $"Not retried: {lastProtectedError ?? ProtectedWorkflowErrorCodes.StepError}"
						: "Maximum retry attempts exceeded.";
					if (protectedGate.IsProtected && _protectedWorkflows != null)
						await _protectedWorkflows.NotifyFinalFailureAsync(departmentId, workflow, run.WorkflowRunId,
							lastProtectedError ?? ProtectedWorkflowErrorCodes.StepError, cancellationToken);
				}
			}
			else
			{
				run.Status = (int)WorkflowRunStatus.Completed;
			}

			run.CompletedOn = DateTime.UtcNow;
			await UpdateRunAsync(run, cancellationToken);
			return run;
		}

		public async Task<bool> CancelWorkflowRunAsync(string workflowRunId, CancellationToken cancellationToken = default)
		{
			var run = await _runRepository.GetByIdAsync(workflowRunId);
			if (run == null) return false;

			var status = (WorkflowRunStatus)run.Status;
			if (status == WorkflowRunStatus.Completed || status == WorkflowRunStatus.Failed)
				return false;

			run.Status      = (int)WorkflowRunStatus.Cancelled;
			run.CompletedOn = DateTime.UtcNow;
			await UpdateRunAsync(run, cancellationToken);
			return true;
		}

		// ── Run Queries ───────────────────────────────────────────────────────────────

		public async Task<WorkflowRun> GetWorkflowRunByIdAsync(string workflowRunId, CancellationToken cancellationToken = default)
			=> await DisplayRunAsync(await _runRepository.GetByIdAsync(workflowRunId));

		public async Task<List<WorkflowRun>> GetRunsByDepartmentIdAsync(int departmentId, int page, int pageSize, CancellationToken cancellationToken = default)
		{
			var results = await _runRepository.GetByDepartmentIdPagedAsync(departmentId, page, pageSize);
			return await DisplayRunsAsync(results);
		}

		public async Task<List<WorkflowRun>> GetRunsByWorkflowIdAsync(string workflowId, int page, int pageSize, CancellationToken cancellationToken = default)
		{
			var results = await _runRepository.GetRunsByWorkflowIdAsync(workflowId, page, pageSize);
			return await DisplayRunsAsync(results);
		}

		public async Task<List<WorkflowRun>> GetPendingAndRunningRunsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var results = await _runRepository.GetPendingAndRunningByDepartmentIdAsync(departmentId);
			return await DisplayRunsAsync(results);
		}

		public async Task<List<WorkflowRunLog>> GetLogsForRunAsync(string workflowRunId, CancellationToken cancellationToken = default)
		{
			var results = await _runLogRepository.GetByWorkflowRunIdAsync(workflowRunId);
			var run = await _runRepository.GetByIdAsync(workflowRunId);
			if (run == null) return new List<WorkflowRunLog>();
			var logs = results?.OrderBy(l => l.StartedOn).ToList() ?? new List<WorkflowRunLog>();
			if (ChecklistWorkflowPayload.IsChecklist(run.TriggerEventType))
				return (await Task.WhenAll(logs.Select(log => History.ForDisplayAsync(run.DepartmentId, log, ReadinessHistoryFields.Logs)))).ToList();
			return logs;
		}

		public async Task<WorkflowHealthSummary> GetWorkflowHealthAsync(string workflowId, CancellationToken cancellationToken = default)
		{
			var workflow = await _workflowRepository.GetByIdAsync(workflowId);
			if (workflow == null) return null;

			var allRuns = await DisplayRunsAsync(await _runRepository.GetRunsByWorkflowIdAsync(workflowId, 1, 10000));

			var now     = DateTime.UtcNow;
			var runs24h = allRuns.Where(r => r.StartedOn >= now.AddHours(-24)).ToList();
			var runs7d  = allRuns.Where(r => r.StartedOn >= now.AddDays(-7)).ToList();
			var runs30d = allRuns.Where(r => r.StartedOn >= now.AddDays(-30)).ToList();

			var completedRuns30d = runs30d.Where(r => r.CompletedOn.HasValue && r.Status == (int)WorkflowRunStatus.Completed).ToList();
			double? avgDurationMs = completedRuns30d.Any()
				? completedRuns30d
					.Where(r => r.CompletedOn.HasValue)
					.Select(r => (r.CompletedOn!.Value - r.StartedOn).TotalMilliseconds)
					.DefaultIfEmpty(0)
					.Average()
				: (double?)null;

			var lastRun = allRuns.OrderByDescending(r => r.StartedOn).FirstOrDefault();

			return new WorkflowHealthSummary
			{
				WorkflowId         = workflowId,
				WorkflowName       = workflow.Name,
				TotalRuns24h       = runs24h.Count,
				SuccessfulRuns24h  = runs24h.Count(r => r.Status == (int)WorkflowRunStatus.Completed),
				FailedRuns24h      = runs24h.Count(r => r.Status == (int)WorkflowRunStatus.Failed),
				SkippedRuns24h     = runs24h.Count(r => r.Status == (int)WorkflowRunStatus.Skipped),
				RetryingRuns24h    = runs24h.Count(r => r.Status == (int)WorkflowRunStatus.Retrying),
				TotalRuns7d        = runs7d.Count,
				SuccessfulRuns7d   = runs7d.Count(r => r.Status == (int)WorkflowRunStatus.Completed),
				FailedRuns7d       = runs7d.Count(r => r.Status == (int)WorkflowRunStatus.Failed),
				SkippedRuns7d     = runs7d.Count(r => r.Status == (int)WorkflowRunStatus.Skipped),
				TotalRuns30d       = runs30d.Count,
				SuccessfulRuns30d  = runs30d.Count(r => r.Status == (int)WorkflowRunStatus.Completed),
				FailedRuns30d      = runs30d.Count(r => r.Status == (int)WorkflowRunStatus.Failed),
				SkippedRuns30d     = runs30d.Count(r => r.Status == (int)WorkflowRunStatus.Skipped),
				AverageDurationMs30d = avgDurationMs,
				LastRunOn          = lastRun?.StartedOn,
				LastRunStatus      = lastRun != null ? (WorkflowRunStatus?)lastRun.Status : null,
				LastErrorMessage   = lastRun?.ErrorMessage
			};
		}

		private async Task UpdateRunAsync(WorkflowRun run, CancellationToken ct)
		{
			if (ChecklistWorkflowPayload.IsChecklist(run.TriggerEventType))
				await History.ProtectAsync(run.DepartmentId, run.WorkflowRunId, run, ReadinessHistoryFields.Runs, ct);
			await _runRepository.UpdateAsync(run, ct);
		}

		private async Task InsertLogAsync(int departmentId, bool checklist, WorkflowRunLog log, CancellationToken ct)
		{
			if (checklist) await History.ProtectAsync(departmentId, log.WorkflowRunLogId, log, ReadinessHistoryFields.Logs, ct);
			await _runLogRepository.InsertAsync(log, ct);
		}

		private async Task<WorkflowRun> DisplayRunAsync(WorkflowRun run)
		{
			if (run == null || !ChecklistWorkflowPayload.IsChecklist(run.TriggerEventType)) return run;
			var copy = await History.ForDisplayAsync(run.DepartmentId, run, ReadinessHistoryFields.Runs);
			if (copy.Logs != null)
			{
				var logs = new List<WorkflowRunLog>();
				foreach (var log in copy.Logs)
					logs.Add(await History.ForDisplayAsync(run.DepartmentId, log, ReadinessHistoryFields.Logs));
				copy.Logs = logs;
			}
			return copy;
		}

		private async Task<List<WorkflowRun>> DisplayRunsAsync(IEnumerable<WorkflowRun> runs)
		{
			var display = new List<WorkflowRun>();
			foreach (var run in runs ?? Enumerable.Empty<WorkflowRun>())
				display.Add(await DisplayRunAsync(run));
			return display;
		}

		public async Task<bool> ClearPendingRunsAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var pending = await _runRepository.GetPendingAndRunningByDepartmentIdAsync(departmentId);
			if (pending == null) return true;

			foreach (var run in pending.Where(r => r.Status == (int)WorkflowRunStatus.Pending))
			{
				run.Status      = (int)WorkflowRunStatus.Cancelled;
				run.CompletedOn = DateTime.UtcNow;
				await UpdateRunAsync(run, cancellationToken);
			}
			return true;
		}
	}
}
