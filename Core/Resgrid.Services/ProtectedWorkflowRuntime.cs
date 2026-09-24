using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Scriban.Runtime;

namespace Resgrid.Services
{
	/// <summary>
	/// Unattended half of Protected Workflows. See <see cref="IProtectedWorkflowRuntime"/>. Every check reads fresh
	/// (bypassing caches) because it gates a disclosure; every failure is closed. Plaintext produced here lives only
	/// in the returned ScriptObject and is never logged: log lines carry ids, codes and exception type names only.
	/// </summary>
	public class ProtectedWorkflowRuntime : IProtectedWorkflowRuntime
	{
		private readonly IWorkflowProtectedReleaseRepository _releases;
		private readonly IWorkflowStepRepository _steps;
		private readonly IWorkflowCredentialRepository _credentials;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IProtectedDataBrokerClient _broker;
		private readonly IProtectedWorkflowService _service;
		private readonly ICallsRepository _calls;
		private readonly IUdfFieldValueRepository _udfValues;
		private readonly IProtectedWriteService _protectedWrites;

		/// <summary>Attempts at the conditional subject-identifier write before a capture gives up (a concurrent edit won each time).</summary>
		private const int CaptureWriteAttempts = 3;

		public ProtectedWorkflowRuntime(IWorkflowProtectedReleaseRepository releases, IWorkflowStepRepository steps,
			IWorkflowCredentialRepository credentials, IDepartmentDataProtectionService dataProtection,
			IProtectedDataBrokerClient broker, IProtectedWorkflowService service, ICallsRepository calls,
			IUdfFieldValueRepository udfValues, IProtectedWriteService protectedWrites)
		{
			_calls = calls;
			_udfValues = udfValues;
			_protectedWrites = protectedWrites;
			_releases = releases;
			_steps = steps;
			_credentials = credentials;
			_dataProtection = dataProtection;
			_broker = broker;
			_service = service;
		}

		public async Task<ProtectedRunGate> GetRunGateAsync(Workflow workflow, CancellationToken cancellationToken = default)
		{
			if (workflow == null || string.IsNullOrWhiteSpace(workflow.WorkflowId))
				return ProtectedRunGate.NotProtected;

			var release = await _releases.GetLatestByWorkflowIdAsync(workflow.WorkflowId);
			if (release == null)
				return ProtectedRunGate.NotProtected;

			if (release.DepartmentId != workflow.DepartmentId)
				return new ProtectedRunGate { SkipReason = "protected_release_invalid", Release = release };

			if (release.ReleaseState == ProtectedReleaseState.Active)
				return new ProtectedRunGate { IsProtected = true, Release = release };

			// Never run unprotected: the destination expects plaintext, and REDACTED would overwrite the real record.
			return new ProtectedRunGate { SkipReason = SkipReasonFor(release.ReleaseState), Release = release };
		}

		public static string SkipReasonFor(ProtectedReleaseState state) => state switch
		{
			ProtectedReleaseState.Draft => "protected_release_draft",
			ProtectedReleaseState.PendingApproval => "protected_release_pending_approval",
			ProtectedReleaseState.Suspended => "protected_release_suspended",
			ProtectedReleaseState.Expired => "protected_release_expired",
			ProtectedReleaseState.Revoked => "protected_release_revoked",
			_ => "protected_release_invalid"
		};

		public async Task<ProtectedStepAuthorization> AuthorizeStepAsync(Workflow workflow, WorkflowStep step, string renderedActionConfig,
			CancellationToken cancellationToken = default)
		{
			if (workflow == null || step == null)
				return ProtectedStepAuthorization.Block(null, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ReleaseNotActive);

			var release = await _releases.GetLatestByWorkflowIdAsync(workflow.WorkflowId);
			if (release == null || release.DepartmentId != workflow.DepartmentId || release.ReleaseState != ProtectedReleaseState.Active)
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ReleaseNotActive);

