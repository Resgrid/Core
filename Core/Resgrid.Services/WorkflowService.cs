using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Scriban;

namespace Resgrid.Services
{
	public class WorkflowService : IWorkflowService
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
			ISubscriptionsService subscriptionsService)
		{
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
				return workflow;
			}
		}

		public async Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
		{
			var workflow = await _workflowRepository.GetByIdAsync(workflowId);
			if (workflow == null) return false;
			await _workflowRepository.DeleteAsync(workflow, cancellationToken);
			return true;
		}

		public async Task<List<Workflow>> GetActiveWorkflowsByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType, CancellationToken cancellationToken = default)
		{
			var results = await _workflowRepository.GetAllActiveByDepartmentAndEventTypeAsync(departmentId, triggerEventType);
			return results?.ToList() ?? new List<Workflow>();
		}

		public async Task<bool> WorkflowExistsForEventTypeAsync(int departmentId, int triggerEventType, CancellationToken cancellationToken = default)
		{
			var existing = await _workflowRepository.GetByDepartmentAndEventTypeAsync(departmentId, triggerEventType);
			return existing != null;
		}

		public async Task<IReadOnlyCollection<int>> GetUsedEventTypesForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var workflows = await _workflowRepository.GetAllByDepartmentIdAsync(departmentId);
			return workflows?.Select(w => w.TriggerEventType).ToHashSet() ?? (IReadOnlyCollection<int>)Array.Empty<int>();
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
				return await _stepRepository.InsertAsync(step, cancellationToken);
			}
			step.UpdatedOn = DateTime.UtcNow;
			await _stepRepository.UpdateAsync(step, cancellationToken);
			return step;
		}

		public async Task<bool> DeleteWorkflowStepAsync(string stepId, CancellationToken cancellationToken = default)
		{
			var step = await _stepRepository.GetByIdAsync(stepId);
			if (step == null) return false;
			await _stepRepository.DeleteAsync(step, cancellationToken);
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

		public async Task<WorkflowCredential> SaveCredentialAsync(WorkflowCredential credential, string departmentCode, CancellationToken cancellationToken = default)
		{
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
				return credential;
			}
		}

		public async Task<bool> DeleteCredentialAsync(string credentialId, CancellationToken cancellationToken = default)
		{
			var cred = await _credentialRepository.GetByIdAsync(credentialId);
			if (cred == null) return false;
			await _credentialRepository.DeleteAsync(cred, cancellationToken);
			return true;
		}

		// ── Execution ─────────────────────────────────────────────────────────────────

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
					run.Status        = (int)WorkflowRunStatus.Running;
					run.AttemptNumber = attemptNumber;
					await _runRepository.UpdateAsync(run, cancellationToken);
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

			// Build template context once for all steps
			var triggerEventType = (WorkflowTriggerEventType)workflow.TriggerEventType;
			object scriptObject = null;
			try
			{
				scriptObject = await _contextBuilder.BuildContextAsync(departmentId, triggerEventType, eventPayloadJson, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				run.Status       = (int)WorkflowRunStatus.Failed;
				run.ErrorMessage = $"Failed to build template context: {ex.Message}";
				run.CompletedOn  = DateTime.UtcNow;
				await _runRepository.UpdateAsync(run, cancellationToken);
				return run;
			}

			if (scriptObject == null)
			{
				Logging.LogError($"WorkflowService.ExecuteWorkflowAsync: BuildContextAsync returned null for workflowId '{workflowId}', departmentId {departmentId}, eventType {triggerEventType}.");
				run.Status       = (int)WorkflowRunStatus.Failed;
				run.ErrorMessage = "Template context builder returned a null context object.";
				run.CompletedOn  = DateTime.UtcNow;
				await _runRepository.UpdateAsync(run, cancellationToken);
				return run;
			}

			var steps = await GetStepsByWorkflowIdAsync(workflowId, cancellationToken);
			var anyFailure = false;
			var utcToday = DateTime.UtcNow.Date;

			foreach (var step in steps.Where(s => s.IsEnabled))
			{
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
							StrictVariables = false
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
							await _runLogRepository.InsertAsync(logEntry, cancellationToken);
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
							await _runLogRepository.InsertAsync(logEntry, cancellationToken);
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
							await _runLogRepository.InsertAsync(logEntry, cancellationToken);
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
							await _runLogRepository.InsertAsync(logEntry, cancellationToken);
							anyFailure = true;
							continue;
						}
					}
					// ── End daily send limit check ───────────────────────────────────

					// ── Build sandboxed Scriban context ──────────────────────────────
					var scribanContext = new Scriban.TemplateContext
					{
						LoopLimit       = WorkflowConfig.ScribanLoopLimit,
						StrictVariables = false
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
						await _runLogRepository.InsertAsync(logEntry, cancellationToken);
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
					if (!string.IsNullOrEmpty(step.WorkflowCredentialId))
					{
						var cred = await _credentialRepository.GetByIdAsync(step.WorkflowCredentialId);
						if (cred != null)
							decryptedCredJson = _encryptionService.DecryptForDepartment(
								cred.EncryptedData, departmentId, departmentCode);
					}

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
						IsFreePlanDepartment   = isFreePlan
					};

					var executor = _executorFactory.GetExecutor((WorkflowActionType)step.ActionType);
					var result   = await executor.ExecuteAsync(context, cancellationToken);

					sw.Stop();
					logEntry.DurationMs  = sw.ElapsedMilliseconds;
					logEntry.CompletedOn = DateTime.UtcNow;

					if (result.Success)
					{
						logEntry.Status       = (int)WorkflowRunStatus.Completed;
						logEntry.ActionResult = result.ResultMessage?.Length > 4000
							? result.ResultMessage.Substring(0, 4000)
							: result.ResultMessage;

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
					Logging.LogException(ex);
				}

				await _runLogRepository.InsertAsync(logEntry, cancellationToken);
			}

			// Determine retry or final status
			if (anyFailure)
			{
				var maxRetries = workflow.MaxRetryCount > 0
					? workflow.MaxRetryCount
					: WorkflowConfig.DefaultMaxRetryCount;

				if (attemptNumber < maxRetries)
					run.Status = (int)WorkflowRunStatus.Retrying;
				else
				{
					run.Status       = (int)WorkflowRunStatus.Failed;
					run.ErrorMessage = "Maximum retry attempts exceeded.";
				}
			}
			else
			{
				run.Status = (int)WorkflowRunStatus.Completed;
			}

			run.CompletedOn = DateTime.UtcNow;
			await _runRepository.UpdateAsync(run, cancellationToken);
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
			await _runRepository.UpdateAsync(run, cancellationToken);
			return true;
		}

		// ── Run Queries ───────────────────────────────────────────────────────────────

		public async Task<WorkflowRun> GetWorkflowRunByIdAsync(string workflowRunId, CancellationToken cancellationToken = default)
			=> await _runRepository.GetByIdAsync(workflowRunId);

		public async Task<List<WorkflowRun>> GetRunsByDepartmentIdAsync(int departmentId, int page, int pageSize, CancellationToken cancellationToken = default)
		{
			var results = await _runRepository.GetByDepartmentIdPagedAsync(departmentId, page, pageSize);
			return results?.ToList() ?? new List<WorkflowRun>();
		}

		public async Task<List<WorkflowRun>> GetRunsByWorkflowIdAsync(string workflowId, int page, int pageSize, CancellationToken cancellationToken = default)
		{
			var results = await _runRepository.GetRunsByWorkflowIdAsync(workflowId, page, pageSize);
			return results?.ToList() ?? new List<WorkflowRun>();
		}

		public async Task<List<WorkflowRun>> GetPendingAndRunningRunsByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var results = await _runRepository.GetPendingAndRunningByDepartmentIdAsync(departmentId);
			return results?.ToList() ?? new List<WorkflowRun>();
		}

		public async Task<List<WorkflowRunLog>> GetLogsForRunAsync(string workflowRunId, CancellationToken cancellationToken = default)
		{
			var results = await _runLogRepository.GetByWorkflowRunIdAsync(workflowRunId);
			return results?.OrderBy(l => l.StartedOn).ToList() ?? new List<WorkflowRunLog>();
		}

		public async Task<WorkflowHealthSummary> GetWorkflowHealthAsync(string workflowId, CancellationToken cancellationToken = default)
		{
			var workflow = await _workflowRepository.GetByIdAsync(workflowId);
			if (workflow == null) return null;

			var allRuns = (await _runRepository.GetRunsByWorkflowIdAsync(workflowId, 1, 10000))?.ToList()
			              ?? new List<WorkflowRun>();

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
				RetryingRuns24h    = runs24h.Count(r => r.Status == (int)WorkflowRunStatus.Retrying),
				TotalRuns7d        = runs7d.Count,
				SuccessfulRuns7d   = runs7d.Count(r => r.Status == (int)WorkflowRunStatus.Completed),
				FailedRuns7d       = runs7d.Count(r => r.Status == (int)WorkflowRunStatus.Failed),
				TotalRuns30d       = runs30d.Count,
				SuccessfulRuns30d  = runs30d.Count(r => r.Status == (int)WorkflowRunStatus.Completed),
				FailedRuns30d      = runs30d.Count(r => r.Status == (int)WorkflowRunStatus.Failed),
				AverageDurationMs30d = avgDurationMs,
				LastRunOn          = lastRun?.StartedOn,
				LastRunStatus      = lastRun != null ? (WorkflowRunStatus?)lastRun.Status : null,
				LastErrorMessage   = lastRun?.ErrorMessage
			};
		}

		public async Task<bool> ClearPendingRunsAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var pending = await _runRepository.GetPendingAndRunningByDepartmentIdAsync(departmentId);
			if (pending == null) return true;

			foreach (var run in pending.Where(r => r.Status == (int)WorkflowRunStatus.Pending))
			{
				run.Status      = (int)WorkflowRunStatus.Cancelled;
				run.CompletedOn = DateTime.UtcNow;
				await _runRepository.UpdateAsync(run, cancellationToken);
			}
			return true;
		}
	}
}



