using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.ProtectedWorkflows;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Administrative half of Protected Workflows. See <see cref="IProtectedWorkflowService"/> for the contract.
	/// Holds no plaintext and performs no decryption; the unattended half is <see cref="ProtectedWorkflowRuntime"/>.
	/// </summary>
	public class ProtectedWorkflowService : IProtectedWorkflowService
	{
		/// <summary>Actor recorded on transitions the system makes on its own (sweep, lifecycle hooks without a user).</summary>
		public const string SystemActor = "system:protected-workflows";

		private static readonly TimeSpan StepUpClockSkew = TimeSpan.FromMinutes(1);

		private readonly IWorkflowProtectedReleaseRepository _releases;
		private readonly IProtectedWorkflowDisclosureRepository _disclosures;
		private readonly IWorkflowRepository _workflows;
		private readonly IWorkflowStepRepository _steps;
		private readonly IWorkflowCredentialRepository _credentials;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDepartmentsService _departments;
		private readonly IPermissionsService _permissions;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IEncryptionService _encryption;
		private readonly IUserProfileService _profiles;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IUdfDefinitionRepository _udfDefinitions;
		private readonly IUdfFieldRepository _udfFields;

		/// <summary>Subject identifier keys the release panel always offers (EHR integration conventions).</summary>
		public static readonly string[] WellKnownSubjectKeys = { "ehr_client_id", "ehr_encounter_id", "dynamics_case_id" };

		public ProtectedWorkflowService(IWorkflowProtectedReleaseRepository releases, IProtectedWorkflowDisclosureRepository disclosures,
			IWorkflowRepository workflows, IWorkflowStepRepository steps, IWorkflowCredentialRepository credentials,
			IDepartmentDataProtectionService dataProtection, IDepartmentsService departments, IPermissionsService permissions,
			IDepartmentGroupsService groups, IPersonnelRolesService roles, IEncryptionService encryption,
			IUserProfileService profiles, Lazy<ICommunicationService> communication, IUdfDefinitionRepository udfDefinitions,
			IUdfFieldRepository udfFields)
		{
			_udfDefinitions = udfDefinitions;
			_udfFields = udfFields;
			_releases = releases;
			_disclosures = disclosures;
			_workflows = workflows;
			_steps = steps;
			_credentials = credentials;
			_dataProtection = dataProtection;
			_departments = departments;
			_permissions = permissions;
			_groups = groups;
			_roles = roles;
			_encryption = encryption;
			_profiles = profiles;
			_communication = communication;
		}

		// ── Authorization ─────────────────────────────────────────────────────────────────────────────

		public async Task<bool> CanAdministerAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;

			var department = await _departments.GetDepartmentByIdAsync(departmentId);
			if (department == null)
				return false;

			// ADP permissions never use the wide-open "missing row means allowed" convention.
			var permission = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ConfigureProtectedDataEgress)
				?? new Permission
				{
					DepartmentId = departmentId,
					PermissionType = (int)PermissionTypes.ConfigureProtectedDataEgress,
					Action = (int)AdpPermissionDefaults.For(PermissionTypes.ConfigureProtectedDataEgress)
				};

			var isDepartmentAdmin = department.IsUserAnAdmin(userId) ||
				string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			var isGroupAdmin = group != null && group.IsUserGroupAdmin(userId);
			var roles = await _roles.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();

			return _permissions.IsUserAllowed(permission, isDepartmentAdmin, isGroupAdmin, roles);
		}

		/// <summary>Null when the actor may run the command; otherwise a ProtectedWorkflowErrorCodes value.</summary>
		private async Task<string> AuthorizeAsync(int departmentId, ProtectedWorkflowActor actor, bool requireStepUp)
		{
			if (actor == null || string.IsNullOrWhiteSpace(actor.UserId))
				return ProtectedWorkflowErrorCodes.PermissionDenied;

			if (requireStepUp)
			{
				// API keys, client-credentials tokens and workers can never approve, renew or toggle.
				if (!actor.IsInteractive)
					return ProtectedWorkflowErrorCodes.InteractiveRequired;
				if (!IsStepUpFresh(actor.StepUpVerifiedAtUtc, DateTime.UtcNow))
					return ProtectedWorkflowErrorCodes.StepUpRequired;
			}

			return await CanAdministerAsync(departmentId, actor.UserId) ? null : ProtectedWorkflowErrorCodes.PermissionDenied;
		}

		public static bool IsStepUpFresh(DateTime? verifiedAtUtc, DateTime utcNow)
		{
			if (!verifiedAtUtc.HasValue)
				return false;

			var freshness = TimeSpan.FromMinutes(Math.Max(1, DataProtectionConfig.ProtectedWorkflowStepUpFreshnessMinutes));
			var age = utcNow - verifiedAtUtc.Value;
			return age <= freshness && age >= -StepUpClockSkew;
		}

		// ── Department settings ───────────────────────────────────────────────────────────────────────

		public async Task<ProtectedWorkflowDepartmentSettings> GetDepartmentSettingsAsync(int departmentId, bool bypassCache = false)
		{
			var egress = await _dataProtection.GetEgressPolicyByDepartmentIdAsync(departmentId, bypassCache);
			var state = await _dataProtection.GetStateAsync(departmentId, bypassCache);
			return new ProtectedWorkflowDepartmentSettings
			{
				DepartmentId = departmentId,
				Enabled = egress?.ProtectedWorkflowsEnabled ?? false,
				RequireSecondApprover = egress?.ProtectedWorkflowsRequireSecondApprover ?? false,
				AckVersion = egress?.ProtectedWorkflowsAckVersion,
				AckByUserId = egress?.ProtectedWorkflowsAckByUserId,
				AckOn = egress?.ProtectedWorkflowsAckOn,
				AdpState = state,
				RelaxRequestedByUserId = egress?.ProtectedWorkflowsRelaxRequestedByUserId,
				RelaxRequestedOn = egress?.ProtectedWorkflowsRelaxRequestedOn
			};
		}

		/// <summary>How long a request to turn the two-person rule off waits for a second administrator.</summary>
		private static readonly TimeSpan RelaxRequestLifetime = TimeSpan.FromDays(7);

		public async Task<ProtectedWorkflowCommandResult> SetDepartmentSettingsAsync(int departmentId, bool enabled, bool requireSecondApprover,
			string acknowledgedVersion, ProtectedWorkflowActor actor, CancellationToken cancellationToken = default)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: true);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);

			var state = await _dataProtection.GetStateAsync(departmentId, bypassCache: true);
			var policy = await _dataProtection.GetEgressPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			var now = DateTime.UtcNow;
			var wasEnabled = policy.ProtectedWorkflowsEnabled;
			var wasSecondApprover = policy.ProtectedWorkflowsRequireSecondApprover;
			var changed = false;
			var events = new List<(string Type, string Detail)>();

			if (enabled)
			{
				if (state != DepartmentDataProtectionState.Enabled && state != DepartmentDataProtectionState.Rotating)
					return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AdpNotEnabled);

				// Turning it on (or keeping it on after the warning text changed) needs the CURRENT text acknowledged.
				var alreadyCurrent = wasEnabled && string.Equals(policy.ProtectedWorkflowsAckVersion, ProtectedWorkflowDefaults.WarningTextVersion, StringComparison.Ordinal);
				if (!alreadyCurrent)
				{
					if (!string.Equals(acknowledgedVersion, ProtectedWorkflowDefaults.WarningTextVersion, StringComparison.Ordinal))
						return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AckVersionMismatch);

					policy.ProtectedWorkflowsAckVersion = ProtectedWorkflowDefaults.WarningTextVersion;
					policy.ProtectedWorkflowsAckByUserId = actor.UserId;
					policy.ProtectedWorkflowsAckOn = now;
					changed = true;
				}
			}

			// The two-person rule: tightening is immediate; relaxing is itself two-person. The first administrator's request
			// is parked on the policy and only a DIFFERENT administrator can confirm it within RelaxRequestLifetime.
			var pendingConfirmation = false;
			if (requireSecondApprover)
			{
				if (!wasSecondApprover || policy.ProtectedWorkflowsRelaxRequestedByUserId != null)
				{
					policy.ProtectedWorkflowsRequireSecondApprover = true;
					policy.ProtectedWorkflowsRelaxRequestedByUserId = null;
					policy.ProtectedWorkflowsRelaxRequestedOn = null;
					changed = true;
				}
			}
			else if (wasSecondApprover)
			{
				var pendingBy = policy.ProtectedWorkflowsRelaxRequestedByUserId;
				var pendingLive = pendingBy != null && policy.ProtectedWorkflowsRelaxRequestedOn.HasValue &&
					now - policy.ProtectedWorkflowsRelaxRequestedOn.Value <= RelaxRequestLifetime;

				if (pendingLive && !string.Equals(pendingBy, actor.UserId, StringComparison.OrdinalIgnoreCase))
				{
					policy.ProtectedWorkflowsRequireSecondApprover = false;
					policy.ProtectedWorkflowsRelaxRequestedByUserId = null;
					policy.ProtectedWorkflowsRelaxRequestedOn = null;
					events.Add((ProtectedWorkflowAdminEventTypes.SecondApproverRelaxed, $"requested_by={pendingBy}"));
					changed = true;
				}
				else if (pendingLive)
				{
					// The requester asking again changes nothing: someone else has to confirm.
					if (wasEnabled == enabled && !changed)
						return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.SelfApproval);
					pendingConfirmation = true;
				}
				else
				{
					policy.ProtectedWorkflowsRelaxRequestedByUserId = actor.UserId;
					policy.ProtectedWorkflowsRelaxRequestedOn = now;
					events.Add((ProtectedWorkflowAdminEventTypes.SecondApproverRelaxRequested, null));
					pendingConfirmation = true;
					changed = true;
				}
			}

			if (wasEnabled != enabled)
				changed = true;

			if (!changed)
				return new ProtectedWorkflowCommandResult { Success = true, PendingConfirmation = pendingConfirmation };

			policy.ProtectedWorkflowsEnabled = enabled;

			try
			{
				// Saving the egress policy bumps the PolicyEpoch and drops the cached copy.
				await _dataProtection.SaveEgressPolicyAsync(policy, actor.UserId, cancellationToken);
			}
			catch (ArgumentException)
			{
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ValidationFailed);
			}

			if (enabled && (!wasEnabled || wasSecondApprover != policy.ProtectedWorkflowsRequireSecondApprover))
				await RecordAdminEventAsync(departmentId, ProtectedWorkflowAdminEventTypes.DepartmentEnabled, actor.UserId, null, null,
					$"second_approver={(policy.ProtectedWorkflowsRequireSecondApprover ? "on" : "off")};ack={ProtectedWorkflowDefaults.WarningTextVersion}", cancellationToken);

			foreach (var (type, detail) in events)
				await RecordAdminEventAsync(departmentId, type, actor.UserId, null, null, detail, cancellationToken);

			if (!enabled && wasEnabled)
			{
				await RecordAdminEventAsync(departmentId, ProtectedWorkflowAdminEventTypes.DepartmentDisabled, actor.UserId, null, null, null, cancellationToken);
				foreach (var release in (await _releases.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<WorkflowProtectedRelease>())
					.Where(r => r.ReleaseState == ProtectedReleaseState.Active || r.ReleaseState == ProtectedReleaseState.PendingApproval))
					await SuspendInternalAsync(release, ProtectedWorkflowSuspendReasons.DepartmentDisabled, actor.UserId, cancellationToken);
			}

			return new ProtectedWorkflowCommandResult { Success = true, PendingConfirmation = pendingConfirmation };
		}

		// ── Releases ──────────────────────────────────────────────────────────────────────────────────

		public Task<WorkflowProtectedRelease> GetCurrentReleaseAsync(string workflowId) =>
			string.IsNullOrWhiteSpace(workflowId) ? Task.FromResult<WorkflowProtectedRelease>(null) : _releases.GetLatestByWorkflowIdAsync(workflowId);

		public async Task<List<WorkflowProtectedRelease>> GetReleasesForDepartmentAsync(int departmentId) =>
			(await _releases.GetAllByDepartmentIdAsync(departmentId))?.ToList() ?? new List<WorkflowProtectedRelease>();

		public async Task<ProtectedWorkflowReleaseView> GetReleaseViewAsync(int departmentId, string workflowId, string userId,
			CancellationToken cancellationToken = default)
		{
			var workflow = await LoadWorkflowAsync(departmentId, workflowId);
			if (workflow == null)
				return null;

			var steps = await LoadStepsAsync(workflowId);
			var credentialTypes = await LoadCredentialTypesAsync(departmentId);
			var validation = Validate(workflow, steps, credentialTypes);
			var pins = await ResolvePinsAsync(departmentId, validation);
			var tokenHost = pins?.TokenHost;
			var release = await _releases.GetLatestByWorkflowIdAsync(workflowId);
			var customFields = ProtectedWorkflowFieldCatalog.IsSupportedTrigger(workflow.TriggerEventType)
				? await GetCallCustomFieldsAsync(departmentId)
				: new List<UdfField>();
			var current = release == null ? null : ComputeFingerprint(workflow, steps, release, customFields);
			var sensitivities = release == null ? new Dictionary<string, int>() : Sensitivities(release.GetAllowedFieldIds(), customFields);
			var suggestedKeys = WellKnownSubjectKeys
				.Concat(steps.SelectMany(s => ProtectedStepOptions.Read(s.ActionConfig, out _).ResponseCapture.Select(c => c.Key)))
				.Concat(release == null ? Enumerable.Empty<string>() : ProtectedWorkflowFieldCatalog.Plan(workflow.TriggerEventType, release.GetAllowedFieldIds()).SubjectIdentifierKeys)
				.Where(k => !string.IsNullOrWhiteSpace(k) && ProtectedStepOptions.SubjectKeyPattern.IsMatch(k))
				.Distinct(StringComparer.Ordinal)
				.OrderBy(k => k, StringComparer.Ordinal)
				.ToList();

			return new ProtectedWorkflowReleaseView
			{
				Workflow = workflow,
				Release = release,
				Department = await GetDepartmentSettingsAsync(departmentId),
				TriggerSupported = ProtectedWorkflowFieldCatalog.IsSupportedTrigger(workflow.TriggerEventType),
				CanAdminister = await CanAdministerAsync(departmentId, userId),
				Validation = validation,
				AvailableFields = ProtectedWorkflowFieldCatalog.FieldsFor(workflow.TriggerEventType),
				CurrentFingerprint = current,
				FingerprintMatches = release != null && !string.IsNullOrEmpty(release.ConfigFingerprint) &&
					string.Equals(release.ConfigFingerprint, current, StringComparison.Ordinal),
				StepsFingerprint = ComputeStepsFingerprint(workflow, steps, validation.DestinationHost, tokenHost, pins?.AuthMethod),
				TokenHost = tokenHost,
				AuthMethod = pins?.AuthMethod,
				AvailableUdfFields = customFields,
				SuggestedSubjectKeys = suggestedKeys,
				NeedsRestrictedAttestation = sensitivities.Values.Contains((int)UdfFieldSensitivity.Restricted),
				NeedsPart2Attestation = sensitivities.Values.Contains((int)UdfFieldSensitivity.Part2)
			};
		}

		public async Task<List<UdfField>> GetCallCustomFieldsAsync(int departmentId)
		{
			var definition = await _udfDefinitions.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, (int)UdfEntityType.Call);
			if (definition == null || definition.DepartmentId != departmentId)
				return new List<UdfField>();

			return ((await _udfFields.GetFieldsByDefinitionIdAsync(definition.UdfDefinitionId)) ?? Enumerable.Empty<UdfField>())
				.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Name))
				.OrderBy(f => f.SortOrder)
				.ToList();
		}

		public async Task<string> ComputeCurrentFingerprintAsync(Workflow workflow, IEnumerable<WorkflowStep> steps, WorkflowProtectedRelease release)
		{
			if (workflow == null || release == null)
				return null;

			// Custom field tags are only read when the release carries a custom field.
			var customFields = ProtectedWorkflowFieldCatalog.Plan(workflow.TriggerEventType, release.GetAllowedFieldIds()).UdfNames.Count > 0
				? await GetCallCustomFieldsAsync(release.DepartmentId)
				: new List<UdfField>();
			return ComputeFingerprint(workflow, steps, release, customFields);
		}

		public async Task<ProtectedWorkflowCommandResult> SaveDraftAsync(int departmentId, string workflowId, ProtectedReleaseDraft draft,
			ProtectedWorkflowActor actor, CancellationToken cancellationToken = default)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: false);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);

			var settings = await GetDepartmentSettingsAsync(departmentId, bypassCache: true);
			if (!settings.AdpActive)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AdpNotEnabled);
			if (!settings.Enabled)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.DepartmentDisabled);

			var workflow = await LoadWorkflowAsync(departmentId, workflowId);
			if (workflow == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);
			if (!ProtectedWorkflowFieldCatalog.IsSupportedTrigger(workflow.TriggerEventType))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.TriggerNotSupported);

			draft ??= new ProtectedReleaseDraft();
			var fields = WorkflowProtectedRelease.NormalizeFieldIds(draft.FieldIds);
			var fieldError = ValidateFieldIds(workflow.TriggerEventType, fields, requireAny: false, await GetCallCustomFieldsAsync(departmentId));
			if (fieldError != null)
				return ProtectedWorkflowCommandResult.Fail(fieldError);

			var now = DateTime.UtcNow;
			var release = await _releases.GetLatestByWorkflowIdAsync(workflowId);
			var isNew = release == null || release.ReleaseState == ProtectedReleaseState.Revoked;
			if (isNew)
			{
				release = new WorkflowProtectedRelease
				{
					WorkflowProtectedReleaseId = Guid.NewGuid().ToString(),
					WorkflowId = workflowId,
					DepartmentId = departmentId,
					State = (int)ProtectedReleaseState.Draft,
					DestinationScheme = "https",
					CreatedOn = now,
					Version = 1
				};
			}

			var changed = isNew ||
				!fields.SequenceEqual(release.GetAllowedFieldIds(), StringComparer.Ordinal) ||
				release.RecipientType != draft.RecipientType ||
				!string.Equals(release.RecipientName ?? string.Empty, Trim(draft.RecipientName, 200) ?? string.Empty, StringComparison.Ordinal) ||
				!string.Equals(release.Purpose ?? string.Empty, Trim(draft.Purpose, 500) ?? string.Empty, StringComparison.Ordinal);

			if (!changed)
				return ProtectedWorkflowCommandResult.Ok(release);

			release.SetAllowedFieldIds(fields);
			release.RecipientType = draft.RecipientType;
			release.RecipientName = Trim(draft.RecipientName, 200);
			release.Purpose = Trim(draft.Purpose, 500);
			release.UpdatedOn = now;

			// Changing what is released (or to whom, or why) on an approved or pending release is a change that
			// needs re-approval; nothing opens until an administrator requests and approves again.
			var reapproval = !isNew && (release.ReleaseState == ProtectedReleaseState.Active || release.ReleaseState == ProtectedReleaseState.PendingApproval);
			if (reapproval)
			{
				release.State = (int)ProtectedReleaseState.PendingApproval;
				release.SuspendedReason = ProtectedWorkflowSuspendReasons.ConfigChanged;
			}

			if (isNew)
				await _releases.InsertAsync(release, cancellationToken);
			else if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConcurrentChange);

			if (reapproval)
				await RecordAdminEventAsync(departmentId, ProtectedWorkflowAdminEventTypes.ReleaseSuspended, actor.UserId, workflowId,
					release.WorkflowProtectedReleaseId, ProtectedWorkflowSuspendReasons.ConfigChanged, cancellationToken);

			return ProtectedWorkflowCommandResult.Ok(release);
		}

		public async Task<ProtectedWorkflowCommandResult> RequestApprovalAsync(int departmentId, string workflowId, bool attested,
			string acknowledgedVersion, string reviewedStepsFingerprint, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive = null,
			CancellationToken cancellationToken = default)
		{
			var release = await _releases.GetLatestByWorkflowIdAsync(workflowId);
			if (release == null || release.DepartmentId != departmentId ||
				release.ReleaseState == ProtectedReleaseState.Revoked || release.ReleaseState == ProtectedReleaseState.Active)
			{
				var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: true);
				return ProtectedWorkflowCommandResult.Fail(denied ?? ProtectedWorkflowErrorCodes.InvalidState);
			}

			return await SubmitAsync(departmentId, release, attested, acknowledgedVersion, reviewedStepsFingerprint, actor, sensitive, renewal: false, cancellationToken);
		}

		public async Task<ProtectedWorkflowCommandResult> RenewAsync(int departmentId, string releaseId, bool attested,
			string acknowledgedVersion, string reviewedFingerprint, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive = null,
			CancellationToken cancellationToken = default)
		{
			var release = await LoadReleaseAsync(departmentId, releaseId);
			if (release == null)
				return ProtectedWorkflowCommandResult.Fail(await AuthorizeAsync(departmentId, actor, true) ?? ProtectedWorkflowErrorCodes.NotFound);

			if (release.ReleaseState != ProtectedReleaseState.Active && release.ReleaseState != ProtectedReleaseState.Expired)
				return ProtectedWorkflowCommandResult.Fail(await AuthorizeAsync(departmentId, actor, true) ?? ProtectedWorkflowErrorCodes.InvalidState);

			return await SubmitAsync(departmentId, release, attested, acknowledgedVersion, reviewedFingerprint, actor, sensitive, renewal: true, cancellationToken);
		}

		/// <summary>
		/// The request path shared by first requests, re-requests after a change, suspension or expiry, and renewals.
		/// Re-validates everything from fresh reads, pins host, credential and token host, binds the fingerprint and
		/// records the attestation. Single approver: Active now. Two-person rule: waits for a different administrator
		/// (an Active release being renewed keeps sending until its current ExpiresOn while the renewal waits).
		/// </summary>
		/// <param name="reviewed">What the administrator was shown: the StepsFingerprint for a request, the release's
		/// ConfigFingerprint for a renewal. A mismatch means the configuration changed since they looked, and nothing is written.</param>
		private async Task<ProtectedWorkflowCommandResult> SubmitAsync(int departmentId, WorkflowProtectedRelease release, bool attested,
			string acknowledgedVersion, string reviewed, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive, bool renewal,
			CancellationToken cancellationToken)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: true);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);
			if (!attested)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AttestationRequired);
			if (!string.Equals(acknowledgedVersion, ProtectedWorkflowDefaults.WarningTextVersion, StringComparison.Ordinal))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AckVersionMismatch);

			var settings = await GetDepartmentSettingsAsync(departmentId, bypassCache: true);
			if (!settings.AdpActive)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AdpNotEnabled);
			if (!settings.Enabled)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.DepartmentDisabled);

			var workflow = await LoadWorkflowAsync(departmentId, release.WorkflowId);
			if (workflow == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);

			var fields = release.GetAllowedFieldIds().ToList();
			var customFields = await GetCallCustomFieldsAsync(departmentId);
			var fieldError = ValidateFieldIds(workflow.TriggerEventType, fields, requireAny: true, customFields);
			if (fieldError != null)
				return ProtectedWorkflowCommandResult.Fail(fieldError);
			if (!Enum.IsDefined(typeof(ProtectedReleaseRecipientType), release.RecipientType) || string.IsNullOrWhiteSpace(release.RecipientName))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.RecipientRequired);
			if (string.IsNullOrWhiteSpace(release.Purpose))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.PurposeRequired);

			var steps = await LoadStepsAsync(release.WorkflowId);
			var validation = Validate(workflow, steps, await LoadCredentialTypesAsync(departmentId));
			var pins = await ResolvePinsAsync(departmentId, validation);
			var tokenHost = pins?.TokenHost;
			if (!validation.IsValid)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ValidationFailed, validation.Errors);

			// A step that writes response values back into the subject identifiers merges into the stored set, which
			// means reading it: only a release that already reads the subject identifiers may do that.
			var plan = ProtectedWorkflowFieldCatalog.Plan(workflow.TriggerEventType, fields);
			if (validation.CapturesResponse && !plan.ReadsSubjectIdentifiers)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.CaptureRequiresSubjectIdentifiers);

			// Restricted and 42 CFR Part 2 custom fields need their own, current attestation.
			var sensitivities = Sensitivities(fields, customFields);
			var needsRestricted = sensitivities.Values.Contains((int)UdfFieldSensitivity.Restricted);
			var needsPart2 = sensitivities.Values.Contains((int)UdfFieldSensitivity.Part2);
			if (needsRestricted && !(sensitive?.RestrictedValid ?? false))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.RestrictedAttestationRequired);
			if (needsPart2 && !(sensitive?.Part2Valid ?? false))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.Part2AttestationRequired);

			// Bind the request to what the administrator actually reviewed.
			var shown = renewal ? release.ConfigFingerprint : ComputeStepsFingerprint(workflow, steps, validation.DestinationHost, tokenHost, pins?.AuthMethod);
			if (string.IsNullOrEmpty(reviewed) || !string.Equals(reviewed, shown, StringComparison.Ordinal))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConfigChanged);

			var now = DateTime.UtcNow;
			var wasActive = release.ReleaseState == ProtectedReleaseState.Active;
			var approvedFingerprint = release.ConfigFingerprint;
			release.DestinationScheme = "https";
			release.DestinationHost = validation.DestinationHost;
			release.WorkflowCredentialId = validation.WorkflowCredentialId;
			release.TokenHost = tokenHost;
			release.AuthMethod = pins?.AuthMethod;
			release.AllowsRestricted = needsRestricted;
			release.RestrictedAckVersion = needsRestricted ? ProtectedWorkflowDefaults.RestrictedAttestationVersion : null;
			release.RestrictedAckByUserId = needsRestricted ? actor.UserId : null;
			release.AllowsPart2 = needsPart2;
			release.Part2AckVersion = needsPart2 ? ProtectedWorkflowDefaults.Part2AttestationVersion : null;
			release.Part2AckByUserId = needsPart2 ? actor.UserId : null;
			release.ConfigFingerprint = ComputeFingerprint(workflow, steps, release, customFields);

			// A renewal only extends what was already approved. If the configuration moved underneath an Active release
			// (a save hook that did not run), keeping it Active would let one administrator re-bind the approval to a
			// configuration nobody else has seen: it goes back through a full approval instead.
			if (renewal && wasActive && !string.Equals(approvedFingerprint, release.ConfigFingerprint, StringComparison.Ordinal))
				wasActive = false;
			release.AckVersion = ProtectedWorkflowDefaults.WarningTextVersion;
			release.RequestedByUserId = actor.UserId;
			release.RequestedOn = now;
			release.UpdatedOn = now;

			if (settings.RequireSecondApprover)
			{
				if (!(renewal && wasActive))
				{
					release.State = (int)ProtectedReleaseState.PendingApproval;
					release.SuspendedReason = null;
					release.ApprovedByUserId = null;
					release.ApprovedOn = null;
					release.ExpiresOn = null;
				}
			}
			else
			{
				Activate(release, actor.UserId, now);
			}

			if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConcurrentChange);

			await RecordAdminEventAsync(departmentId, ProtectedWorkflowAdminEventTypes.ReleaseRequested, actor.UserId, release.WorkflowId,
				release.WorkflowProtectedReleaseId, DescribeRequest(release, renewal), cancellationToken);
			if (!settings.RequireSecondApprover)
				await RecordAdminEventAsync(departmentId, ProtectedWorkflowAdminEventTypes.ReleaseApproved, actor.UserId, release.WorkflowId,
					release.WorkflowProtectedReleaseId, $"expires={release.ExpiresOn:yyyy-MM-dd};single_approver", cancellationToken);

			return ProtectedWorkflowCommandResult.Ok(release);
		}

		public async Task<ProtectedWorkflowCommandResult> ApproveAsync(int departmentId, string releaseId, bool attested,
			string acknowledgedVersion, string reviewedFingerprint, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive = null,
			CancellationToken cancellationToken = default)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: true);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);
			if (!attested)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AttestationRequired);
			if (!string.Equals(acknowledgedVersion, ProtectedWorkflowDefaults.WarningTextVersion, StringComparison.Ordinal))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AckVersionMismatch);

			var release = await LoadReleaseAsync(departmentId, releaseId);
			if (release == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);

			// A pending first request, or a renewal of an Active release that is waiting for its second approver.
			var pendingRequest = release.ReleaseState == ProtectedReleaseState.PendingApproval && release.SuspendedReason == null;
			var pendingRenewal = release.ReleaseState == ProtectedReleaseState.Active && release.RequestedOn.HasValue &&
				(!release.ApprovedOn.HasValue || release.RequestedOn.Value > release.ApprovedOn.Value);
			if (!pendingRequest && !pendingRenewal)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.InvalidState);

			// Two-person rule: whoever asked can never be the one who approves.
			if (string.Equals(release.RequestedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.SelfApproval);

			// The approval binds to the request the approver was shown; a re-request in between changes the fingerprint.
			if (string.IsNullOrEmpty(reviewedFingerprint) || !string.Equals(reviewedFingerprint, release.ConfigFingerprint, StringComparison.Ordinal))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConfigChanged);

			// The approver makes the same restricted / Part 2 attestations the requester made.
			if (release.AllowsRestricted && !(sensitive?.RestrictedValid ?? false))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.RestrictedAttestationRequired);
			if (release.AllowsPart2 && !(sensitive?.Part2Valid ?? false))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.Part2AttestationRequired);

			var settings = await GetDepartmentSettingsAsync(departmentId, bypassCache: true);
			if (!settings.AdpActive)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.AdpNotEnabled);
			if (!settings.Enabled)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.DepartmentDisabled);

			// The approver approves exactly what was requested: re-validate and re-fingerprint from fresh reads.
			var workflow = await LoadWorkflowAsync(departmentId, release.WorkflowId);
			if (workflow == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);
			var steps = await LoadStepsAsync(release.WorkflowId);
			var validation = Validate(workflow, steps, await LoadCredentialTypesAsync(departmentId));
			var pins = await ResolvePinsAsync(departmentId, validation);
			if (!validation.IsValid)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ValidationFailed, validation.Errors);

			if (!string.Equals(validation.DestinationHost, release.DestinationHost, StringComparison.Ordinal) ||
				!string.Equals(validation.WorkflowCredentialId, release.WorkflowCredentialId, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(pins?.TokenHost, release.TokenHost, StringComparison.Ordinal) ||
				!string.Equals(pins?.AuthMethod, release.AuthMethod, StringComparison.Ordinal) ||
				!string.Equals(await ComputeCurrentFingerprintAsync(workflow, steps, release), release.ConfigFingerprint, StringComparison.Ordinal))
			{
				if (pendingRequest)
				{
					release.State = (int)ProtectedReleaseState.PendingApproval;
					release.SuspendedReason = ProtectedWorkflowSuspendReasons.ConfigChanged;
					release.UpdatedOn = DateTime.UtcNow;
					await _releases.TryUpdateAsync(release, cancellationToken);
				}
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConfigChanged);
			}

			Activate(release, actor.UserId, DateTime.UtcNow);
			if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConcurrentChange);
			await RecordAdminEventAsync(departmentId, ProtectedWorkflowAdminEventTypes.ReleaseApproved, actor.UserId, release.WorkflowId,
				release.WorkflowProtectedReleaseId, $"expires={release.ExpiresOn:yyyy-MM-dd};second_approver", cancellationToken);

			return ProtectedWorkflowCommandResult.Ok(release);
		}

		public async Task<ProtectedWorkflowCommandResult> SuspendAsync(int departmentId, string releaseId, ProtectedWorkflowActor actor,
			CancellationToken cancellationToken = default)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: false);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);

			var release = await LoadReleaseAsync(departmentId, releaseId);
			if (release == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);
			if (release.ReleaseState != ProtectedReleaseState.Active && release.ReleaseState != ProtectedReleaseState.PendingApproval)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.InvalidState);

			return await SuspendInternalAsync(release, ProtectedWorkflowSuspendReasons.AdminSuspended, actor.UserId, cancellationToken)
				? ProtectedWorkflowCommandResult.Ok(release)
				: ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConcurrentChange);
		}

		public async Task<ProtectedWorkflowCommandResult> RevokeAsync(int departmentId, string releaseId, ProtectedWorkflowActor actor,
			CancellationToken cancellationToken = default)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: false);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);

			var release = await LoadReleaseAsync(departmentId, releaseId);
			if (release == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);
			if (release.ReleaseState == ProtectedReleaseState.Revoked)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.InvalidState);

			return await RevokeInternalAsync(release, ProtectedWorkflowSuspendReasons.AdminRevoked, actor.UserId, cancellationToken)
				? ProtectedWorkflowCommandResult.Ok(release)
				: ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.ConcurrentChange);
		}

		public async Task<ProtectedWorkflowCommandResult> DiscardDraftAsync(int departmentId, string releaseId, ProtectedWorkflowActor actor,
			CancellationToken cancellationToken = default)
		{
			var denied = await AuthorizeAsync(departmentId, actor, requireStepUp: false);
			if (denied != null)
				return ProtectedWorkflowCommandResult.Fail(denied);

			var release = await LoadReleaseAsync(departmentId, releaseId);
			if (release == null)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.NotFound);

			// Only a release that was never requested can disappear; anything else is part of the audit trail.
			if (release.ReleaseState != ProtectedReleaseState.Draft || release.RequestedOn.HasValue)
				return ProtectedWorkflowCommandResult.Fail(ProtectedWorkflowErrorCodes.InvalidState);

			await _releases.DeleteAsync(release, cancellationToken);
			return ProtectedWorkflowCommandResult.Ok();
		}

		// ── Lifecycle hooks ───────────────────────────────────────────────────────────────────────────

		public async Task OnWorkflowConfigurationChangedAsync(string workflowId, string actorUserId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(workflowId))
				return;

			var release = await _releases.GetLatestByWorkflowIdAsync(workflowId);
			if (release == null)
				return;

			// Only approved or pending releases carry a fingerprint that a change can invalidate; a draft, suspended
			// or expired release is re-validated and re-fingerprinted when it is next requested anyway.
			var active = release.ReleaseState == ProtectedReleaseState.Active;
			var pending = release.ReleaseState == ProtectedReleaseState.PendingApproval && release.SuspendedReason == null;
			if (!active && !pending)
				return;

			var workflow = await _workflows.GetByIdAsync(workflowId);
			if (workflow == null)
				return;

			var current = await ComputeCurrentFingerprintAsync(workflow, await LoadStepsAsync(workflowId), release);
			if (string.Equals(current, release.ConfigFingerprint, StringComparison.Ordinal))
				return;

			release.State = (int)ProtectedReleaseState.PendingApproval;
			release.SuspendedReason = ProtectedWorkflowSuspendReasons.ConfigChanged;
			release.UpdatedOn = DateTime.UtcNow;
			if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return; // someone else moved it first; the runtime re-checks the fingerprint on every send anyway
			await RecordAdminEventAsync(release.DepartmentId, ProtectedWorkflowAdminEventTypes.ReleaseSuspended,
				string.IsNullOrWhiteSpace(actorUserId) ? SystemActor : actorUserId, workflowId, release.WorkflowProtectedReleaseId,
				ProtectedWorkflowSuspendReasons.ConfigChanged, cancellationToken);
		}

		public async Task OnWorkflowDeletedAsync(Workflow workflow, string actorUserId, CancellationToken cancellationToken = default)
		{
			if (workflow == null)
				return;

			foreach (var release in (await _releases.GetAllByWorkflowIdAsync(workflow.WorkflowId) ?? Enumerable.Empty<WorkflowProtectedRelease>())
				.Where(r => r.ReleaseState != ProtectedReleaseState.Revoked))
			{
				if (release.ReleaseState == ProtectedReleaseState.Draft && !release.RequestedOn.HasValue)
				{
					await _releases.DeleteAsync(release, cancellationToken);
					continue;
				}

				await RevokeInternalAsync(release, ProtectedWorkflowSuspendReasons.WorkflowDeleted,
					string.IsNullOrWhiteSpace(actorUserId) ? SystemActor : actorUserId, cancellationToken);
			}
		}

		public async Task OnCredentialSavedAsync(WorkflowCredential credential, bool typeChanged, bool secretChanged, string previousTokenHost,
			string currentTokenHost, string actorUserId, CancellationToken cancellationToken = default, string previousAuthMethod = null,
			string currentAuthMethod = null, string rotatedKeyId = null)
		{
			if (credential == null || string.IsNullOrWhiteSpace(credential.WorkflowCredentialId))
				return;

			var actor = string.IsNullOrWhiteSpace(actorUserId) ? SystemActor : actorUserId;
			var rotationDetail = rotatedKeyId == null
				? $"credential={credential.WorkflowCredentialId}"
				: $"credential={credential.WorkflowCredentialId};kid={rotatedKeyId}";

			var pinned = (await _releases.GetAllByCredentialIdAsync(credential.WorkflowCredentialId) ?? Enumerable.Empty<WorkflowProtectedRelease>())
				.Where(r => r.DepartmentId == credential.DepartmentId && r.ReleaseState != ProtectedReleaseState.Revoked)
				.ToList();
			if (pinned.Count == 0)
			{
				// A signing key rotation is always on the record for a department using Protected Workflows, even before any
				// release pins the credential: the EHR will have been given the old key.
				if (rotatedKeyId != null && (await GetDepartmentSettingsAsync(credential.DepartmentId, bypassCache: true)).Enabled)
					await RecordAdminEventAsync(credential.DepartmentId, ProtectedWorkflowAdminEventTypes.CredentialRotated, actor, null, null,
						rotationDetail, cancellationToken);
				return;
			}

			var tokenHostChanged = !string.Equals(ProtectedWorkflowFingerprint.NormalizeHost(previousTokenHost),
				ProtectedWorkflowFingerprint.NormalizeHost(currentTokenHost), StringComparison.Ordinal);
			var authMethodChanged = !string.Equals(previousAuthMethod, currentAuthMethod, StringComparison.Ordinal);

			foreach (var release in pinned)
			{
				if (typeChanged || tokenHostChanged || authMethodChanged)
				{
					if (release.ReleaseState == ProtectedReleaseState.Active || release.ReleaseState == ProtectedReleaseState.PendingApproval)
						await SuspendInternalAsync(release, ProtectedWorkflowSuspendReasons.CredentialChanged, actor, cancellationToken);
				}
				else if (secretChanged)
				{
					// The id is pinned, the secret is not: rotation never forces re-approval, but it is on the record.
					await RecordAdminEventAsync(release.DepartmentId, ProtectedWorkflowAdminEventTypes.CredentialRotated, actor, release.WorkflowId,
						release.WorkflowProtectedReleaseId, rotationDetail, cancellationToken);
				}
			}
		}

		public async Task OnCallCustomFieldsChangedAsync(int departmentId, string actorUserId, CancellationToken cancellationToken = default)
		{
			var affected = ((await _releases.GetAllByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<WorkflowProtectedRelease>())
				.Where(r => r.ReleaseState == ProtectedReleaseState.Active ||
					(r.ReleaseState == ProtectedReleaseState.PendingApproval && r.SuspendedReason == null))
				.Where(r => r.GetAllowedFieldIds().Any(id => ProtectedWorkflowFieldCatalog.ParseUdfName(id) != null))
				.Select(r => r.WorkflowId)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			// The fingerprint carries every released custom field's tag: a retag, a removal or a disabled field moves it.
			foreach (var workflowId in affected)
				await OnWorkflowConfigurationChangedAsync(workflowId, actorUserId, cancellationToken);
		}

		public async Task OnCredentialDeletedAsync(WorkflowCredential credential, string actorUserId, CancellationToken cancellationToken = default)
		{
			if (credential == null || string.IsNullOrWhiteSpace(credential.WorkflowCredentialId))
				return;

			foreach (var release in (await _releases.GetAllByCredentialIdAsync(credential.WorkflowCredentialId) ?? Enumerable.Empty<WorkflowProtectedRelease>())
				.Where(r => r.DepartmentId == credential.DepartmentId &&
					(r.ReleaseState == ProtectedReleaseState.Active || r.ReleaseState == ProtectedReleaseState.PendingApproval)))
				await SuspendInternalAsync(release, ProtectedWorkflowSuspendReasons.CredentialChanged,
					string.IsNullOrWhiteSpace(actorUserId) ? SystemActor : actorUserId, cancellationToken);
		}

		public async Task<string> ValidateStepTemplatesAsync(WorkflowStep step, CancellationToken cancellationToken = default)
		{
			if (step == null)
				return null;

			// Branching on plaintext, or putting it in a URL or header, is never allowed — protected or not.
			if (ProtectedWorkflowValidator.ReferencesProtectedNamespace(step.ConditionExpression))
				return ProtectedWorkflowValidator.ProtectedInCondition;
			if (ProtectedWorkflowValidator.ReferencesProtectedNamespace(step.ActionConfig))
				return ProtectedWorkflowValidator.ProtectedInActionConfig;

			if (ProtectedWorkflowValidator.ReferencesProtectedNamespace(step.OutputTemplate))
			{
				var release = string.IsNullOrWhiteSpace(step.WorkflowId) ? null : await _releases.GetLatestByWorkflowIdAsync(step.WorkflowId);
				if (release == null || release.ReleaseState == ProtectedReleaseState.Revoked)
					return ProtectedWorkflowValidator.ProtectedWithoutRelease;
			}

			return null;
		}

		// ── Disclosure chain ──────────────────────────────────────────────────────────────────────────

		public Task<ProtectedWorkflowDisclosure> RecordDisclosureAsync(ProtectedWorkflowDisclosure disclosure, CancellationToken cancellationToken = default)
		{
			if (disclosure == null)
				throw new ArgumentNullException(nameof(disclosure));

			disclosure.RecordType = ProtectedWorkflowRecordTypes.Disclosure;
			disclosure.EventType = null;
			if (disclosure.OccurredOn == default)
				disclosure.OccurredOn = DateTime.UtcNow;
			return _disclosures.AppendAsync(disclosure, cancellationToken);
		}

		public Task<ProtectedWorkflowDisclosure> RecordAdminEventAsync(int departmentId, string eventType, string actorUserId, string workflowId,
			string releaseId, string detail, CancellationToken cancellationToken = default)
		{
			return _disclosures.AppendAsync(new ProtectedWorkflowDisclosure
			{
				DepartmentId = departmentId,
				RecordType = ProtectedWorkflowRecordTypes.AdminEvent,
				EventType = eventType,
				ActorUserId = actorUserId,
				WorkflowId = workflowId,
				WorkflowProtectedReleaseId = releaseId,
				Detail = Trim(detail, 500),
				OccurredOn = DateTime.UtcNow
			}, cancellationToken);
		}

		public async Task<List<ProtectedWorkflowDisclosure>> GetDisclosuresAsync(int departmentId, ProtectedWorkflowDisclosureFilter filter) =>
			(await _disclosures.GetForDepartmentAsync(departmentId, filter))?.ToList() ?? new List<ProtectedWorkflowDisclosure>();

		public async Task<ProtectedWorkflowChainVerification> VerifyChainAsync(int departmentId)
		{
			var chain = (await _disclosures.GetChainForDepartmentAsync(departmentId))?.ToList() ?? new List<ProtectedWorkflowDisclosure>();
			var broken = ProtectedWorkflowDisclosureChain.Verify(chain);
			return new ProtectedWorkflowChainVerification { IsValid = broken == null, RecordCount = chain.Count, FirstInvalidSequence = broken };
		}

		public async Task<string> ExportDisclosuresCsvAsync(int departmentId, ProtectedWorkflowDisclosureFilter filter)
		{
			var rows = await GetDisclosuresAsync(departmentId, filter);
			var csv = new StringBuilder();
			csv.AppendLine("Sequence,OccurredOnUtc,RecordType,EventType,Outcome,IsTest,WorkflowId,WorkflowRunId,WorkflowStepId,ReleaseId,EntityType,EntityId,FieldIds,DestinationHost,ContentType,HttpStatus,PayloadBytes,PayloadSha256,BrokerRequestId,ActorUserId,Detail,CapturedKeys,Hash");
			foreach (var row in rows.OrderBy(r => r.ChainSequence))
			{
				csv.AppendLine(string.Join(",", new[]
				{
					row.ChainSequence.ToString(CultureInfo.InvariantCulture),
					row.OccurredOn.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
					row.RecordType, row.EventType, row.Outcome, row.IsTest ? "true" : "false",
					row.WorkflowId, row.WorkflowRunId, row.WorkflowStepId, row.WorkflowProtectedReleaseId,
					row.EntityType, row.EntityId,
					string.Join(" ", WorkflowProtectedRelease.ParseFieldIds(row.FieldIds)),
					row.DestinationHost, row.ContentType,
					row.HttpStatus?.ToString(CultureInfo.InvariantCulture),
					row.PayloadBytes?.ToString(CultureInfo.InvariantCulture),
					row.PayloadSha256, row.BrokerRequestId, row.ActorUserId, row.Detail,
					string.Join(" ", WorkflowProtectedRelease.ParseFieldIds(row.CapturedKeys)), row.Hash
				}.Select(CsvCell)));
			}

			return csv.ToString();
		}

		// ── Shared transitions ────────────────────────────────────────────────────────────────────────

		public async Task MarkExpiredAsync(WorkflowProtectedRelease release, CancellationToken cancellationToken = default)
		{
			if (release == null || release.ReleaseState != ProtectedReleaseState.Active)
				return;

			release.State = (int)ProtectedReleaseState.Expired;
			release.UpdatedOn = DateTime.UtcNow;
			if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return;
			await RecordAdminEventAsync(release.DepartmentId, ProtectedWorkflowAdminEventTypes.ReleaseExpired, SystemActor, release.WorkflowId,
				release.WorkflowProtectedReleaseId, $"expires={release.ExpiresOn:yyyy-MM-dd}", cancellationToken);
		}

		public async Task<int> RevokeAllForDepartmentAsync(int departmentId, string reason, string actorUserId, CancellationToken cancellationToken = default)
		{
			var revoked = 0;
			foreach (var release in (await _releases.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<WorkflowProtectedRelease>())
				.Where(r => r.ReleaseState != ProtectedReleaseState.Revoked))
			{
				if (await RevokeInternalAsync(release, reason, string.IsNullOrWhiteSpace(actorUserId) ? SystemActor : actorUserId, cancellationToken))
					revoked++;
			}

			return revoked;
		}

		public async Task<ProtectedWorkflowSweepResult> RunSweepAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedWorkflowSweepResult();
			var open = (await _releases.GetAllByStatesAsync(new[]
			{
				ProtectedReleaseState.Draft, ProtectedReleaseState.PendingApproval, ProtectedReleaseState.Active,
				ProtectedReleaseState.Suspended, ProtectedReleaseState.Expired
			}))?.ToList() ?? new List<WorkflowProtectedRelease>();

			foreach (var department in open.GroupBy(r => r.DepartmentId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var policy = await _dataProtection.GetPolicyByDepartmentIdAsync(department.Key, bypassCache: true);
					if (IsOffboardingOrDisabled(policy))
					{
						result.Revoked += await RevokeAllForDepartmentAsync(department.Key, ProtectedWorkflowSuspendReasons.AdpOffboarding, SystemActor, cancellationToken);
						continue;
					}

					var egress = await _dataProtection.GetEgressPolicyByDepartmentIdAsync(department.Key, bypassCache: true);
					foreach (var release in department)
					{
						if (!egress.ProtectedWorkflowsEnabled &&
							(release.ReleaseState == ProtectedReleaseState.Active || release.ReleaseState == ProtectedReleaseState.PendingApproval))
						{
							if (await SuspendInternalAsync(release, ProtectedWorkflowSuspendReasons.DepartmentDisabled, SystemActor, cancellationToken))
								result.Suspended++;
							continue;
						}

						if (release.ReleaseState != ProtectedReleaseState.Active || !release.ExpiresOn.HasValue)
							continue;

						if (release.ExpiresOn.Value <= utcNow)
						{
							var version = release.Version;
							await MarkExpiredAsync(release, cancellationToken);
							if (release.Version != version)
							{
								result.Expired++;
								await NotifyAdminsAsync(release.DepartmentId, release.WorkflowId, "NoticeExpiredBody", null, cancellationToken);
							}
							continue;
						}

						var threshold = DueNoticeThreshold(release, utcNow);
						if (threshold.HasValue)
						{
							// Claim the notice first (a conditional write on the version the sweep read): a release that
							// was revoked or suspended in the meantime is never written back as Active, and no notice goes out.
							release.ExpiryNoticeSentDays = threshold;
							release.UpdatedOn = utcNow;
							if (!await _releases.TryUpdateAsync(release, cancellationToken))
								continue;
							await NotifyAdminsAsync(release.DepartmentId, release.WorkflowId, "NoticeExpiringBody", release.ExpiresOn, cancellationToken);
							result.NoticesSent++;
						}
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Failed++;
					Logging.LogError($"Protected workflow sweep failed for department {department.Key}: {ex.GetType().FullName}.");
				}
			}

			return result;
		}

		public async Task NotifyFinalFailureAsync(int departmentId, Workflow workflow, string workflowRunId, string errorCode,
			CancellationToken cancellationToken = default)
		{
			if (workflow == null)
				return;

			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
				{
					var profile = await _profiles.GetProfileByUserIdAsync(admin.UserId);
					var message = ProtectedWorkflowsResources.Get("NoticeFailureBody", profile?.Language, workflow.Name, workflowRunId, errorCode);
					var title = ProtectedWorkflowsResources.Get("NoticeFailureTitle", profile?.Language);
					await _communication.Value.SendNotificationAsync(admin.UserId, departmentId, message, null, department, title, profile);
				}
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow failure notice could not be sent for department {departmentId}: {ex.GetType().FullName}.");
			}
		}

		public async Task<string> GetCredentialTokenHostAsync(int departmentId, string workflowCredentialId) =>
			(await GetCredentialPinsAsync(departmentId, workflowCredentialId))?.TokenHost;

		public async Task<ProtectedCredentialPins> GetCredentialPinsAsync(int departmentId, string workflowCredentialId)
		{
			if (string.IsNullOrWhiteSpace(workflowCredentialId))
				return null;

			var credential = await _credentials.GetByIdAsync(workflowCredentialId);
			if (credential == null || credential.DepartmentId != departmentId ||
				credential.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials)
				return null;

			var department = await _departments.GetDepartmentByIdAsync(departmentId);
			string json;
			try
			{
				json = _encryption.DecryptForDepartment(credential.EncryptedData, departmentId, department?.Code ?? string.Empty);
			}
			catch (Exception)
			{
				return new ProtectedCredentialPins();
			}

			return ReadPins(json);
		}

		/// <summary>The token host, client authentication and signing-key presence of an OAuth2 credential's JSON.</summary>
		public static ProtectedCredentialPins ReadPins(string credentialJson)
		{
			var authMethod = WorkflowJwtKeys.ClientSecret;
			var hasKey = false;
			try
			{
				var json = string.IsNullOrWhiteSpace(credentialJson) ? null : JObject.Parse(credentialJson);
				authMethod = WorkflowJwtKeys.NormalizeAuthMethod((string)json?.GetValue("authMethod", StringComparison.OrdinalIgnoreCase));
				var keys = json?.GetValue("signingKeys", StringComparison.OrdinalIgnoreCase)?.ToObject<List<WorkflowSigningKey>>();
				hasKey = WorkflowJwtKeys.Current(keys) != null;
			}
			catch (JsonException)
			{
			}

			return new ProtectedCredentialPins { TokenHost = ReadTokenHost(credentialJson), AuthMethod = authMethod, HasSigningKey = hasKey };
		}

		/// <summary>The literal https host of an OAuth2 credential's tokenUrl, or null.</summary>
		public static string ReadTokenHost(string credentialJson)
		{
			if (string.IsNullOrWhiteSpace(credentialJson))
				return null;
			try
			{
				var token = JObject.Parse(credentialJson).GetValue("tokenUrl", StringComparison.OrdinalIgnoreCase);
				var url = token?.Type == JTokenType.String ? (string)token : null;
				return ProtectedWorkflowValidator.TryParseLiteralHttpsHost(url, out var host, out _) ? host : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		// ── Helpers ───────────────────────────────────────────────────────────────────────────────────

		private static void Activate(WorkflowProtectedRelease release, string approverUserId, DateTime now)
		{
			release.State = (int)ProtectedReleaseState.Active;
			release.SuspendedReason = null;
			release.ApprovedByUserId = approverUserId;
			release.ApprovedOn = now;
			release.ExpiresOn = now.AddDays(Math.Max(1, DataProtectionConfig.ProtectedWorkflowReleaseLifetimeDays));
			release.ExpiryNoticeSentDays = null;
			release.UpdatedOn = now;
		}

		private async Task<bool> SuspendInternalAsync(WorkflowProtectedRelease release, string reason, string actorUserId, CancellationToken cancellationToken)
		{
			release.State = (int)ProtectedReleaseState.Suspended;
			release.SuspendedReason = reason;
			release.UpdatedOn = DateTime.UtcNow;
			if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return false;
			await RecordAdminEventAsync(release.DepartmentId, ProtectedWorkflowAdminEventTypes.ReleaseSuspended, actorUserId, release.WorkflowId,
				release.WorkflowProtectedReleaseId, reason, cancellationToken);
			return true;
		}

		private async Task<bool> RevokeInternalAsync(WorkflowProtectedRelease release, string reason, string actorUserId, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			release.State = (int)ProtectedReleaseState.Revoked;
			release.SuspendedReason = reason;
			release.RevokedByUserId = actorUserId;
			release.RevokedOn = now;
			release.UpdatedOn = now;
			if (!await _releases.TryUpdateAsync(release, cancellationToken))
				return false;
			await RecordAdminEventAsync(release.DepartmentId, ProtectedWorkflowAdminEventTypes.ReleaseRevoked, actorUserId, release.WorkflowId,
				release.WorkflowProtectedReleaseId, reason, cancellationToken);
			return true;
		}

		/// <summary>
		/// The fingerprint of the workflow as saved now, against the release's field list, pinned hosts, OAuth2 method and
		/// attestations. <paramref name="callCustomFields"/> are the department's current call custom fields: each released
		/// custom field's sensitivity tag (or its absence) is an input.
		/// </summary>
		public static string ComputeFingerprint(Workflow workflow, IEnumerable<WorkflowStep> steps, WorkflowProtectedRelease release,
			IReadOnlyList<UdfField> callCustomFields = null) =>
			ProtectedWorkflowFingerprint.Compute(workflow.TriggerEventType, steps, release.GetAllowedFieldIds(), release.DestinationHost, release.TokenHost,
				new ProtectedFingerprintExtras
				{
					AuthMethod = release.AuthMethod,
					AllowsRestricted = release.AllowsRestricted,
					AllowsPart2 = release.AllowsPart2,
					FieldSensitivities = Sensitivities(release.GetAllowedFieldIds(), callCustomFields)
				});

		/// <summary>The configuration an administrator reviews before requesting: steps, host, token host and OAuth2 method, no field list.</summary>
		public static string ComputeStepsFingerprint(Workflow workflow, IEnumerable<WorkflowStep> steps, string destinationHost, string tokenHost,
			string authMethod = null) =>
			ProtectedWorkflowFingerprint.Compute(workflow.TriggerEventType, steps, Array.Empty<string>(), destinationHost, tokenHost,
				new ProtectedFingerprintExtras { AuthMethod = authMethod });

		/// <summary>calls.udf#name -> the field's current sensitivity, or -1 when it no longer exists or is disabled.</summary>
		public static Dictionary<string, int> Sensitivities(IEnumerable<string> fieldIds, IReadOnlyList<UdfField> callCustomFields)
		{
			var byName = (callCustomFields ?? Array.Empty<UdfField>())
				.Where(f => f != null && f.IsEnabled && !string.IsNullOrWhiteSpace(f.Name))
				.GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Sensitivity, StringComparer.OrdinalIgnoreCase);

			var result = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var id in WorkflowProtectedRelease.NormalizeFieldIds(fieldIds))
			{
				var name = ProtectedWorkflowFieldCatalog.ParseUdfName(id);
				if (name != null)
					result[id] = byName.TryGetValue(name, out var sensitivity) ? sensitivity : -1;
			}

			return result;
		}

		/// <summary>True when ADP is on its way out (or gone): the department's releases must be revoked, not merely blocked.</summary>
		public static bool IsOffboardingOrDisabled(DepartmentDataProtectionPolicy policy)
		{
			var state = policy == null ? DepartmentDataProtectionState.Disabled : (DepartmentDataProtectionState)policy.State;
			switch (state)
			{
				case DepartmentDataProtectionState.OffboardingScheduled:
				case DepartmentDataProtectionState.DisableRequested:
				case DepartmentDataProtectionState.Decrypting:
				case DepartmentDataProtectionState.Disabled:
					return true;
				case DepartmentDataProtectionState.Verifying:
					return policy?.ActiveMigrationKind == (int)DepartmentDataProtectionMigrationKind.Offboarding;
				default:
					return false;
			}
		}

		/// <summary>The notice threshold (days) now due for an Active release, or null. Each threshold is sent once per ExpiresOn.</summary>
		public static int? DueNoticeThreshold(WorkflowProtectedRelease release, DateTime utcNow)
		{
			if (release?.ExpiresOn == null)
				return null;

			var remaining = (release.ExpiresOn.Value - utcNow).TotalDays;
			var thresholds = (DataProtectionConfig.ProtectedWorkflowExpiryNoticeDays ?? string.Empty)
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : -1)
				.Where(d => d > 0)
				.OrderBy(d => d)
				.ToList();

			foreach (var threshold in thresholds)
			{
				if (remaining <= threshold && (!release.ExpiryNoticeSentDays.HasValue || threshold < release.ExpiryNoticeSentDays.Value))
					return threshold;
			}

			return null;
		}

		private async Task NotifyAdminsAsync(int departmentId, string workflowId, string bodyKey, DateTime? expiresOn, CancellationToken cancellationToken)
		{
			try
			{
				var workflow = await _workflows.GetByIdAsync(workflowId);
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
				{
					var profile = await _profiles.GetProfileByUserIdAsync(admin.UserId);
					var message = ProtectedWorkflowsResources.Get(bodyKey, profile?.Language, workflow?.Name ?? workflowId,
						expiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
					var title = ProtectedWorkflowsResources.Get("NoticeExpiryTitle", profile?.Language);
					await _communication.Value.SendNotificationAsync(admin.UserId, departmentId, message, null, department, title, profile);
				}
			}
			catch (Exception ex)
			{
				Logging.LogError($"Protected workflow expiry notice could not be sent for department {departmentId}: {ex.GetType().FullName}.");
			}
		}

		private async Task<Workflow> LoadWorkflowAsync(int departmentId, string workflowId)
		{
			if (string.IsNullOrWhiteSpace(workflowId))
				return null;
			var workflow = await _workflows.GetByIdAsync(workflowId);
			return workflow != null && workflow.DepartmentId == departmentId ? workflow : null;
		}

		private async Task<WorkflowProtectedRelease> LoadReleaseAsync(int departmentId, string releaseId)
		{
			if (string.IsNullOrWhiteSpace(releaseId))
				return null;
			var release = await _releases.GetByIdAsync(releaseId);
			return release != null && release.DepartmentId == departmentId ? release : null;
		}

		private async Task<List<WorkflowStep>> LoadStepsAsync(string workflowId) =>
			(await _steps.GetAllByWorkflowIdAsync(workflowId))?.ToList() ?? new List<WorkflowStep>();

		private async Task<IReadOnlyDictionary<string, int>> LoadCredentialTypesAsync(int departmentId) =>
			((await _credentials.GetAllByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<WorkflowCredential>())
				.Where(c => !string.IsNullOrWhiteSpace(c.WorkflowCredentialId))
				.GroupBy(c => c.WorkflowCredentialId.Trim(), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().CredentialType, StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Resolves (and validates) the pinned OAuth2 token host and client authentication; adds token_url_invalid when an
		/// OAuth2 credential has none, and signing_key_missing when a private_key_jwt credential has no current key.
		/// </summary>
		private async Task<ProtectedCredentialPins> ResolvePinsAsync(int departmentId, ProtectedWorkflowValidationResult validation)
		{
			if (validation?.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials)
				return null;

			var pins = await GetCredentialPinsAsync(departmentId, validation.WorkflowCredentialId) ?? new ProtectedCredentialPins();
			if (pins.TokenHost == null)
				validation.Add(ProtectedWorkflowValidator.TokenUrlInvalid);
			if (pins.AuthMethod == WorkflowJwtKeys.PrivateKeyJwt && !pins.HasSigningKey)
				validation.Add(ProtectedWorkflowValidator.SigningKeyMissing);
			return pins;
		}

		private static ProtectedWorkflowValidationResult Validate(Workflow workflow, IEnumerable<WorkflowStep> steps, IReadOnlyDictionary<string, int> credentialTypes) =>
			ProtectedWorkflowValidator.Validate(workflow.TriggerEventType, steps, credentialTypes,
				DataProtectionConfig.ProtectedWorkflowAllowHttpBasicCredentials, DataProtectionConfig.ProtectedWorkflowMaxCaptureKeys);

		/// <summary>
		/// Field ids: known call columns, calls.subjectidentifiers whole OR per key (never both), and custom fields that exist
		/// (enabled) in the department's current call custom field definition.
		/// </summary>
		private static string ValidateFieldIds(int triggerEventType, IReadOnlyCollection<string> fields, bool requireAny, IReadOnlyList<UdfField> callCustomFields)
		{
			if (requireAny && fields.Count == 0)
				return ProtectedWorkflowErrorCodes.NoFields;
			if (fields.Count > Math.Max(1, DataProtectionConfig.ProtectedWorkflowMaxFieldsPerRelease))
				return ProtectedWorkflowErrorCodes.TooManyFields;

			var plan = ProtectedWorkflowFieldCatalog.Plan(triggerEventType, fields);
			if (plan.Unknown.Count > 0)
				return ProtectedWorkflowErrorCodes.UnknownField;
			if (plan.HasConflict)
				return ProtectedWorkflowErrorCodes.FieldConflict;
			if (Sensitivities(fields, callCustomFields).Values.Any(s => s < 0))
				return ProtectedWorkflowErrorCodes.UnknownField;
			return null;
		}

		private static string DescribeRequest(WorkflowProtectedRelease release, bool renewal) =>
			$"{(renewal ? "renewal;" : string.Empty)}fields={string.Join(",", release.GetAllowedFieldIds())};host={release.DestinationHost};" +
			$"recipient_type={(ProtectedReleaseRecipientType)release.RecipientType};ack={release.AckVersion}" +
			(release.AuthMethod != null ? $";auth={release.AuthMethod}" : string.Empty) +
			(release.AllowsRestricted ? $";restricted={release.RestrictedAckVersion}" : string.Empty) +
			(release.AllowsPart2 ? $";part2={release.Part2AckVersion}" : string.Empty);

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			value = value.Trim();
			return value.Length > max ? value.Substring(0, max) : value;
		}

		private static string CsvCell(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			// Spreadsheet formula injection: a leading =, +, - or @ is neutralized with a quote.
			if ("=+-@".IndexOf(value[0]) >= 0)
				value = "'" + value;

			return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
				? "\"" + value.Replace("\"", "\"\"") + "\""
				: value;
		}
	}
}