			var departmentId = workflow.DepartmentId;

			// 1. ADP must be actively protecting the department (Enabled or Rotating). Offboarding or disabled revokes.
			var policy = await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			if (ProtectedWorkflowService.IsOffboardingOrDisabled(policy))
			{
				await _service.RevokeAllForDepartmentAsync(departmentId, ProtectedWorkflowSuspendReasons.AdpOffboarding,
					ProtectedWorkflowService.SystemActor, cancellationToken);
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedDepartment, ProtectedWorkflowErrorCodes.AdpNotEnabled);
			}

			var state = (DepartmentDataProtectionState)policy.State;
			if (state != DepartmentDataProtectionState.Enabled && state != DepartmentDataProtectionState.Rotating)
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedDepartment, ProtectedWorkflowErrorCodes.AdpNotEnabled);

			// 2. The department toggle.
			var egress = await _dataProtection.GetEgressPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			if (egress == null || !egress.ProtectedWorkflowsEnabled)
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedDepartment, ProtectedWorkflowErrorCodes.DepartmentDisabled);

			// 3. Unexpired.
			if (!release.ExpiresOn.HasValue || release.ExpiresOn.Value <= DateTime.UtcNow)
			{
				await _service.MarkExpiredAsync(release, cancellationToken);
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ReleaseExpired);
			}

			// 4. The configuration is exactly what was approved (the save-time hook is the fast path; this is the backstop).
			var steps = (await _steps.GetAllByWorkflowIdAsync(workflow.WorkflowId))?.ToList() ?? new List<WorkflowStep>();
			var fingerprint = await _service.ComputeCurrentFingerprintAsync(workflow, steps, release);
			if (!string.Equals(fingerprint, release.ConfigFingerprint, StringComparison.Ordinal))
			{
				await _service.OnWorkflowConfigurationChangedAsync(workflow.WorkflowId, ProtectedWorkflowService.SystemActor, cancellationToken);
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ConfigChanged);
			}

			// 5. Only API POST or PUT.
			if (!ProtectedWorkflowValidator.IsAllowedActionType(step.ActionType))
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ActionNotAllowed);

			// 6. The pinned credential, still of an allowed type.
			if (string.IsNullOrWhiteSpace(step.WorkflowCredentialId) ||
				!string.Equals(step.WorkflowCredentialId.Trim(), release.WorkflowCredentialId, StringComparison.OrdinalIgnoreCase))
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.CredentialNotAllowed);

			var credential = await _credentials.GetByIdAsync(step.WorkflowCredentialId.Trim());
			if (credential == null || credential.DepartmentId != departmentId ||
				!ProtectedWorkflowValidator.IsAllowedCredentialType(credential.CredentialType, DataProtectionConfig.ProtectedWorkflowAllowHttpBasicCredentials))
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.CredentialNotAllowed);

			// 6b. An OAuth2 credential still authenticates the pinned way, against the pinned token host (the save hook
			// suspends on a change; this is the backstop for an edit that bypassed it).
			if (credential.CredentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials)
			{
				var pins = await _service.GetCredentialPinsAsync(departmentId, credential.WorkflowCredentialId);
				if (pins?.TokenHost == null || !string.Equals(pins.TokenHost, release.TokenHost, StringComparison.Ordinal))
					return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.TokenHostMismatch);
				if (!string.Equals(pins.AuthMethod, release.AuthMethod, StringComparison.Ordinal))
					return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.AuthMethodMismatch);
			}

			// 7. The URL as rendered: https, and exactly the pinned host.
			if (!ProtectedWorkflowValidator.TryGetRenderedHttpsHost(ProtectedWorkflowValidator.ReadUrl(renderedActionConfig), out var host, out var urlError))
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedHost,
					urlError == ProtectedWorkflowValidator.SchemeNotHttps ? ProtectedWorkflowErrorCodes.SchemeNotHttps : ProtectedWorkflowErrorCodes.HostMismatch);

			if (!string.Equals(host, release.DestinationHost, StringComparison.Ordinal))
				return ProtectedStepAuthorization.Block(release, ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.HostMismatch);

			return ProtectedStepAuthorization.Allow(release);
		}

		public async Task<ProtectedReleasedValues> ResolveReleasedValuesAsync(Workflow workflow, WorkflowProtectedRelease release, string eventPayloadJson,
			CancellationToken cancellationToken = default)
		{
			var requestId = Guid.NewGuid().ToString("N");
			var entityType = ProtectedWorkflowFieldCatalog.EntityTypeFor(workflow?.TriggerEventType ?? -1);
			if (workflow == null || release == null || entityType == null)
				return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, null);

			// The queued payload is the SAFE projection (every cataloged value already reads REDACTED), so it only ever
			// supplies the call id. The values come from the stored row, where they are still envelopes.
			var callId = ReadCall(eventPayloadJson)?.CallId ?? 0;
			var entityId = callId > 0 ? callId.ToString(CultureInfo.InvariantCulture) : null;
			if (callId <= 0)
				return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, entityId);

			var call = await _calls.GetByIdAsync(callId);
			if (call == null || call.DepartmentId != release.DepartmentId)
				return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, entityId);

			// Minimum necessary: ONLY the allow-listed fields are read, sent to the broker or rendered.
			var plan = ProtectedWorkflowFieldCatalog.Plan(workflow.TriggerEventType, release.GetAllowedFieldIds());
			if (plan.IsEmpty || plan.Unknown.Count > 0 || plan.HasConflict)
				return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, entityId);

			// Custom fields: the tag each one carries NOW decides whether this call may be sent at all — before anything
			// is decrypted. A Part 2 field needs Part 2 consent on file for this call.
			var customValues = new List<(string Name, UdfField Field, UdfFieldValue Value)>();
			if (plan.UdfNames.Count > 0)
			{
				var customFields = await _service.GetCallCustomFieldsAsync(release.DepartmentId);
				foreach (var name in plan.UdfNames)
				{
					var field = customFields.FirstOrDefault(f => string.Equals(f.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
					if (field == null ||
						(field.Sensitivity == (int)UdfFieldSensitivity.Restricted && !release.AllowsRestricted) ||
						(field.Sensitivity == (int)UdfFieldSensitivity.Part2 && !release.AllowsPart2))
						return Failed(ProtectedWorkflowErrorCodes.ConfigChanged, null, entityType, entityId, ProtectedWorkflowDisclosureOutcomes.BlockedRelease);
					if (field.Sensitivity == (int)UdfFieldSensitivity.Part2 && !call.Part2ConsentOnFile)
						return Failed(ProtectedWorkflowErrorCodes.ConsentMissing, null, entityType, entityId, ProtectedWorkflowDisclosureOutcomes.BlockedConsent);
					customValues.Add((name, field, null));
				}

				var definitionId = customValues[0].Field.UdfDefinitionId;
				var stored = ((await _udfValues.GetFieldValuesByEntityAsync((int)UdfEntityType.Call, entityId, definitionId)) ?? Enumerable.Empty<UdfFieldValue>())
					.Where(v => !string.IsNullOrEmpty(v.UdfFieldId))
					.GroupBy(v => v.UdfFieldId, StringComparer.Ordinal)
					.ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
				customValues = customValues
					.Select(c => (c.Name, c.Field, stored.TryGetValue(c.Field.UdfFieldId ?? string.Empty, out var value) ? value : null))
					.ToList();
			}

			// Gather what has to go to the broker; plaintext stored before enrollment needs no decrypt.
			var plain = new Dictionary<(string RowKey, string FieldId), string>();
			var items = new List<ProtectedFieldOperationItem>();
			bool Queue(string fieldId, string rowKey, string raw)
			{
				if (string.Equals(raw, ProtectedDataEnvelope.RedactionValue, StringComparison.Ordinal))
					return false; // never send the placeholder
				if (string.IsNullOrEmpty(raw))
					plain[(rowKey, fieldId)] = string.Empty;
				else if (ProtectedDataEnvelope.HasEnvelopePrefix(raw))
					items.Add(new ProtectedFieldOperationItem { FieldId = fieldId, RowKey = rowKey, Value = raw });
				else
					plain[(rowKey, fieldId)] = raw;
				return true;
			}

			foreach (var column in plan.Columns)
				if (!Queue(column.FieldId, entityId, column.GetCallValue(call)))
					return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, entityId);
			if (plan.ReadsSubjectIdentifiers && !Queue(ProtectedWorkflowFieldCatalog.SubjectIdentifiersFieldId, entityId, call.SubjectIdentifiers))
				return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, entityId);
			foreach (var custom in customValues.Where(c => c.Value != null))
				if (!Queue(ProtectedWorkflowFieldCatalog.UdfValueCatalogFieldId, custom.Value.UdfFieldValueId, custom.Value.Value))
					return Failed(ProtectedWorkflowErrorCodes.EntityUnavailable, null, entityType, entityId);

			if (items.Count > 0)
			{
				var decrypted = await DecryptAsync(workflow, release, requestId, items, cancellationToken);
				if (decrypted.ErrorCode != null)
					return Failed(decrypted.ErrorCode, requestId, entityType, entityId);
				foreach (var pair in decrypted.Values)
					plain[pair.Key] = pair.Value;
			}

			// Projection. The subject identifiers are parsed immediately and cut down to the allow-listed keys; the full
			// plaintext string is not kept. Malformed identifiers fail the step: never a fall back to the raw text.
			ScriptObject subjectIds = null;
			if (plan.ReadsSubjectIdentifiers)
			{
				plain.TryGetValue((entityId, ProtectedWorkflowFieldCatalog.SubjectIdentifiersFieldId), out var identifiersJson);
				if (!CallSubjectIdentifiers.TryParse(identifiersJson, out var identifiers))
					return Failed(ProtectedWorkflowErrorCodes.ProjectionFailed, items.Count > 0 ? requestId : null, entityType, entityId,
						ProtectedWorkflowDisclosureOutcomes.FailedProjection);
				plain.Remove((entityId, ProtectedWorkflowFieldCatalog.SubjectIdentifiersFieldId));

				subjectIds = new ScriptObject();
				foreach (var pair in identifiers.Where(p => plan.SubjectIdentifiersWhole || plan.SubjectIdentifierKeys.Contains(p.Key)))
					subjectIds[pair.Key] = pair.Value;
			}

			var columnValues = plan.Columns.ToDictionary(c => c.FieldId,
				c => plain.TryGetValue((entityId, c.FieldId), out var value) ? value : string.Empty, StringComparer.Ordinal);
			var customResults = customValues.Select(c => (c.Field.Name.Trim(),
				c.Value != null && plain.TryGetValue((c.Value.UdfFieldValueId, ProtectedWorkflowFieldCatalog.UdfValueCatalogFieldId), out var value) ? value : string.Empty));

			return new ProtectedReleasedValues
			{
				Success = true,
				Namespace = BuildNamespace(plan.Columns, columnValues, subjectIds, plan.UdfNames.Count > 0 ? customResults : null),
				FieldIds = release.GetAllowedFieldIds(),
				EntityType = entityType,
				EntityId = entityId,
				BrokerRequestId = items.Count > 0 ? requestId : null
			};
		}

		/// <summary>One workload decrypt of the queued items (fresh request id). Any missing or failed item fails the whole call.</summary>
		private async Task<(string ErrorCode, Dictionary<(string RowKey, string FieldId), string> Values)> DecryptAsync(Workflow workflow,
			WorkflowProtectedRelease release, string requestId, List<ProtectedFieldOperationItem> items, CancellationToken cancellationToken)
		{
			if (!_broker.IsConfigured)
				return (ProtectedWorkflowErrorCodes.BrokerFailed, null);

			int catalogVersion;
			try
			{
				catalogVersion = (await _dataProtection.GetPolicyByDepartmentIdAsync(release.DepartmentId, bypassCache: true))?.CatalogVersion ?? 0;
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow {workflow.WorkflowId}: protection lookup failed ({ex.GetType().FullName}).");
				return (ProtectedWorkflowErrorCodes.BrokerFailed, null);
			}

			foreach (var item in items)
				item.CatalogVersion = catalogVersion;

			ProtectedDataBrokerResult result;
			try
			{
				// A fresh request id per attempt: the broker's replay protection refuses a reused one.
				result = await _broker.DecryptForWorkloadAsync(release.DepartmentId, ProtectedWorkflowDefaults.WorkloadPurpose, requestId, items, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow {workflow.WorkflowId}: broker call failed ({ex.GetType().FullName}).");
				return (ProtectedWorkflowErrorCodes.BrokerFailed, null);
			}

			if (result == null || !result.Success)
				return (BrokerCode(result?.ErrorCode), null);

			var decrypted = (result.Items ?? new List<ProtectedFieldOperationResult>())
				.Where(i => i?.FieldId != null && i.RowKey != null)
				.GroupBy(i => (i.RowKey, FieldId: i.FieldId.ToLowerInvariant()))
				.ToDictionary(g => g.Key, g => g.First());

			var values = new Dictionary<(string RowKey, string FieldId), string>();
			foreach (var item in items)
			{
				// Every requested field must come back; one missing value fails the whole send (never partial).
				if (!decrypted.TryGetValue((item.RowKey, item.FieldId), out var outcome) || outcome.ErrorCode != null || outcome.Value == null)
					return (BrokerCode(outcome?.ErrorCode), null);
				values[(item.RowKey, item.FieldId)] = outcome.Value;
			}

			return (null, values);
		}

		public ProtectedReleasedValues BuildSampleValues(int triggerEventType, IEnumerable<string> fieldIds)
		{
			var plan = ProtectedWorkflowFieldCatalog.Plan(triggerEventType, fieldIds);
			var values = plan.Columns.ToDictionary(f => f.FieldId, f => SampleValue(f), StringComparer.Ordinal);
			var subjectIds = plan.ReadsSubjectIdentifiers
				? WorkflowSampleDataGenerator.SampleSubjectIdentifiers(plan.SubjectIdentifiersWhole ? null : plan.SubjectIdentifierKeys)
				: null;
			var custom = plan.UdfNames.Select(n => (n, WorkflowSampleDataGenerator.SampleCustomFieldValue(n)));

			return new ProtectedReleasedValues
			{
				Success = true,
				Namespace = BuildNamespace(plan.Columns, values, subjectIds, plan.UdfNames.Count > 0 ? custom : null),
				FieldIds = WorkflowProtectedRelease.NormalizeFieldIds(fieldIds),
				EntityType = ProtectedWorkflowFieldCatalog.EntityTypeFor(triggerEventType),
				EntityId = "sample"
			};
		}

		public async Task<ProtectedCaptureWriteResult> WriteCapturedValuesAsync(Workflow workflow, WorkflowProtectedRelease release, string entityId,
			IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
		{
			if (values == null || values.Count == 0)
				return new ProtectedCaptureWriteResult { Success = true };
			if (workflow == null || release == null)
				return CaptureFailed();

			// Merging means reading the stored set: only a release that already reads the subject identifiers may capture.
			if (!ProtectedWorkflowFieldCatalog.Plan(workflow.TriggerEventType, release.GetAllowedFieldIds()).ReadsSubjectIdentifiers)
				return CaptureFailed(ProtectedWorkflowErrorCodes.CaptureRequiresSubjectIdentifiers);

			var captured = new SortedDictionary<string, string>(StringComparer.Ordinal);
			foreach (var pair in values)
				captured[pair.Key] = pair.Value?.Trim();
			if (CallSubjectIdentifiers.Validate(captured).Count > 0)
				return CaptureFailed();

			if (!int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var callId) || callId <= 0)
				return CaptureFailed(ProtectedWorkflowErrorCodes.EntityUnavailable);

			for (var attempt = 0; attempt < CaptureWriteAttempts; attempt++)
			{
				var call = await _calls.GetByIdAsync(callId);
				if (call == null || call.DepartmentId != release.DepartmentId)
					return CaptureFailed(ProtectedWorkflowErrorCodes.EntityUnavailable);

				var stored = call.SubjectIdentifiers;
				if (string.Equals(stored, ProtectedDataEnvelope.RedactionValue, StringComparison.Ordinal))
					return CaptureFailed(ProtectedWorkflowErrorCodes.EntityUnavailable);

				var currentJson = stored;
				if (ProtectedDataEnvelope.HasEnvelopePrefix(stored))
				{
					var decrypted = await DecryptAsync(workflow, release, Guid.NewGuid().ToString("N"), new List<ProtectedFieldOperationItem>
					{
						new ProtectedFieldOperationItem { FieldId = ProtectedWorkflowFieldCatalog.SubjectIdentifiersFieldId, RowKey = entityId, Value = stored }
					}, cancellationToken);
					if (decrypted.ErrorCode != null)
						return CaptureFailed(ProtectedWorkflowErrorCodes.BrokerFailed);
					currentJson = decrypted.Values.Values.FirstOrDefault();
				}

				if (!CallSubjectIdentifiers.TryParse(currentJson, out var merged))
					return CaptureFailed(ProtectedWorkflowErrorCodes.ProjectionFailed);
				foreach (var pair in captured)
					merged[pair.Key] = pair.Value;
				if (CallSubjectIdentifiers.Validate(merged).Count > 0)
					return CaptureFailed();

				// Encrypt through the workload lane exactly as any system write of a call field would (plaintext only for a
				// department that is not encrypting new writes, or is pinned below the catalog that added the field).
				var scratch = new Call { CallId = callId, DepartmentId = call.DepartmentId, SubjectIdentifiers = CallSubjectIdentifiers.Serialize(merged) };
				var write = await _protectedWrites.PrepareCallWriteAsync(call.DepartmentId, scratch, null, null, ProtectedWorkflowService.SystemActor,
					workloadCaller: true, cancellationToken);
				if (!write.Success)
					return CaptureFailed(ProtectedWorkflowErrorCodes.BrokerFailed);

				if (await _calls.TryUpdateSubjectIdentifiersAsync(callId, call.DepartmentId, stored, scratch.SubjectIdentifiers, cancellationToken))
					return new ProtectedCaptureWriteResult { Success = true, WrittenKeys = captured.Keys.ToList() };
			}

			return CaptureFailed(ProtectedWorkflowErrorCodes.ConcurrentChange);
		}

		/// <summary>
		/// { protected: { call: { completed_notes, form_data, form, subject_ids: { key }, udf: { name } } } } — only what
		/// was released. Custom fields render under their defined (machine) name, so a template can also loop over them.
		/// </summary>
		private static ScriptObject BuildNamespace(IEnumerable<ProtectedWorkflowField> columns, IReadOnlyDictionary<string, string> values,
			ScriptObject subjectIds, IEnumerable<(string Name, string Value)> customFields)
		{
			var entity = new ScriptObject();
			foreach (var field in columns)
			{
				values.TryGetValue(field.FieldId, out var value);
				entity[field.TemplateName] = value ?? string.Empty;

				if (field.FieldId == ProtectedWorkflowFieldCatalog.FormDataFieldId)
					entity[ProtectedWorkflowFieldCatalog.ParsedFormName] = ParseForm(value);
			}

			if (subjectIds != null)
				entity[ProtectedWorkflowFieldCatalog.SubjectIdentifiersTemplateName] = subjectIds;

			if (customFields != null)
			{
				var udf = new ScriptObject();
				foreach (var (name, value) in customFields)
					udf[name] = value ?? string.Empty;
				entity[ProtectedWorkflowFieldCatalog.UdfTemplateName] = udf;
			}

			return new ScriptObject
			{
				[ProtectedWorkflowFieldCatalog.NamespaceRoot] = new ScriptObject { [ProtectedWorkflowDefaults.CallEntityType] = entity }
			};
		}

		private static object ParseForm(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new ScriptObject();
			try
			{
				using var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None };
				var token = JToken.ReadFrom(reader);
				if (token is JArray fields)
					return FormValues(fields);
				return token.Type == JTokenType.Object ? WorkflowTemplateContextBuilder.ToScriptValue(token) : new ScriptObject();
			}
			catch (JsonException)
			{
				return new ScriptObject();
			}
		}

		/// <summary>
		/// Call form data is the form builder's field array with each answer in <c>userData</c>; the template sees it as
		/// { fieldName: value } (a single answer as a string, several as an array).
		/// </summary>
		public static ScriptObject FormValues(JArray fields)
		{
			var values = new ScriptObject();
			foreach (var field in fields.OfType<JObject>())
			{
				var name = field.Value<string>("name");
				if (string.IsNullOrWhiteSpace(name))
					continue;

				var answers = field["userData"] as JArray;
				if (answers == null || answers.Count == 0)
					values[name] = string.Empty;
				else if (answers.Count == 1)
					values[name] = answers[0]?.ToString() ?? string.Empty;
				else
				{
					var list = new ScriptArray();
					foreach (var answer in answers)
						list.Add(answer?.ToString() ?? string.Empty);
					values[name] = list;
				}
			}

			return values;
		}

		private static string SampleValue(ProtectedWorkflowField field) => field.FieldId switch
		{
			ProtectedWorkflowFieldCatalog.FormDataFieldId => "{\"outcome\":\"Referred to follow-up care\",\"follow_up_required\":\"yes\",\"sample\":true}",
			"calls.completednotes" => "SAMPLE: client stabilized on scene, referred to county follow-up (synthetic test data)",
			"calls.contactnumber" => "555-0100",
			"calls.geolocationdata" => "0,0",
			_ => $"SAMPLE {field.TemplateName} (synthetic test data)"
		};

		private static Call ReadCall(string eventPayloadJson)
		{
			if (string.IsNullOrWhiteSpace(eventPayloadJson))
				return null;

			return TryDeserialize<CallAddedEvent>(eventPayloadJson)?.Call
				?? TryDeserialize<CallUpdatedEvent>(eventPayloadJson)?.Call
				?? TryDeserialize<CallClosedEvent>(eventPayloadJson)?.Call;
		}

		private static T TryDeserialize<T>(string json) where T : class
		{
			try { return JsonConvert.DeserializeObject<T>(json); }
			catch { return null; }
		}

		private static string BrokerCode(string brokerErrorCode) =>
			string.IsNullOrWhiteSpace(brokerErrorCode)
				? ProtectedWorkflowErrorCodes.BrokerFailed
				: $"{ProtectedWorkflowErrorCodes.BrokerFailed}:{new string(brokerErrorCode.Where(c => char.IsLetterOrDigit(c) || c == '_').Take(48).ToArray())}";

		private static ProtectedReleasedValues Failed(string errorCode, string requestId, string entityType, string entityId, string outcome = null) =>
			new ProtectedReleasedValues { Success = false, ErrorCode = errorCode, BrokerRequestId = requestId, EntityType = entityType, EntityId = entityId, Outcome = outcome };

		private static ProtectedCaptureWriteResult CaptureFailed(string code = null) =>
			new ProtectedCaptureWriteResult { Success = false, ErrorCode = code ?? ProtectedWorkflowErrorCodes.CaptureFailed };
	}
}
