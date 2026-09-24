using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Services
{
	/// <summary>
	/// Protected Workflows (ADP push model) inside workflow execution. A step of a workflow with an Active release runs
	/// ONLY through <see cref="ExecuteProtectedStepAsync"/>:
	///   1. the condition is evaluated against the ordinary (REDACTED) context — never against plaintext;
	///   2. the action config (URL, headers) renders against the same ordinary context — plaintext never reaches a URL;
	///   3. every precondition is re-read fresh (ADP state, toggle, release Active and unexpired, fingerprint, action,
	///      credential, rendered https host);
	///   4. ONLY the allow-listed fields are decrypted, through the broker's workload lane with a fresh request id;
	///   5. the output renders with the released values under protected.* and is scrubbed for envelopes;
	///   6. the payload is normalized and validated for its declared content type (JSON, FHIR, XML, HL7 v2);
	///   7. the executor sends in protected mode (no redirects, TLS 1.2+, pinned host re-checked, status line only; the
	///      body is read, capped, only for a success rule or a capture);
	///   8. captured response values are written into the call's subject identifiers through the encrypt lane;
	///   9. one value-free disclosure is chained per attempt (after an "attempted" record written before sending).
	/// Plaintext exists only in memory during 4-6. Nothing here persists, queues or logs it: the run log gets a hash,
	/// a byte count and field ids; errors get a fixed code and an exception type name.
	/// </summary>
	public partial class WorkflowService
	{
		private sealed class ProtectedStepOutcome
		{
			public WorkflowRunLog Log { get; init; }
			public bool Failed { get; init; }
			public string ErrorCode { get; init; }

			/// <summary>Whether the workflow retry policy may repeat this attempt (see ProtectedWorkflowRetryPolicy).</summary>
			public bool Retryable { get; init; }
		}

		private sealed class CredentialChange
		{
			public bool TypeChanged { get; init; }
			public bool SecretChanged { get; init; }
			public string PreviousTokenHost { get; init; }
			public string CurrentTokenHost { get; init; }
			public string PreviousAuthMethod { get; init; }
			public string CurrentAuthMethod { get; init; }

			/// <summary>Set when this save replaced a private_key_jwt signing key (a rotation).</summary>
			public string RotatedKeyId { get; set; }
		}

		private async Task<ProtectedStepOutcome> ExecuteProtectedStepAsync(Workflow workflow, WorkflowRun run, WorkflowStep step,
			ScriptObject baseContext, string eventPayloadJson, int departmentId, string departmentCode, bool isFreePlan,
			string idempotencyKey, CancellationToken cancellationToken)
		{
			var log = new WorkflowRunLog
			{
				WorkflowRunLogId = Guid.NewGuid().ToString(),
				WorkflowRunId    = run.WorkflowRunId,
				WorkflowStepId   = step.WorkflowStepId,
				Status           = (int)WorkflowRunStatus.Running,
				StartedOn        = DateTime.UtcNow
			};

			var disclosure = new ProtectedWorkflowDisclosure
			{
				DepartmentId   = departmentId,
				WorkflowId     = workflow.WorkflowId,
				WorkflowRunId  = run.WorkflowRunId,
				WorkflowStepId = step.WorkflowStepId,
				EntityType     = ProtectedWorkflowFieldCatalog.EntityTypeFor(workflow.TriggerEventType)
			};

			var sw = Stopwatch.StartNew();
			var recordDisclosure = false;
			string errorCode = null;
			ProtectedReleasedValues released = null;
			string rendered = null;

			try
			{
				// 1. Condition: the ordinary context only (a fresh copy, so no step can leave values behind for another).
				if (!string.IsNullOrWhiteSpace(step.ConditionExpression))
				{
					var condition = Template.Parse(step.ConditionExpression);
					string conditionResult = null;
					var conditionError = condition.HasErrors;
					if (!conditionError)
					{
						try { conditionResult = (await condition.RenderAsync(NewContext(baseContext, null)))?.Trim() ?? string.Empty; }
						catch (Exception) { conditionError = true; }
					}

					if (conditionError || string.IsNullOrWhiteSpace(conditionResult) || string.Equals(conditionResult, "false", StringComparison.OrdinalIgnoreCase))
					{
						log.Status       = (int)WorkflowRunStatus.Skipped;
						log.ErrorMessage = conditionError ? "Step skipped: condition_error" : "Step skipped: condition evaluated to false.";
						return new ProtectedStepOutcome { Log = log, Failed = false };
					}
				}

				// 2. Action config (URL, headers, If-None-Exist): the ordinary context only. A render failure fails the step —
				// falling back to the raw template would send to a URL nobody approved in that form.
				var renderedActionConfig = step.ActionConfig;
				if (!string.IsNullOrWhiteSpace(step.ActionConfig))
				{
					var configTemplate = Template.Parse(step.ActionConfig);
					if (configTemplate.HasErrors)
						return Fail(ProtectedWorkflowDisclosureOutcomes.FailedRender, ProtectedWorkflowErrorCodes.RenderFailed);
					try { renderedActionConfig = await configTemplate.RenderAsync(NewContext(baseContext, null)); }
					catch (Exception) { return Fail(ProtectedWorkflowDisclosureOutcomes.FailedRender, ProtectedWorkflowErrorCodes.RenderFailed); }
				}

				// 3. Preconditions, re-read fresh for this attempt.
				var authorization = await _protectedRuntime.AuthorizeStepAsync(workflow, step, renderedActionConfig, cancellationToken);
				disclosure.WorkflowProtectedReleaseId = authorization.Release?.WorkflowProtectedReleaseId;
				disclosure.DestinationHost = authorization.Release?.DestinationHost;
				if (!authorization.Allowed)
					return Fail(authorization.Outcome, authorization.ErrorCode);

				var release = authorization.Release;
				var credential = await _credentialRepository.GetByIdAsync(step.WorkflowCredentialId);
				if (credential == null)
					return Fail(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.CredentialNotAllowed);

				// The step options as rendered: declared content type, success rule, capture, idempotency.
				var options = ProtectedStepOptions.Read(renderedActionConfig, out var optionErrors, DataProtectionConfig.ProtectedWorkflowMaxCaptureKeys);
				disclosure.ContentType = options.MediaType;
				if (optionErrors.Count > 0)
					return Fail(ProtectedWorkflowDisclosureOutcomes.FailedValidation, $"{ProtectedWorkflowErrorCodes.PayloadInvalid}: rule={optionErrors[0]}");

				// 4. Decrypt ONLY the allow-listed fields of the triggering entity (a Part 2 field is refused first without consent).
				released = await _protectedRuntime.ResolveReleasedValuesAsync(workflow, release, eventPayloadJson, cancellationToken);
				disclosure.BrokerRequestId = released.BrokerRequestId;
				disclosure.EntityId        = released.EntityId;
				disclosure.EntityType      = released.EntityType ?? disclosure.EntityType;
				if (!released.Success)
					return Fail(released.Outcome ?? ProtectedWorkflowDisclosureOutcomes.FailedBroker, released.ErrorCode ?? ProtectedWorkflowErrorCodes.BrokerFailed);
				disclosure.FieldIds = JsonConvert.SerializeObject(released.FieldIds);

				// 5. Render with protected.* layered over a copy of the ordinary context, then scrub for envelopes.
				var output = Template.Parse(step.OutputTemplate ?? string.Empty);
				if (output.HasErrors)
					return Fail(ProtectedWorkflowDisclosureOutcomes.FailedRender, ProtectedWorkflowErrorCodes.RenderFailed);
				try { rendered = await output.RenderAsync(NewContext(baseContext, (ScriptObject)released.Namespace)); }
				catch (Exception) { return Fail(ProtectedWorkflowDisclosureOutcomes.FailedRender, ProtectedWorkflowErrorCodes.RenderFailed); }

				// Never truncate a protected payload: a partial record is worse than none.
				if (rendered != null && rendered.Length > WorkflowConfig.MaxRenderedContentLength)
					return Fail(ProtectedWorkflowDisclosureOutcomes.FailedRender, ProtectedWorkflowErrorCodes.PayloadTooLarge);

				rendered = ProtectedPayloadValidator.NormalizeBody(options.MediaType, rendered);
				ProtectedOutboundGuard.Scrub(rendered, out var envelopes);
				if (envelopes > 0)
					return Fail(ProtectedWorkflowDisclosureOutcomes.BlockedGuard, ProtectedWorkflowErrorCodes.EnvelopeInPayload);

				// The payload must be valid for what it claims to be before it leaves. The detail is the rule and position only.
				var check = ProtectedPayloadValidator.Validate(options.MediaType, rendered);
				if (!check.Ok)
					return Fail(ProtectedWorkflowDisclosureOutcomes.FailedValidation, check.Describe());

				// 6. Chain the attempt BEFORE anything leaves: if the audit record cannot be written, nothing is sent.
				var bodyBytes = Encoding.UTF8.GetBytes(rendered ?? string.Empty);
				disclosure.PayloadSha256 = ProtectedWorkflowDisclosureChain.Sha256Hex(bodyBytes);
				disclosure.PayloadBytes = bodyBytes.Length;
				if (!await RecordAttemptAsync(disclosure))
					return Fail(null, ProtectedWorkflowErrorCodes.DisclosureUnavailable, record: false);

				// 7. Send in protected mode.
				var decryptedCredJson = _encryptionService.DecryptForDepartment(credential.EncryptedData, departmentId, departmentCode);
				var result = await _executorFactory.GetExecutor((WorkflowActionType)step.ActionType).ExecuteAsync(new WorkflowActionContext
				{
					RenderedContent         = rendered,
					DecryptedCredentialJson = decryptedCredJson,
					ActionConfigJson        = renderedActionConfig,
					WorkflowId              = workflow.WorkflowId,
					WorkflowStepId          = step.WorkflowStepId,
					WorkflowRunId           = run.WorkflowRunId,
					DepartmentId            = departmentId,
					ActionType              = step.ActionType,
					IsFreePlanDepartment    = isFreePlan,
					CredentialType          = credential.CredentialType,
					ProtectedMode           = true,
					PinnedHost              = release.DestinationHost,
					PinnedTokenHost         = release.TokenHost,
					PinnedAuthMethod        = release.AuthMethod,
					IdempotencyKey          = idempotencyKey
				}, cancellationToken);

				recordDisclosure = true;
				disclosure.HttpStatus    = result.HttpStatus;
				disclosure.PayloadSha256 = result.PayloadSha256 ?? disclosure.PayloadSha256;
				disclosure.PayloadBytes  = result.PayloadBytes ?? disclosure.PayloadBytes;
				log.RenderedOutput = ProtectedWorkflowLogText.RenderedOutputSummary(
					result.PayloadSha256 ?? ProtectedWorkflowDisclosureChain.Sha256Hex(Encoding.UTF8.GetBytes(rendered ?? string.Empty)),
					result.PayloadBytes ?? Encoding.UTF8.GetByteCount(rendered ?? string.Empty),
					released.FieldIds);
				log.ActionResult = Cap(result.ResultMessage);

				if (result.Success)
				{
					disclosure.Outcome = ProtectedWorkflowDisclosureOutcomes.Sent;

					// 8. Captured response values go into the call's subject identifiers through the encrypt lane. Only the
					// KEYS are ever written anywhere else: captured=[ehr_encounter_id].
					if (result.CapturedValues != null && result.CapturedValues.Count > 0)
					{
						var write = await _protectedRuntime.WriteCapturedValuesAsync(workflow, release, released.EntityId, result.CapturedValues, cancellationToken);
						if (!write.Success)
						{
							// The record was delivered; sending it again would duplicate it. Stop and alert instead of retrying.
							errorCode = write.ErrorCode ?? ProtectedWorkflowErrorCodes.CaptureFailed;
							disclosure.Detail = errorCode;
							log.Status        = (int)WorkflowRunStatus.Failed;
							log.ErrorMessage  = errorCode;
							return new ProtectedStepOutcome { Log = log, Failed = true, ErrorCode = errorCode, Retryable = false };
						}

						disclosure.CapturedKeys = JsonConvert.SerializeObject(write.WrittenKeys);
						log.ActionResult = Cap($"{result.ResultMessage} captured=[{string.Join(",", write.WrittenKeys)}]");
					}

					log.Status = (int)WorkflowRunStatus.Completed;
					return new ProtectedStepOutcome { Log = log, Failed = false };
				}

				disclosure.Outcome = result.ProtectedOutcome ?? ProtectedWorkflowDisclosureOutcomes.FailedHttp;
				errorCode = disclosure.Outcome switch
				{
					ProtectedWorkflowDisclosureOutcomes.BlockedHost => ProtectedWorkflowErrorCodes.HostMismatch,
					ProtectedWorkflowDisclosureOutcomes.BlockedRelease => result.ErrorDetail ?? ProtectedWorkflowErrorCodes.CredentialNotAllowed,
					ProtectedWorkflowDisclosureOutcomes.FailedAck => ProtectedWorkflowErrorCodes.AckRejected,
					ProtectedWorkflowDisclosureOutcomes.FailedResponseTooLarge => ProtectedWorkflowErrorCodes.ResponseTooLarge,
					ProtectedWorkflowDisclosureOutcomes.FailedValidation => ProtectedWorkflowErrorCodes.PayloadInvalid,
					_ => ProtectedWorkflowErrorCodes.HttpFailed
				};
				disclosure.Detail = Cap(result.ErrorDetail, 500);
				log.Status       = (int)WorkflowRunStatus.Failed;
				log.ErrorMessage = Cap(result.ErrorDetail) ?? errorCode;
				return new ProtectedStepOutcome
				{
					Log = log,
					Failed = true,
					ErrorCode = errorCode,
					Retryable = ProtectedWorkflowRetryPolicy.IsRetryable(disclosure.Outcome, result.HttpStatus, errorCode)
				};
			}
			catch (Exception ex)
			{
				// Fixed code + exception type. The message is never logged or stored: it could quote rendered content.
				errorCode = ProtectedWorkflowErrorCodes.StepError;
				log.Status       = (int)WorkflowRunStatus.Failed;
				log.ErrorMessage = ProtectedWorkflowLogText.Error(errorCode, ex);
				if (recordDisclosure || released != null)
				{
					recordDisclosure = true;
					disclosure.Outcome = disclosure.Outcome ?? (rendered != null ? ProtectedWorkflowDisclosureOutcomes.FailedHttp : ProtectedWorkflowDisclosureOutcomes.FailedRender);
					disclosure.Detail = errorCode;
				}
				Logging.LogError($"Protected workflow step {step.WorkflowStepId} failed for run {run.WorkflowRunId}: {ex.GetType().FullName}.");
				// An exception after the request left must not resend it; before that, another attempt is harmless.
				return new ProtectedStepOutcome { Log = log, Failed = true, ErrorCode = errorCode, Retryable = disclosure.Outcome != ProtectedWorkflowDisclosureOutcomes.Sent };
			}
			finally
			{
				sw.Stop();
				log.DurationMs  = sw.ElapsedMilliseconds;
				log.CompletedOn = DateTime.UtcNow;

				// Drop the only references to plaintext before anything slower happens.
				rendered = null;
				released = null;

				if (recordDisclosure)
					await RecordProtectedDisclosureAsync(disclosure, log);
			}

			// Every refused or failed attempt past the condition is chained too, with its blocked_*/failed_* outcome.
			ProtectedStepOutcome Fail(string outcome, string code, bool record = true)
			{
				errorCode = code;
				recordDisclosure = record;
				disclosure.Outcome = outcome;
				disclosure.Detail  = code;
				log.Status       = (int)WorkflowRunStatus.Failed;
				log.ErrorMessage = code;
				return new ProtectedStepOutcome { Log = log, Failed = true, ErrorCode = code, Retryable = ProtectedWorkflowRetryPolicy.IsRetryable(outcome, null, code) };
			}
		}

		/// <summary>The pre-send record. False (and nothing is sent) when the chain cannot be written.</summary>
		private async Task<bool> RecordAttemptAsync(ProtectedWorkflowDisclosure disclosure)
		{
			if (_protectedWorkflows == null)
				return false;

			var attempt = new ProtectedWorkflowDisclosure
			{
				DepartmentId               = disclosure.DepartmentId,
				WorkflowId                 = disclosure.WorkflowId,
				WorkflowRunId              = disclosure.WorkflowRunId,
				WorkflowStepId             = disclosure.WorkflowStepId,
				WorkflowProtectedReleaseId = disclosure.WorkflowProtectedReleaseId,
				EntityType                 = disclosure.EntityType,
				EntityId                   = disclosure.EntityId,
				FieldIds                   = disclosure.FieldIds,
				DestinationHost            = disclosure.DestinationHost,
				PayloadSha256              = disclosure.PayloadSha256,
				PayloadBytes               = disclosure.PayloadBytes,
				BrokerRequestId            = disclosure.BrokerRequestId,
				IsTest                     = disclosure.IsTest,
				ContentType                = disclosure.ContentType,
				Outcome                    = ProtectedWorkflowDisclosureOutcomes.Attempted
			};

			try
			{
				await _protectedWorkflows.RecordDisclosureAsync(attempt, CancellationToken.None);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow attempt could not be recorded for run {disclosure.WorkflowRunId}, step {disclosure.WorkflowStepId}; not sending: {ex.GetType().FullName}.");
				return false;
			}
		}

		/// <summary>
		/// Writes the disclosure. A failure here never fails the step (the request already left; failing would retry and
		/// send again) — it is logged value-free and flagged on the run log instead.
		/// </summary>
		private async Task RecordProtectedDisclosureAsync(ProtectedWorkflowDisclosure disclosure, WorkflowRunLog log)
		{
			if (_protectedWorkflows == null)
				return;

			try
			{
				await _protectedWorkflows.RecordDisclosureAsync(disclosure, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow disclosure could not be recorded for run {disclosure.WorkflowRunId}, step {disclosure.WorkflowStepId}: {ex.GetType().FullName}.");
				log.ActionResult = Cap((log.ActionResult ?? string.Empty) + " [disclosure_record_failed]");
			}
		}

		/// <summary>A fresh, isolated render context: a deep copy of the ordinary context, optional released values, and a scratch frame for assignments.</summary>
		private static TemplateContext NewContext(ScriptObject baseContext, ScriptObject protectedNamespace)
		{
			var context = new TemplateContext
			{
				LoopLimit       = WorkflowConfig.ScribanLoopLimit,
				StrictVariables = false,
				// A field that is not released (or protected.* in a condition or URL) reads as empty, never as an error.
				EnableRelaxedTargetAccess = true
			};
			context.PushGlobal((ScriptObject)(baseContext ?? new ScriptObject()).Clone(true));
			if (protectedNamespace != null)
				context.PushGlobal(protectedNamespace);
			context.PushGlobal(new ScriptObject());
			return context;
		}

		private static string Cap(string value, int max = 4000) =>
			value == null ? null : value.Length > max ? value.Substring(0, max) : value;

		// ── Test send ─────────────────────────────────────────────────────────────────────────────────

		public async Task<ProtectedWorkflowTestResult> SendProtectedTestAsync(int departmentId, string departmentCode, string workflowId,
			CancellationToken cancellationToken = default)
		{
			if (_protectedRuntime == null || _protectedWorkflows == null)
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.InvalidState };

			var workflow = await _workflowRepository.GetByIdAsync(workflowId);
			if (workflow == null || workflow.DepartmentId != departmentId)
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.NotFound };

			var settings = await _protectedWorkflows.GetDepartmentSettingsAsync(departmentId, bypassCache: true);
			if (!settings.AdpActive)
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.AdpNotEnabled };
			if (!settings.Enabled)
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.DepartmentDisabled };

			var release = await _protectedWorkflows.GetCurrentReleaseAsync(workflowId);
			if (release == null || release.ReleaseState == ProtectedReleaseState.Revoked)
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.InvalidState };

			var steps = await GetStepsByWorkflowIdAsync(workflowId, cancellationToken);
			var credentials = (await _credentialRepository.GetAllByDepartmentIdAsync(departmentId))?.ToList() ?? new List<WorkflowCredential>();
			var validation = ProtectedWorkflowValidator.Validate(workflow.TriggerEventType, steps,
				credentials.Where(c => !string.IsNullOrWhiteSpace(c.WorkflowCredentialId))
					.GroupBy(c => c.WorkflowCredentialId.Trim(), StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().CredentialType, StringComparer.OrdinalIgnoreCase),
				DataProtectionConfig.ProtectedWorkflowAllowHttpBasicCredentials, DataProtectionConfig.ProtectedWorkflowMaxCaptureKeys);
			var pins = validation.CredentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials
				? await _protectedWorkflows.GetCredentialPinsAsync(departmentId, validation.WorkflowCredentialId) ?? new ProtectedCredentialPins()
				: null;
			var tokenHost = pins?.TokenHost;
			if (pins != null && tokenHost == null)
				validation.Add(ProtectedWorkflowValidator.TokenUrlInvalid);
			if (pins?.AuthMethod == WorkflowJwtKeys.PrivateKeyJwt && !pins.HasSigningKey)
				validation.Add(ProtectedWorkflowValidator.SigningKeyMissing);
			if (!validation.IsValid)
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.ValidationFailed, ValidationErrors = validation.Errors };

			// Once a release has pinned its hosts, a test may only go to those hosts (a draft has none pinned yet).
			if (!string.IsNullOrEmpty(release.DestinationHost) &&
				(!string.Equals(validation.DestinationHost, release.DestinationHost, StringComparison.Ordinal) ||
				 !string.Equals(tokenHost, release.TokenHost, StringComparison.Ordinal) ||
				 !string.Equals(pins?.AuthMethod, release.AuthMethod, StringComparison.Ordinal)))
				return new ProtectedWorkflowTestResult { ErrorCode = ProtectedWorkflowErrorCodes.HostMismatch };

			// Synthetic values only: the sample generator for call.*, and sample strings for the allow-listed protected.*.
			var sampleContext = WorkflowSampleDataGenerator.GenerateSampleData((WorkflowTriggerEventType)workflow.TriggerEventType) as ScriptObject ?? new ScriptObject();
			var sample = _protectedRuntime.BuildSampleValues(workflow.TriggerEventType, release.GetAllowedFieldIds());
			var stepResults = new List<ProtectedWorkflowTestStepResult>();
			var testRunId = "test-" + Guid.NewGuid().ToString("N");

			foreach (var step in ProtectedWorkflowFingerprint.OrderedEnabledSteps(steps))
			{
				// A test has its own idempotency key, so an EHR that deduplicates never mistakes it for a real delivery.
				var idempotencyKey = WorkflowIdempotency.Key(workflowId, null, testRunId, step.WorkflowStepId);
				sampleContext["run"] = new ScriptObject { ["id"] = testRunId, ["attempt"] = 1, ["idempotency_key"] = idempotencyKey };

				var disclosure = new ProtectedWorkflowDisclosure
				{
					DepartmentId               = departmentId,
					WorkflowId                 = workflowId,
					WorkflowStepId             = step.WorkflowStepId,
					WorkflowProtectedReleaseId = release.WorkflowProtectedReleaseId,
					EntityType                 = sample.EntityType,
					EntityId                   = sample.EntityId,
					FieldIds                   = JsonConvert.SerializeObject(sample.FieldIds),
					DestinationHost            = validation.DestinationHost,
					IsTest                     = true
				};

				string code = null;
				try
				{
					var renderedActionConfig = await Template.Parse(step.ActionConfig ?? string.Empty).RenderAsync(NewContext(sampleContext, null));
					var options = ProtectedStepOptions.Read(renderedActionConfig, out var optionErrors, DataProtectionConfig.ProtectedWorkflowMaxCaptureKeys);
					disclosure.ContentType = options.MediaType;
					if (!ProtectedWorkflowValidator.TryGetRenderedHttpsHost(ProtectedWorkflowValidator.ReadUrl(renderedActionConfig), out var host, out _) ||
						!string.Equals(host, validation.DestinationHost, StringComparison.Ordinal))
					{
						disclosure.Outcome = ProtectedWorkflowDisclosureOutcomes.BlockedHost;
						code = ProtectedWorkflowErrorCodes.HostMismatch;
					}
					else if (optionErrors.Count > 0)
					{
						disclosure.Outcome = ProtectedWorkflowDisclosureOutcomes.FailedValidation;
						code = $"{ProtectedWorkflowErrorCodes.PayloadInvalid}: rule={optionErrors[0]}";
					}
					else
					{
						var rendered = await Template.Parse(step.OutputTemplate ?? string.Empty).RenderAsync(NewContext(sampleContext, (ScriptObject)sample.Namespace));
						rendered = ProtectedPayloadValidator.NormalizeBody(options.MediaType, rendered);
						ProtectedOutboundGuard.Scrub(rendered, out var envelopes);
						var testBytes = Encoding.UTF8.GetBytes(rendered ?? string.Empty);
						disclosure.PayloadSha256 = ProtectedWorkflowDisclosureChain.Sha256Hex(testBytes);
						disclosure.PayloadBytes = testBytes.Length;
						var check = envelopes > 0 ? ProtectedPayloadCheck.Valid : ProtectedPayloadValidator.Validate(options.MediaType, rendered);
						if (envelopes > 0)
						{
							disclosure.Outcome = ProtectedWorkflowDisclosureOutcomes.BlockedGuard;
							code = ProtectedWorkflowErrorCodes.EnvelopeInPayload;
						}
						else if (!check.Ok)
						{
							disclosure.Outcome = ProtectedWorkflowDisclosureOutcomes.FailedValidation;
							code = check.Describe();
						}
						else if (!await RecordAttemptAsync(disclosure))
						{
							disclosure.Outcome = null;
							code = ProtectedWorkflowErrorCodes.DisclosureUnavailable;
						}
						else
						{
							var credential = credentials.First(c => string.Equals(c.WorkflowCredentialId, validation.WorkflowCredentialId, StringComparison.OrdinalIgnoreCase));
							var result = await _executorFactory.GetExecutor((WorkflowActionType)step.ActionType).ExecuteAsync(new WorkflowActionContext
							{
								RenderedContent         = rendered,
								DecryptedCredentialJson = _encryptionService.DecryptForDepartment(credential.EncryptedData, departmentId, departmentCode),
								ActionConfigJson        = renderedActionConfig,
								WorkflowId              = workflowId,
								WorkflowStepId          = step.WorkflowStepId,
								DepartmentId            = departmentId,
								ActionType              = step.ActionType,
								CredentialType          = credential.CredentialType,
								ProtectedMode           = true,
								PinnedHost              = validation.DestinationHost,
								PinnedTokenHost         = tokenHost,
								PinnedAuthMethod        = pins?.AuthMethod,
								IdempotencyKey          = idempotencyKey
							}, cancellationToken);

							// A test never writes captured values anywhere: there is no real call behind synthetic data.
							disclosure.HttpStatus    = result.HttpStatus;
							disclosure.PayloadSha256 = result.PayloadSha256;
							disclosure.PayloadBytes  = result.PayloadBytes;
							disclosure.Outcome = result.Success ? ProtectedWorkflowDisclosureOutcomes.Sent : result.ProtectedOutcome ?? ProtectedWorkflowDisclosureOutcomes.FailedHttp;
							code = result.Success ? null : disclosure.Outcome switch
							{
								ProtectedWorkflowDisclosureOutcomes.FailedAck => ProtectedWorkflowErrorCodes.AckRejected,
								ProtectedWorkflowDisclosureOutcomes.FailedResponseTooLarge => ProtectedWorkflowErrorCodes.ResponseTooLarge,
								ProtectedWorkflowDisclosureOutcomes.BlockedHost => ProtectedWorkflowErrorCodes.HostMismatch,
								_ => ProtectedWorkflowErrorCodes.HttpFailed
							};
						}
					}
				}
				catch (Exception ex)
				{
					disclosure.Outcome = ProtectedWorkflowDisclosureOutcomes.FailedRender;
					code = ProtectedWorkflowLogText.Error(ProtectedWorkflowErrorCodes.StepError, ex);
				}

				disclosure.Detail = code == null ? "test" : $"test;{code}";
				if (disclosure.Outcome != null)
					await RecordProtectedDisclosureAsync(disclosure, new WorkflowRunLog());
				stepResults.Add(new ProtectedWorkflowTestStepResult
				{
					WorkflowStepId = step.WorkflowStepId,
					Outcome        = disclosure.Outcome,
					HttpStatus     = disclosure.HttpStatus,
					ErrorCode      = code
				});
			}

			var allSent = stepResults.Count > 0 && stepResults.All(s => s.Outcome == ProtectedWorkflowDisclosureOutcomes.Sent);
			var testResult = new ProtectedWorkflowTestResult { Success = allSent, ErrorCode = allSent ? null : ProtectedWorkflowErrorCodes.HttpFailed };
			testResult.Steps.AddRange(stepResults);
			return testResult;
		}

		// ── Lifecycle hooks (best effort: the runtime's fingerprint check is the fail-closed backstop) ───────

		private async Task OnProtectedConfigurationChangedAsync(string workflowId, string actorUserId, CancellationToken cancellationToken)
		{
			if (_protectedWorkflows == null || string.IsNullOrWhiteSpace(workflowId))
				return;
			try { await _protectedWorkflows.OnWorkflowConfigurationChangedAsync(workflowId, actorUserId, cancellationToken); }
			catch (Exception ex) { Logging.LogError($"Protected workflow change hook failed for workflow {workflowId}: {ex.GetType().FullName}."); }
		}

		private async Task OnProtectedWorkflowDeletedAsync(Workflow workflow, CancellationToken cancellationToken)
		{
			if (_protectedWorkflows == null || workflow == null)
				return;
			try { await _protectedWorkflows.OnWorkflowDeletedAsync(workflow, null, cancellationToken); }
			catch (Exception ex) { Logging.LogError($"Protected workflow delete hook failed for workflow {workflow.WorkflowId}: {ex.GetType().FullName}."); }
		}

		private async Task<CredentialChange> CaptureCredentialChangeAsync(WorkflowCredential incoming, string departmentCode)
		{
			if (_protectedWorkflows == null || incoming == null || string.IsNullOrEmpty(incoming.WorkflowCredentialId))
				return null;

			try
			{
				var stored = await _credentialRepository.GetByIdAsync(incoming.WorkflowCredentialId);
				if (stored == null)
					return null;

				string previousJson = null;
				try { previousJson = _encryptionService.DecryptForDepartment(stored.EncryptedData, stored.DepartmentId, departmentCode); }
				catch (Exception) { /* unreadable before: treat as a secret change */ }

				var previousOAuth = stored.CredentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials;
				var currentOAuth = incoming.CredentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials;
				return new CredentialChange
				{
					TypeChanged        = stored.CredentialType != incoming.CredentialType,
					SecretChanged      = !SameJson(previousJson, incoming.EncryptedData),
					PreviousTokenHost  = previousOAuth ? ProtectedWorkflowService.ReadTokenHost(previousJson) : null,
					CurrentTokenHost   = currentOAuth ? ProtectedWorkflowService.ReadTokenHost(incoming.EncryptedData) : null,
					PreviousAuthMethod = previousOAuth ? ProtectedWorkflowService.ReadPins(previousJson).AuthMethod : null,
					CurrentAuthMethod  = currentOAuth ? ProtectedWorkflowService.ReadPins(incoming.EncryptedData).AuthMethod : null
				};
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow credential capture failed for credential {incoming.WorkflowCredentialId}: {ex.GetType().FullName}.");
				return null;
			}
		}

		private async Task OnProtectedCredentialSavedAsync(WorkflowCredential credential, CredentialChange change, CancellationToken cancellationToken)
		{
			if (_protectedWorkflows == null || change == null)
				return;
			try
			{
				await _protectedWorkflows.OnCredentialSavedAsync(credential, change.TypeChanged, change.SecretChanged || change.RotatedKeyId != null,
					change.PreviousTokenHost, change.CurrentTokenHost, credential.UpdatedByUserId, cancellationToken,
					change.PreviousAuthMethod, change.CurrentAuthMethod, change.RotatedKeyId);
			}
			catch (Exception ex) { Logging.LogError($"Protected workflow credential hook failed for credential {credential.WorkflowCredentialId}: {ex.GetType().FullName}."); }
		}

		/// <summary>Semantic JSON equality (property order and whitespace do not matter); ordinal text equality otherwise.</summary>
		private static bool SameJson(string left, string right)
		{
			if (string.Equals(left, right, StringComparison.Ordinal))
				return true;
			try { return JToken.DeepEquals(JToken.Parse(left ?? "null"), JToken.Parse(right ?? "null")); }
			catch (JsonException) { return false; }
		}

		private async Task OnProtectedCredentialDeletedAsync(WorkflowCredential credential, CancellationToken cancellationToken)
		{
			if (_protectedWorkflows == null || credential == null)
				return;
			try { await _protectedWorkflows.OnCredentialDeletedAsync(credential, null, cancellationToken); }
			catch (Exception ex) { Logging.LogError($"Protected workflow credential delete hook failed for credential {credential.WorkflowCredentialId}: {ex.GetType().FullName}."); }
		}
	}
}
