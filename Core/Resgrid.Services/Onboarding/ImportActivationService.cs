using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Verification, activation and invitations (wizard plan section 15). Imported people stay dormant until an
	/// administrator chooses to invite them; invitations are created pending and sent by worker 47 in provider-safe
	/// batches outside quiet hours. Accepting clears dormancy: an imported person sets their first password on the
	/// account the import created; an existing Resgrid account joins (its membership is created only then). Activation
	/// is a checklist, not a switch: every item can be done separately and the checklist shows what remains.
	/// </summary>
	public class ImportActivationService : IImportActivationService
	{
		private const int MaxSendAttempts = 5;
		private const int QuietStartHour = 21;
		private const int QuietEndHour = 7;

		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportRunsRepository _runs;
		private readonly IImportExternalIdsRepository _externalIds;
		private readonly IImportReconciliationsRepository _reconciliations;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportInvitesRepository _invites;
		private readonly IImportValidationService _validation;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IMemberAccountService _accounts;
		private readonly IEmailService _email;
		private readonly IUsersService _users;
		private readonly ILimitsService _limits;
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentSettingsService _settings;
		private readonly IImportTimelineService _timeline;
		private readonly ImportEffectsRunner _effects;
		private readonly IImportRunQueue _queue;

		public ImportActivationService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportRunsRepository runs,
			IImportExternalIdsRepository externalIds, IImportReconciliationsRepository reconciliations, IImportStagedRowsRepository rows, IImportInvitesRepository invites,
			IImportValidationService validation, IDepartmentsService departments, IUserProfileService profiles, IMemberAccountService accounts, IEmailService email,
			IUsersService users, ILimitsService limits, IDepartmentImportAccessService access, IDepartmentSettingsService settings, IImportTimelineService timeline,
			ImportEffectsRunner effects, IImportRunQueue queue)
		{
			_queue = queue;
			_workspaces = workspaces;
			_datasets = datasets;
			_runs = runs;
			_externalIds = externalIds;
			_reconciliations = reconciliations;
			_rows = rows;
			_invites = invites;
			_validation = validation;
			_departments = departments;
			_profiles = profiles;
			_accounts = accounts;
			_email = email;
			_users = users;
			_limits = limits;
			_access = access;
			_settings = settings;
			_timeline = timeline;
			_effects = effects;
		}

		public async Task<ImportActivationChecklist> GetChecklistAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var checklist = new ImportActivationChecklist { Workspace = workspace, SignedOff = workspace.ActivatedOn.HasValue };
			var commits = await CompletedCommitsAsync(departmentId, workspaceId, cancellationToken);

			foreach (var commit in commits)
			{
				var checks = await _reconciliations.GetForRunAsync(departmentId, commit.Id, cancellationToken);
				checklist.ReconciliationChecks += checks.Count;
				checklist.UnexplainedChecks += checks.Count(c => c.State == (int)ImportReconciliationState.Unexplained);
			}

			var created = await CreatedPeopleAsync(departmentId, commits, cancellationToken);
			var members = (await _departments.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>())
				.Where(m => !m.IsDeleted && created.Contains(m.UserId)).ToList();
			var profiles = await _profiles.GetAllProfilesForDepartmentIncDisabledDeletedAsync(departmentId) ?? new Dictionary<string, UserProfile>();
			string NameOf(string id) => profiles.TryGetValue(id, out var p) ? p.FullName?.AsFirstNameLastName : null;

			checklist.PeopleCreated = members.Count;
			// People imported as inactive (active = false) are created disabled and are never invited (People contract).
			checklist.PeopleDormant = members.Count(m => m.ImportActivationState == (int)ImportActivationState.Dormant && m.IsDisabled != true);
			checklist.PeopleInvited = members.Count(m => m.ImportActivationState == (int)ImportActivationState.Invited);
			checklist.PeopleActivated = members.Count(m => m.ImportActivationState == (int)ImportActivationState.Activated);
			checklist.Dormant = members.Where(m => m.ImportActivationState == (int)ImportActivationState.Dormant && m.IsDisabled != true)
				.Select(m => new ImportActivationPerson { UserId = m.UserId, Name = NameOf(m.UserId), ActivationState = m.ImportActivationState })
				.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

			var adminIds = await FlaggedAdministratorsAsync(departmentId, workspaceId, cancellationToken);
			checklist.PendingAdministrators = members.Where(m => adminIds.Contains(m.UserId) && m.IsAdmin != true)
				.Select(m => new ImportActivationPerson { UserId = m.UserId, Name = NameOf(m.UserId), ActivationState = m.ImportActivationState }).ToList();

			var invites = await _invites.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			checklist.InvitationsPending = invites.Count(i => i.SendState == (int)InviteSendStates.Pending && !i.CompletedOn.HasValue);
			checklist.InvitationsSent = invites.Count(i => i.SendState == (int)InviteSendStates.Sent && !i.CompletedOn.HasValue);
			checklist.InvitationsFailed = invites.Count(i => i.SendState == (int)InviteSendStates.Failed);
			checklist.InvitationsAccepted = invites.Count(i => i.CompletedOn.HasValue);
			var joinInvited = new HashSet<string>(invites.Where(i => i.Kind == (int)InviteKinds.ImportJoin).Select(i => i.EmailAddress), StringComparer.OrdinalIgnoreCase);
			checklist.ExistingAccountsToInvite = (await ExistingAccountEmailsAsync(departmentId, workspaceId, cancellationToken)).Count(e => !joinInvited.Contains(e));

			foreach (var run in (await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(r => r.Kind == (int)ImportRunKind.Rollback))
				checklist.OpenRollbackItems += (await _reconciliations.GetForRunAsync(departmentId, run.Id, cancellationToken))
					.Count(c => c.State == (int)ImportReconciliationState.Unexplained);

			checklist.Items.Add(new ImportActivationItem { Key = "reconcile", Required = true, Done = commits.Count > 0 && checklist.UnexplainedChecks == 0 });
			checklist.Items.Add(new ImportActivationItem { Key = "practice", Required = false, Done = false, Url = "/User/CommunicationTest" });
			checklist.Items.Add(new ImportActivationItem { Key = "administrators", Required = false, Done = checklist.PendingAdministrators.Count == 0 });
			checklist.Items.Add(new ImportActivationItem { Key = "invitations", Required = false, Done = checklist.PeopleDormant == 0 && checklist.ExistingAccountsToInvite == 0 });
			checklist.Items.Add(new ImportActivationItem { Key = "sign-off", Required = true, Done = checklist.SignedOff });
			checklist.CanSignOff = !checklist.SignedOff && commits.Count > 0 && checklist.UnexplainedChecks == 0 &&
				// An activation run is not an obstacle: it is the managed sign-off itself (section 17.8).
				!(await _runs.GetOpenForDepartmentAsync(departmentId, cancellationToken)).Any(r => r.WorkspaceId == workspaceId &&
					r.Kind != (int)ImportRunKind.Analysis && r.Kind != (int)ImportRunKind.Activation);
			return checklist;
		}

		public Task<int> InvitePeopleAsync(int departmentId, string userId, string workspaceId, ImportInvitationRequest request, CancellationToken cancellationToken = default) =>
			InviteAsync(departmentId, ActivationActor.Administrator(userId), workspaceId, request, cancellationToken);

		public Task<int> InviteOnBehalfAsync(int departmentId, ImportAuthority authority, string workspaceId, ImportInvitationRequest request,
			CancellationToken cancellationToken = default) =>
			InviteAsync(departmentId, ActivationActor.Staff(authority), workspaceId, request, cancellationToken);

		private async Task<int> InviteAsync(int departmentId, ActivationActor actor, string workspaceId, ImportInvitationRequest request, CancellationToken cancellationToken)
		{
			await RequireManagerAsync(departmentId, actor.ManagerUserId);
			request ??= new ImportInvitationRequest();
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var created = await CreatedPeopleAsync(departmentId, await CompletedCommitsAsync(departmentId, workspaceId, cancellationToken), cancellationToken);
			var chosen = request.UserIds?.Count > 0 ? new HashSet<string>(request.UserIds.Where(created.Contains), StringComparer.OrdinalIgnoreCase) : created;
			var existing = await _invites.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var now = DateTime.UtcNow;
			var count = 0;

			foreach (var member in (await _departments.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>())
				.Where(m => !m.IsDeleted && m.IsDisabled != true && chosen.Contains(m.UserId) && m.ImportActivationState == (int)ImportActivationState.Dormant))
			{
				if (existing.Any(i => i.Kind == (int)InviteKinds.ImportAccount && string.Equals(i.TargetUserId, member.UserId, StringComparison.OrdinalIgnoreCase) &&
					i.SendState != (int)InviteSendStates.Revoked && !i.CompletedOn.HasValue))
					continue;

				var account = await _accounts.FindByIdAsync(member.UserId);
				if (account == null || string.IsNullOrWhiteSpace(account.Email))
					continue;

				var profile = await _profiles.GetProfileByUserIdAsync(member.UserId, true);
				await _invites.SaveOrUpdateAsync(NewInvite(departmentId, actor.ManagerUserId, workspaceId, account.Email, member.UserId, InviteKinds.ImportAccount,
					request.SendAfter, profile?.Language, now), cancellationToken);

				// Still dormant (Invited is dormant): nothing reaches them but the invitation until they accept.
				member.ImportActivationState = (int)ImportActivationState.Invited;
				await _departments.SaveDepartmentMemberAsync(member, cancellationToken);
				count++;
			}

			if (request.IncludeExistingAccounts)
			{
				foreach (var email in await ExistingAccountEmailsAsync(departmentId, workspaceId, cancellationToken))
				{
					if (existing.Any(i => i.Kind == (int)InviteKinds.ImportJoin && string.Equals(i.EmailAddress, email, StringComparison.OrdinalIgnoreCase) &&
						i.SendState != (int)InviteSendStates.Revoked && !i.CompletedOn.HasValue))
						continue;

					await _invites.SaveOrUpdateAsync(NewInvite(departmentId, actor.ManagerUserId, workspaceId, email, null, InviteKinds.ImportJoin, request.SendAfter, null, now), cancellationToken);
					count++;
				}
			}

			_departments.InvalidateDepartmentUsersInCache(departmentId);
			await _timeline.RecordAsync(departmentId, workspaceId, actor.Kind, actor.Identity, "InvitationsCreated",
				new { count, existingAccounts = request.IncludeExistingAccounts, sendAfter = request.SendAfter }, null, cancellationToken);
			return count;
		}

		private static Invite NewInvite(int departmentId, string sendingUserId, string workspaceId, string email, string targetUserId, InviteKinds kind,
			DateTime? sendAfter, string locale, DateTime now) => new Invite
		{
			DepartmentId = departmentId,
			Code = Guid.NewGuid(),
			EmailAddress = email,
			SendingUserId = sendingUserId,
			SentOn = now,
			Kind = (int)kind,
			TargetUserId = targetUserId,
			WorkspaceId = workspaceId,
			SendState = (int)InviteSendStates.Pending,
			SendAfter = sendAfter,
			Locale = locale
		};

		public Task<int> ApplyAdministratorFlagsAsync(int departmentId, string userId, string workspaceId, IReadOnlyCollection<string> userIds,
			CancellationToken cancellationToken = default) =>
			ApplyAdministratorsAsync(departmentId, ActivationActor.Administrator(userId), workspaceId, userIds, cancellationToken);

		public Task<int> ApplyAdministratorsOnBehalfAsync(int departmentId, ImportAuthority authority, string workspaceId, IReadOnlyCollection<string> userIds,
			CancellationToken cancellationToken = default) =>
			ApplyAdministratorsAsync(departmentId, ActivationActor.Staff(authority), workspaceId, userIds, cancellationToken);

		private async Task<int> ApplyAdministratorsAsync(int departmentId, ActivationActor actor, string workspaceId, IReadOnlyCollection<string> userIds,
			CancellationToken cancellationToken)
		{
			await RequireManagerAsync(departmentId, actor.ManagerUserId);
			var flagged = await FlaggedAdministratorsAsync(departmentId, workspaceId, cancellationToken);
			var applied = 0;
			foreach (var id in (userIds ?? Array.Empty<string>()).Where(flagged.Contains))
			{
				var member = await _departments.GetDepartmentMemberAsync(id, departmentId, true);
				if (member == null || member.IsDeleted || member.IsAdmin == true)
					continue;
				member.IsAdmin = true;
				await _departments.SaveDepartmentMemberAsync(member, cancellationToken);
				applied++;
			}

			if (applied > 0)
			{
				await _effects.RunChunkEffectsAsync(departmentId, ImportEffects.PersonnelVisibility | ImportEffects.DepartmentUsersCache);
				await _timeline.RecordAsync(departmentId, workspaceId, actor.Kind, actor.Identity, "AdministratorsApplied", new { count = applied }, null, cancellationToken);
			}
			return applied;
		}

		public Task<ImportWorkspace> SignOffAsync(int departmentId, string userId, string workspaceId, CancellationToken cancellationToken = default) =>
			SignOffAsync(departmentId, ActivationActor.Administrator(userId), workspaceId, cancellationToken);

		public Task<ImportWorkspace> SignOffOnBehalfAsync(int departmentId, ImportAuthority authority, string workspaceId, CancellationToken cancellationToken = default) =>
			SignOffAsync(departmentId, ActivationActor.Staff(authority), workspaceId, cancellationToken);

		private async Task<ImportWorkspace> SignOffAsync(int departmentId, ActivationActor actor, string workspaceId, CancellationToken cancellationToken)
		{
			await RequireManagerAsync(departmentId, actor.ManagerUserId);
			var checklist = await GetChecklistAsync(departmentId, workspaceId, cancellationToken);
			if (!checklist.CanSignOff)
				throw new DepartmentImportException("import_signoff_not_ready", 409);

			var workspace = checklist.Workspace;
			var config = await _settings.GetDepartmentImportConfigAsync(departmentId);
			var now = DateTime.UtcNow;
			workspace.ActivatedOn = now;
			workspace.Status = (int)ImportWorkspaceStatus.Active;
			workspace.StagingRetentionUntil = now.Add((config ?? new DepartmentImportConfig()).StagingRetentionAfterActivation());
			workspace.UpdatedOn = now;
			workspace.LastActivityOn = now;
			if (!await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken))
				throw new DepartmentImportException("import_workspace_changed", 409);

			_effects.Audit(departmentId, actor.ManagerUserId, AuditLogTypes.DepartmentImportActivated, JsonConvert.SerializeObject(new
			{
				workspaceId,
				checklist.PeopleCreated,
				checklist.PeopleActivated,
				checklist.PeopleInvited,
				checklist.PeopleDormant,
				// "Performed by Resgrid staff (operator), approved by (checker), under (engagement)" (section 17.8).
				staffOperator = actor.Authority?.StaffOperator,
				staffChecker = actor.Authority?.StaffChecker,
				engagement = actor.Authority?.EngagementReference
			}));
			await _timeline.RecordAsync(departmentId, workspaceId, actor.Kind, actor.Identity, "SignedOff", null, null, cancellationToken);
			return workspace;
		}

		public async Task<List<ImportManagedActivationView>> GetManagedRunsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var views = new List<ImportManagedActivationView>();
			foreach (var run in (await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Where(r => r.Kind == (int)ImportRunKind.Activation && (r.Status == (int)ImportRunStatus.AwaitingChecker || r.Status == (int)ImportRunStatus.Queued))
				.OrderBy(r => r.RequestedOn))
			{
				var authority = Authority(run);
				if (authority?.IsStaff != true)
					continue;

				var checkedByStaff = !string.IsNullOrWhiteSpace(authority.StaffChecker);
				views.Add(new ImportManagedActivationView
				{
					Run = run,
					Request = ManagedRequest(run),
					Operator = authority.StaffOperator,
					Checker = authority.StaffChecker,
					EngagementReference = authority.EngagementReference,
					WaitsForStaff = !checkedByStaff,
					WaitsForDepartment = checkedByStaff && run.Status == (int)ImportRunStatus.AwaitingChecker,
					InNoticePeriod = run.Status == (int)ImportRunStatus.Queued && run.NextAttemptOn.HasValue && run.NextAttemptOn.Value > now
				});
			}
			return views;
		}

		public async Task<ImportRun> DecideManagedRunAsync(int departmentId, string userId, string runId, bool approve, CancellationToken cancellationToken = default)
		{
			await RequireManagerAsync(departmentId, userId);
			var run = await _runs.GetByIdAsync(departmentId, runId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);
			var authority = Authority(run);
			var now = DateTime.UtcNow;
			var waiting = run.Status == (int)ImportRunStatus.AwaitingChecker;
			var scheduled = run.Status == (int)ImportRunStatus.Queued && run.NextAttemptOn.HasValue && run.NextAttemptOn.Value > now;
			if (run.Kind != (int)ImportRunKind.Activation || authority?.IsStaff != true || !(waiting || scheduled))
				throw new DepartmentImportException("import_run_not_awaiting_checker", 409);

			var request = ManagedRequest(run);
			if (approve)
			{
				// The department signs after both Resgrid staff members, never instead of the second one.
				if (string.IsNullOrWhiteSpace(authority.StaffChecker))
					throw new DepartmentImportException("import_run_not_awaiting_checker", 409);
				request.DepartmentApprovedOn = now;
				request.DepartmentApprovedBy = userId;
				run.Request = JsonConvert.SerializeObject(request);
				run.Status = (int)ImportRunStatus.Queued;
				// An administrator's approval also ends the notice period (section 17.8: "unless an administrator approves sooner").
				run.NextAttemptOn = null;
			}
			else
			{
				run.Status = (int)ImportRunStatus.Cancelled;
				run.CompletedOn = now;
			}

			run.UpdatedOn = now;
			await _runs.UpdateAsync(run, cancellationToken, true);
			if (approve)
				await _queue.NudgeAsync(run);

			await _timeline.RecordAsync(departmentId, run.WorkspaceId, ImportActorKind.Administrator, userId,
				approve ? "ManagedActivationApproved" : "ManagedActivationCancelled", new { runId, kind = request.Kind.ToString() }, run.CorrelationId, cancellationToken);
			return run;
		}

		internal static ImportManagedActivationRequest ManagedRequest(ImportRun run) =>
			string.IsNullOrWhiteSpace(run?.Request) ? new ImportManagedActivationRequest() : JsonConvert.DeserializeObject<ImportManagedActivationRequest>(run.Request);

		private static ImportAuthority Authority(ImportRun run) =>
			string.IsNullOrWhiteSpace(run?.Authority) ? null : JsonConvert.DeserializeObject<ImportAuthority>(run.Authority);

		public async Task<bool> RevokeInvitationAsync(int departmentId, string userId, int inviteId, CancellationToken cancellationToken = default)
		{
			await RequireManagerAsync(departmentId, userId);
			var invite = await _invites.GetByIdAsync(inviteId);
			if (invite == null || invite.DepartmentId != departmentId || !invite.IsImport || invite.CompletedOn.HasValue)
				return false;

			await _invites.SetSendStateAsync(invite.InviteId, InviteSendStates.Revoked, invite.SendAttempts, null, null, cancellationToken);
			if (invite.Kind == (int)InviteKinds.ImportAccount && invite.TargetUserId != null)
			{
				// Back to Dormant: the person stays imported and can be invited again later.
				var member = await _departments.GetDepartmentMemberAsync(invite.TargetUserId, departmentId, true);
				if (member != null && member.ImportActivationState == (int)ImportActivationState.Invited)
				{
					member.ImportActivationState = (int)ImportActivationState.Dormant;
					await _departments.SaveDepartmentMemberAsync(member, cancellationToken);
				}
			}

			await _timeline.RecordAsync(departmentId, invite.WorkspaceId, ImportActorKind.Administrator, userId, "InvitationRevoked", new { inviteId }, null, cancellationToken);
			return true;
		}

		public async Task<bool> ResendInvitationAsync(int departmentId, string userId, int inviteId, CancellationToken cancellationToken = default)
		{
			await RequireManagerAsync(departmentId, userId);
			var invite = await _invites.GetByIdAsync(inviteId);
			if (invite == null || invite.DepartmentId != departmentId || !invite.IsImport || invite.CompletedOn.HasValue)
				return false;

			// Back in the queue; the batched sender sends it with the next batch.
			await _invites.SetSendStateAsync(invite.InviteId, InviteSendStates.Pending, 0, null, DateTime.UtcNow, cancellationToken);
			await _timeline.RecordAsync(departmentId, invite.WorkspaceId, ImportActorKind.Administrator, userId, "InvitationResent", new { inviteId }, null, cancellationToken);
			return true;
		}

		public async Task<int> SendDueInvitationsAsync(CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var due = await _invites.GetDueAsync(now, MaxSendAttempts, Math.Max(1, ImportConfig.InvitationsPerMinute), cancellationToken);
			var sent = 0;
			var departments = new Dictionary<int, Department>();

			foreach (var invite in due)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!departments.TryGetValue(invite.DepartmentId, out var department))
					departments[invite.DepartmentId] = department = await _departments.GetDepartmentByIdAsync(invite.DepartmentId, false);
				if (department == null)
				{
					await _invites.SetSendStateAsync(invite.InviteId, InviteSendStates.Failed, invite.SendAttempts + 1, null, null, cancellationToken);
					continue;
				}

				// Quiet hours in the department's time zone: the invitation waits for the morning.
				var resume = QuietHoursEnd(now, department.TimeZone);
				if (resume.HasValue)
				{
					await _invites.SetSendStateAsync(invite.InviteId, InviteSendStates.Pending, invite.SendAttempts, null, resume, cancellationToken);
					continue;
				}

				try
				{
					invite.Department = department;
					var sender = _users.GetUserById(invite.SendingUserId);
					var senderProfile = await _profiles.GetProfileByUserIdAsync(invite.SendingUserId);
					var ok = await _email.SendInviteAsync(invite, senderProfile?.FullName?.AsFirstNameLastName ?? department.Name, sender?.Email);
					if (ok)
					{
						await _invites.SetSendStateAsync(invite.InviteId, InviteSendStates.Sent, invite.SendAttempts + 1, DateTime.UtcNow, null, cancellationToken);
						sent++;
						continue;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Department import invitation {invite.InviteId} could not be sent");
				}

				var attempts = invite.SendAttempts + 1;
				await _invites.SetSendStateAsync(invite.InviteId, attempts >= MaxSendAttempts ? InviteSendStates.Failed : InviteSendStates.Pending, attempts, null,
					DateTime.UtcNow.Add(DepartmentImportStateMachine.Backoff(attempts)), cancellationToken);
			}

			if (sent > 0)
				foreach (var group in due.GroupBy(i => i.DepartmentId))
					_effects.Audit(group.Key, null, AuditLogTypes.DepartmentImportInvitationsSent, JsonConvert.SerializeObject(new { count = group.Count() }));

			return sent;
		}

		/// <summary>When the department is in quiet hours, the UTC time they end; otherwise null.</summary>
		public static DateTime? QuietHoursEnd(DateTime utcNow, string timeZone)
		{
			TimeZoneInfo zone;
			try
			{
				// Departments store Windows zone ids; .NET resolves them on every platform (ICU).
				zone = string.IsNullOrWhiteSpace(timeZone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZone);
			}
			catch (Exception)
			{
				zone = TimeZoneInfo.Utc;
			}

			var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
			if (local.Hour >= QuietEndHour && local.Hour < QuietStartHour)
				return null;

			var morning = local.Hour >= QuietStartHour ? local.Date.AddDays(1).AddHours(QuietEndHour) : local.Date.AddHours(QuietEndHour);
			try
			{
				return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(morning, DateTimeKind.Unspecified), zone);
			}
			catch (ArgumentException)
			{
				return utcNow.AddHours(1);
			}
		}

		public async Task<ImportInvitationAcceptance> AcceptAccountInvitationAsync(Guid code, string password, CancellationToken cancellationToken = default)
		{
			var result = new ImportInvitationAcceptance();
			var invite = await _invites.GetByCodeAsync(code, cancellationToken);
			if (invite == null || invite.Kind != (int)InviteKinds.ImportAccount || invite.SendState == (int)InviteSendStates.Revoked || invite.TargetUserId == null)
				return Fail(result, "invite_missing");
			if (invite.CompletedOn.HasValue)
				return Fail(result, "invite_completed");

			result.DepartmentId = invite.DepartmentId;
			var member = await _departments.GetDepartmentMemberAsync(invite.TargetUserId, invite.DepartmentId, true);
			var account = await _accounts.FindByIdAsync(invite.TargetUserId);
			if (member == null || member.IsDeleted || account == null || !string.Equals(account.Email, invite.EmailAddress, StringComparison.OrdinalIgnoreCase))
				return Fail(result, "invite_wrong_account");

			var added = await _accounts.AddPasswordAsync(account, password);
			if (!added.Succeeded)
			{
				result.PasswordErrors = added.Errors;
				return Fail(result, "password_rejected");
			}

			await _accounts.SetEmailConfirmedAsync(account, true);
			member.ImportActivationState = (int)ImportActivationState.Activated;
			await _departments.SaveDepartmentMemberAsync(member, cancellationToken);
			invite.CompletedOn = DateTime.UtcNow;
			invite.CompletedUserId = account.Id;
			await _invites.SaveOrUpdateAsync(invite, cancellationToken);

			_departments.InvalidateDepartmentUsersInCache(invite.DepartmentId);
			_departments.InvalidateDepartmentMemberInCache(account.Id, invite.DepartmentId);
			await _timeline.RecordAsync(invite.DepartmentId, invite.WorkspaceId, ImportActorKind.System, "invitation", "PersonActivated", new { inviteId = invite.InviteId }, null,
				cancellationToken);

			result.Succeeded = true;
			result.UserId = account.Id;
			return result;
		}

		public async Task<ImportInvitationAcceptance> AcceptJoinInvitationAsync(Guid code, string signedInUserId, CancellationToken cancellationToken = default)
		{
			var result = new ImportInvitationAcceptance();
			var invite = await _invites.GetByCodeAsync(code, cancellationToken);
			if (invite == null || invite.Kind != (int)InviteKinds.ImportJoin || invite.SendState == (int)InviteSendStates.Revoked)
				return Fail(result, "invite_missing");
			if (invite.CompletedOn.HasValue)
				return Fail(result, "invite_completed");

			result.DepartmentId = invite.DepartmentId;
			var account = await _accounts.FindByIdAsync(signedInUserId);
			// Only the account the invitation was addressed to can accept it.
			if (account == null || !string.Equals(account.Email, invite.EmailAddress, StringComparison.OrdinalIgnoreCase))
				return Fail(result, "invite_wrong_account");

			var existing = await _departments.GetDepartmentMemberAsync(account.Id, invite.DepartmentId, true);
			if (existing == null || existing.IsDeleted)
			{
				if (!await _limits.CanDepartmentAddNewUserAsync(invite.DepartmentId, true))
					return Fail(result, "department_full");
				if (existing == null)
					await _departments.AddExistingUserAsync(invite.DepartmentId, account.Id, cancellationToken);
				else
					await _departments.ReactivateUserAsync(invite.DepartmentId, account.Id, invite.SendingUserId, cancellationToken);
			}

			invite.CompletedOn = DateTime.UtcNow;
			invite.CompletedUserId = account.Id;
			await _invites.SaveOrUpdateAsync(invite, cancellationToken);
			_departments.InvalidateDepartmentUsersInCache(invite.DepartmentId);
			await _timeline.RecordAsync(invite.DepartmentId, invite.WorkspaceId, ImportActorKind.System, "invitation", "PersonJoined", new { inviteId = invite.InviteId }, null,
				cancellationToken);

			result.Succeeded = true;
			result.UserId = account.Id;
			return result;
		}

		private static ImportInvitationAcceptance Fail(ImportInvitationAcceptance result, string code)
		{
			result.ErrorCode = code;
			return result;
		}

		// ---- helpers ----------------------------------------------------------------------------------------------------

		private async Task<List<ImportRun>> CompletedCommitsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			var runs = await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var rolledBack = new HashSet<string>(runs.Where(r => r.Kind == (int)ImportRunKind.Rollback && r.Status == (int)ImportRunStatus.Completed && r.ParentRunId != null)
				.Select(r => r.ParentRunId), StringComparer.OrdinalIgnoreCase);
			return runs.Where(r => r.Kind == (int)ImportRunKind.Commit && r.Status == (int)ImportRunStatus.Completed && !rolledBack.Contains(r.Id)).ToList();
		}

		/// <summary>People the workspace's commits created (personnel claims written, not linked).</summary>
		private async Task<HashSet<string>> CreatedPeopleAsync(int departmentId, IEnumerable<ImportRun> commits, CancellationToken cancellationToken)
		{
			var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var commit in commits)
				foreach (var entry in await _externalIds.GetForRunAsync(departmentId, commit.Id, cancellationToken))
					if (entry.EntityType == "personnel" && entry.State == (int)ImportExternalIdState.Written && entry.TargetKey != null)
						created.Add(entry.TargetKey);
			return created;
		}

		/// <summary>Imported people whose row said "will become administrator" (applied only after confirmation).</summary>
		private async Task<HashSet<string>> FlaggedAdministratorsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			var flagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var dataset in await PersonnelDatasetsAsync(departmentId, workspaceId, cancellationToken))
				await ForEachRowAsync(dataset, row =>
				{
					if (row.Outcome == ImportRowOutcome.Created && row.TargetKey != null && row.Bool("will_be_admin") == true)
						flagged.Add(row.TargetKey);
				}, cancellationToken);
			return flagged;
		}

		/// <summary>Emails of rows that named an account of other departments only ("invite to join", section 12.3).</summary>
		private async Task<List<string>> ExistingAccountEmailsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var dataset in await PersonnelDatasetsAsync(departmentId, workspaceId, cancellationToken))
				await ForEachRowAsync(dataset, row =>
				{
					if (row.Decision == ImportRowDecision.InviteToJoin && row.Outcome == ImportRowOutcome.Skipped && !string.IsNullOrWhiteSpace(row.Text("email")))
						emails.Add(row.Text("email"));
				}, cancellationToken);
			return emails.ToList();
		}

		private async Task<List<ImportDataset>> PersonnelDatasetsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken) =>
			(await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Where(d => d.EntityKey == "personnel" && (d.Status == (int)ImportDatasetStatus.Imported || d.Status == (int)ImportDatasetStatus.Verified)).ToList();

		private async Task ForEachRowAsync(ImportDataset dataset, Action<ImportCanonicalRow> visit, CancellationToken cancellationToken)
		{
			var after = 0;
			while (true)
			{
				var page = await _validation.ReadCanonicalRowsAsync(dataset, after, 1000, cancellationToken);
				if (page.Count == 0)
					break;
				after = page[page.Count - 1].RowNumber;
				foreach (var row in page)
					visit(row);
			}
		}

		/// <summary>
		/// Who carries out an activation step: an administrator, or Resgrid staff on the department's behalf, in which case the
		/// authority's on-behalf-of administrator is the one checked for permission and recorded on department records, and the
		/// timeline shows the staff operator (section 17.8).
		/// </summary>
		private sealed class ActivationActor
		{
			public string ManagerUserId { get; private init; }
			public ImportActorKind Kind { get; private init; }
			public string Identity { get; private init; }
			public ImportAuthority Authority { get; private init; }

			public static ActivationActor Administrator(string userId) =>
				new ActivationActor { ManagerUserId = userId, Kind = ImportActorKind.Administrator, Identity = userId };

			public static ActivationActor Staff(ImportAuthority authority)
			{
				if (authority == null || !authority.IsStaff || string.IsNullOrWhiteSpace(authority.StaffOperator) || string.IsNullOrWhiteSpace(authority.StaffChecker))
					// Two people sign every staff activation step: never one without a recorded checker.
					throw new DepartmentImportException("import_not_allowed", 403);
				return new ActivationActor
				{
					ManagerUserId = authority.OnBehalfOfUserId,
					Kind = ImportActorKind.ResgridStaff,
					Identity = authority.StaffOperator,
					Authority = authority
				};
			}
		}

		private async Task RequireManagerAsync(int departmentId, string userId)
		{
			if (!await _access.CanManageAsync(departmentId, userId))
				throw new DepartmentImportException("import_not_allowed", 403);
		}

		private async Task<ImportWorkspace> RequireWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken) =>
			await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
	}
}
