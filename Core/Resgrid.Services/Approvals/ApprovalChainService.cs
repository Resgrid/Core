using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Approvals
{
	/// <summary>
	/// e-Signature S4 approval chains (plan §23 S4) on the S3 multi-signer request. See <see cref="IApprovalChainService"/>.
	/// <list type="bullet">
	/// <item>Chains are versioned: saving one inserts a new chain row with its steps and deactivates the version it replaces, so a
	/// running request keeps the steps it was resolved from.</item>
	/// <item>Every resolved step is an open S3 member step at L2 whose eligibility names the approvers (role, members, department
	/// admins, certification type) and excludes the submitter unless the chain allows self-approval.</item>
	/// <item>The S3 request binds to the subject's content hash; S3 refuses a signature once the subject moved on, the gate refuses an
	/// approval for content that changed, and a re-submission supersedes what came before.</item>
	/// </list>
	/// </summary>
	public class ApprovalChainService : IApprovalChainService
	{
		public const int BatchLimit = 200;
		public const int MaxApproversPerStep = 15;

		private readonly IApprovalChainsRepository _chains;
		private readonly IApprovalChainStepsRepository _chainSteps;
		private readonly IApprovalRequestsRepository _requests;
		private readonly IApprovalRequestStepsRepository _steps;
		private readonly IApprovalDelegatesRepository _delegates;
		private readonly IEnumerable<IApprovalSubjectSource> _sources;
		private readonly ISignatureRequestService _signatures;
		private readonly ISignatureRequestSignersRepository _signers;
		private readonly ISignerEligibilityService _eligibility;
		private readonly IMemberLeaveService _leave;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IApprovalNotificationService _notifications;
		private readonly IDocumentSealingService _sealing;
		private readonly IEventAggregator _events;

		public ApprovalChainService(IApprovalChainsRepository chains, IApprovalChainStepsRepository chainSteps, IApprovalRequestsRepository requests,
			IApprovalRequestStepsRepository steps, IApprovalDelegatesRepository delegates, IEnumerable<IApprovalSubjectSource> sources,
			ISignatureRequestService signatures, ISignatureRequestSignersRepository signers, ISignerEligibilityService eligibility, IMemberLeaveService leave,
			IDepartmentsService departments, IDepartmentGroupsService groups, IApprovalNotificationService notifications, IDocumentSealingService sealing,
			IEventAggregator events)
		{
			_chains = chains;
			_chainSteps = chainSteps;
			_requests = requests;
			_steps = steps;
			_delegates = delegates;
			_sources = sources ?? Enumerable.Empty<IApprovalSubjectSource>();
			_signatures = signatures;
			_signers = signers;
			_eligibility = eligibility;
			_leave = leave;
			_departments = departments;
			_groups = groups;
			_notifications = notifications;
			_sealing = sealing;
			_events = events;
		}

		private IApprovalSubjectSource SourceFor(SignatureSubjectKinds kind) => _sources.FirstOrDefault(s => s.Kind == kind);

		public IReadOnlyList<SignatureSubjectKinds> AvailableKinds => ApprovalUses.Kinds.Where(k => SourceFor(k) != null).ToList();

		public async Task<bool> IsKindAvailableAsync(int departmentId, SignatureSubjectKinds kind)
		{
			var source = SourceFor(kind);
			if (source == null || departmentId <= 0)
				return false;
			try
			{
				return await source.IsAvailableAsync(departmentId);
			}
			catch (Exception ex)
			{
				// The owning add-on's state unknown is "not available".
				Logging.LogException(ex, "An approval kind's module state could not be read.");
				return false;
			}
		}

		private static async Task<ApprovalSubjectInfo> DescribeAsync(IApprovalSubjectSource source, int departmentId, string subjectId)
		{
			if (source == null || departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return null;
			try
			{
				var info = await source.DescribeAsync(departmentId, subjectId.Trim());
				return info != null && SignaturePolicy.IsSha256(info.ContentSha256) ? info : null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An approval subject could not be read.");
				return null;
			}
		}

		#region Chains and delegates

		public async Task<List<ApprovalChainDefinition>> GetChainsAsync(int departmentId)
		{
			var list = new List<ApprovalChainDefinition>();
			foreach (var chain in (await _chains.GetForDepartmentAsync(departmentId) ?? new List<ApprovalChain>()).Where(c => c.DepartmentId == departmentId && c.IsActive))
				list.Add(new ApprovalChainDefinition { Chain = chain, Steps = await _chainSteps.GetForChainAsync(departmentId, chain.ApprovalChainId) ?? new List<ApprovalChainStep>() });
			return list;
		}

		public async Task<ApprovalChainDefinition> GetChainAsync(int departmentId, int approvalChainId)
		{
			var chain = (await _chains.GetForDepartmentAsync(departmentId) ?? new List<ApprovalChain>()).FirstOrDefault(c => c.ApprovalChainId == approvalChainId && c.DepartmentId == departmentId);
			return chain == null ? null : new ApprovalChainDefinition { Chain = chain, Steps = await _chainSteps.GetForChainAsync(departmentId, chain.ApprovalChainId) ?? new List<ApprovalChainStep>() };
		}

		public async Task<ApprovalChainDefinition> GetActiveChainAsync(int departmentId, SignatureSubjectKinds kind)
		{
			var chain = await _chains.GetActiveForKindAsync(departmentId, (int)kind);
			if (chain == null || chain.DepartmentId != departmentId)
				return null;
			return new ApprovalChainDefinition { Chain = chain, Steps = await _chainSteps.GetForChainAsync(departmentId, chain.ApprovalChainId) ?? new List<ApprovalChainStep>() };
		}

		public async Task<(ApprovalRefusals Refusal, ApprovalChainDefinition Definition)> SaveChainAsync(ApprovalChainDefinition definition, string userId, CancellationToken cancellationToken = default)
		{
			var refusal = ApprovalRouting.Validate(definition);
			if (refusal != ApprovalRefusals.None || string.IsNullOrWhiteSpace(userId))
				return (refusal == ApprovalRefusals.None ? ApprovalRefusals.InvalidRequest : refusal, null);

			var departmentId = definition.Chain.DepartmentId;
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			foreach (var step in definition.Steps)
			{
				if (step.ApproverKind == (int)ApprovalApproverKinds.NamedMember && !active.Contains(step.UserId.Trim()))
					return (ApprovalRefusals.NoApprover, null);
				if (step.EscalateAfterHours > 0 && step.EscalateTo == (int)ApprovalEscalationTargets.NamedMember && !active.Contains(step.EscalateToUserId.Trim()))
					return (ApprovalRefusals.NoApprover, null);
			}

			var previous = definition.Chain.ApprovalChainId > 0 ? await GetChainAsync(departmentId, definition.Chain.ApprovalChainId) : null;
			var now = DateTime.UtcNow;
			// Chains are versioned: a running request keeps the steps it was resolved from.
			var chain = await _chains.InsertAsync(new ApprovalChain
			{
				DepartmentId = departmentId,
				SubjectKind = definition.Chain.SubjectKind,
				Name = Clean(definition.Chain.Name, 200),
				IsActive = definition.Chain.IsActive,
				AllowSelfApproval = definition.Chain.AllowSelfApproval,
				ExpiresInDays = definition.Chain.ExpiresInDays,
				CreatedByUserId = previous?.Chain.CreatedByUserId ?? userId,
				CreatedOn = previous?.Chain.CreatedOn ?? now,
				UpdatedByUserId = previous == null ? null : userId,
				UpdatedOn = previous == null ? (DateTime?)null : now
			}, cancellationToken, true);

			var steps = new List<ApprovalChainStep>();
			foreach (var step in definition.Steps.OrderBy(s => s.StepOrder))
			{
				var named = step.ApproverKind == (int)ApprovalApproverKinds.NamedMember;
				var escalates = step.EscalateAfterHours > 0;
				steps.Add(await _chainSteps.InsertAsync(new ApprovalChainStep
				{
					ApprovalChainId = chain.ApprovalChainId,
					DepartmentId = departmentId,
					StepOrder = step.StepOrder,
					ApproverKind = step.ApproverKind,
					PersonnelRoleId = step.ApproverKind is (int)ApprovalApproverKinds.Role or (int)ApprovalApproverKinds.CertifyingOfficial ? step.PersonnelRoleId : null,
					UserId = named ? step.UserId.Trim() : null,
					DepartmentGroupId = step.ApproverKind == (int)ApprovalApproverKinds.GroupAdmin ? step.DepartmentGroupId : null,
					CertificationTypeId = step.ApproverKind == (int)ApprovalApproverKinds.CertifyingOfficial ? step.CertificationTypeId : null,
					MinimumAmount = step.MinimumAmount,
					Label = Clean(step.Label, 200),
					EscalateAfterHours = escalates ? step.EscalateAfterHours : null,
					EscalateTo = escalates ? step.EscalateTo : (int)ApprovalEscalationTargets.DepartmentAdmins,
					EscalateToRoleId = escalates && step.EscalateTo == (int)ApprovalEscalationTargets.Role ? step.EscalateToRoleId : null,
					EscalateToUserId = escalates && step.EscalateTo == (int)ApprovalEscalationTargets.NamedMember ? step.EscalateToUserId.Trim() : null
				}, cancellationToken, true));
			}

			if (previous != null && previous.Chain.IsActive)
				await DeactivateAsync(previous.Chain, userId, cancellationToken);
			if (chain.IsActive)
				foreach (var other in (await _chains.GetForDepartmentAsync(departmentId) ?? new List<ApprovalChain>())
					.Where(c => c.DepartmentId == departmentId && c.IsActive && c.SubjectKind == chain.SubjectKind && c.ApprovalChainId != chain.ApprovalChainId))
					await DeactivateAsync(other, userId, cancellationToken);

			Audit(departmentId, userId, AuditLogTypes.ApprovalChainChanged, new
			{
				chain.ApprovalChainId, chain.SubjectKind, chain.Name, chain.IsActive, chain.AllowSelfApproval, chain.ExpiresInDays, Replaces = previous?.Chain.ApprovalChainId,
				Steps = steps.Select(s => new { s.StepOrder, s.ApproverKind, s.PersonnelRoleId, s.UserId, s.DepartmentGroupId, s.CertificationTypeId, s.MinimumAmount, s.EscalateAfterHours, s.EscalateTo })
			});
			return (ApprovalRefusals.None, new ApprovalChainDefinition { Chain = chain, Steps = steps });
		}

		private async Task DeactivateAsync(ApprovalChain chain, string userId, CancellationToken cancellationToken)
		{
			chain.IsActive = false;
			chain.UpdatedByUserId = userId;
			chain.UpdatedOn = DateTime.UtcNow;
			await _chains.UpdateAsync(chain, cancellationToken, true);
		}

		public async Task<bool> DeactivateChainAsync(int departmentId, int approvalChainId, string userId, CancellationToken cancellationToken = default)
		{
			var definition = await GetChainAsync(departmentId, approvalChainId);
			if (definition == null || !definition.Chain.IsActive)
				return false;
			await DeactivateAsync(definition.Chain, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ApprovalChainChanged, new { definition.Chain.ApprovalChainId, definition.Chain.SubjectKind, IsActive = false });
			return true;
		}

		public async Task<List<ApprovalDelegate>> GetDelegatesAsync(int departmentId) =>
			(await _delegates.GetForDepartmentAsync(departmentId) ?? new List<ApprovalDelegate>()).Where(d => d.DepartmentId == departmentId).ToList();

		public async Task<ApprovalDelegate> SaveDelegateAsync(int departmentId, string userId, string delegateUserId, DateTime? startsOnUtc, DateTime? endsOnUtc, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(delegateUserId) || ApprovalRouting.Same(userId, delegateUserId))
				return null;
			if (startsOnUtc.HasValue && endsOnUtc.HasValue && endsOnUtc.Value < startsOnUtc.Value)
				return null;
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			if (!active.Contains(userId.Trim()) || !active.Contains(delegateUserId.Trim()))
				return null;

			var saved = await _delegates.InsertAsync(new ApprovalDelegate
			{
				DepartmentId = departmentId, UserId = userId.Trim(), DelegateUserId = delegateUserId.Trim(), StartsOn = startsOnUtc, EndsOn = endsOnUtc,
				CreatedByUserId = actingUserId, CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.ApprovalDelegateChanged, new { saved.ApprovalDelegateId, saved.UserId, saved.DelegateUserId, saved.StartsOn, saved.EndsOn });
			return saved;
		}

		public async Task<bool> RemoveDelegateAsync(int departmentId, int approvalDelegateId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var existing = (await GetDelegatesAsync(departmentId)).FirstOrDefault(d => d.ApprovalDelegateId == approvalDelegateId);
			if (existing == null)
				return false;
			await _delegates.DeleteAsync(existing, cancellationToken);
			Audit(departmentId, actingUserId, AuditLogTypes.ApprovalDelegateChanged, new { existing.ApprovalDelegateId, existing.UserId, existing.DelegateUserId, Removed = true });
			return true;
		}

		#endregion

		#region Submit

		public async Task<ApprovalResult> SubmitAsync(ApprovalSubmitInput input, CancellationToken cancellationToken = default)
		{
			if (input == null || input.DepartmentId <= 0 || string.IsNullOrWhiteSpace(input.SubjectId) || string.IsNullOrWhiteSpace(input.UserId) || !ApprovalUses.IsApprovalKind(input.Kind))
				return ApprovalResult.Refused(ApprovalRefusals.InvalidRequest);

			var source = SourceFor(input.Kind);
			if (source == null || !await IsKindAvailableAsync(input.DepartmentId, input.Kind))
				return ApprovalResult.Refused(ApprovalRefusals.ModuleUnavailable);

			var info = await DescribeAsync(source, input.DepartmentId, input.SubjectId);
			if (info == null)
				return ApprovalResult.Refused(ApprovalRefusals.SubjectNotFound);
			if (!input.IsDepartmentAdmin && !ApprovalRouting.Same(info.OwnerUserId, input.UserId))
				return ApprovalResult.Refused(ApprovalRefusals.NotAllowed);

			var definition = await GetActiveChainAsync(input.DepartmentId, input.Kind);
			if (definition == null)
				return ApprovalResult.Refused(ApprovalRefusals.NoChain);
			var routed = ApprovalRouting.Resolve(definition.Steps, info.Amount);
			if (routed.Count == 0)
				return ApprovalResult.Refused(ApprovalRefusals.NotRequired);

			var history = await _requests.GetForSubjectAsync(input.DepartmentId, (int)input.Kind, info.SubjectId) ?? new List<ApprovalRequest>();
			var latest = history.FirstOrDefault(r => r.DepartmentId == input.DepartmentId);
			var sameContent = latest != null && SignatureTokens.HashEquals(latest.ContentSha256, info.ContentSha256);
			if (latest != null && latest.IsPending && sameContent)
				return ApprovalResult.Refused(ApprovalRefusals.AlreadyPending);
			// A re-submission needs a change: what was rejected (or already approved) is not asked again unchanged.
			if (latest != null && sameContent && latest.Status is (int)ApprovalRequestStatuses.Approved or (int)ApprovalRequestStatuses.Rejected)
				return ApprovalResult.Refused(ApprovalRefusals.Unchanged);
			if (!info.Submittable)
				return ApprovalResult.Refused(ApprovalRefusals.NotSubmittable);

			// Resolve every step's approvers before anything is stored.
			var now = DateTime.UtcNow;
			var context = await RoutingContextAsync(input.DepartmentId);
			var resolved = new List<(ApprovalChainStep Step, int Order, SignerEligibility Rule, List<string> Delegated)>();
			foreach (var (step, order) in routed)
			{
				var (rule, delegated) = await ResolveAsync(input.DepartmentId, step, info, input.UserId, definition.Chain.AllowSelfApproval, now, context);
				if (rule == null)
					return ApprovalResult.Refused(ApprovalRefusals.NoApprover);
				resolved.Add((step, order, rule, delegated));
			}

			var created = await _signatures.CreateMultiSignerAsync(new MultiSignerRequestInput
			{
				DepartmentId = input.DepartmentId,
				SubjectKind = input.Kind,
				SubjectId = info.SubjectId,
				Ordered = true,
				ExternalChannel = SignatureRequestChannels.InPerson,
				Steps = resolved.Select(r => new SignatureStepInput
				{
					Kind = SignatureSignerKinds.Member,
					Role = r.Step.ApproverKind == (int)ApprovalApproverKinds.CertifyingOfficial ? SignatureSignerRoles.CertifyingOfficial : SignatureSignerRoles.Member,
					Order = r.Order,
					Eligibility = r.Rule,
					Level = SignatureLevels.StepUp,
					StepLabel = StepLabel(r.Step)
				}).ToList(),
				ExpiresInDays = Math.Clamp(definition.Chain.ExpiresInDays, 1, ApprovalRouting.MaxExpiresInDays),
				Culture = input.Culture,
				RequestedByUserId = input.UserId,
				IpAddress = input.IpAddress,
				UserAgent = input.UserAgent
			}, cancellationToken);
			if (!created.Succeeded || created.Request == null)
				return ApprovalResult.Refused(created.Refusal == SignatureRequestRefusals.SubjectNotFound ? ApprovalRefusals.SubjectNotFound : ApprovalRefusals.SignatureFailed);

			// What came before is superseded: a pending request for older content, or an approval of content that has since changed.
			if (latest != null && !sameContent && latest.Status is (int)ApprovalRequestStatuses.Pending or (int)ApprovalRequestStatuses.Approved)
				await SupersedeAsync(latest, input.UserId, cancellationToken);

			var approval = await _requests.InsertAsync(new ApprovalRequest
			{
				DepartmentId = input.DepartmentId,
				SubjectKind = (int)input.Kind,
				SubjectId = info.SubjectId,
				ApprovalChainId = definition.Chain.ApprovalChainId,
				SignatureRequestId = created.Request.SignatureRequestId,
				Title = Clean(info.Title, 256),
				Amount = info.Amount,
				Currency = Clean(info.Currency, 8),
				ContentSha256 = info.ContentSha256.ToLowerInvariant(),
				RevisionId = Clean(info.RevisionId, 128),
				Status = (int)ApprovalRequestStatuses.Pending,
				SubmittedByUserId = input.UserId,
				SubmittedOn = now,
				SupersedesApprovalRequestId = latest?.ApprovalRequestId
			}, cancellationToken, true);

			// S3 inserted its signers in step order; each resolved step maps to one.
			var signers = (await _signatures.GetSignersAsync(input.DepartmentId, created.Request.SignatureRequestId) ?? new List<SignatureRequestSigner>())
				.OrderBy(s => s.SignatureRequestSignerId).ToList();
			var ordered = resolved.Select((r, i) => (r, i)).OrderBy(x => x.r.Order).ThenBy(x => x.i).Select(x => x.r).ToList();
			for (var i = 0; i < ordered.Count && i < signers.Count; i++)
			{
				await _steps.InsertAsync(new ApprovalRequestStep
				{
					ApprovalRequestId = approval.ApprovalRequestId,
					DepartmentId = input.DepartmentId,
					SignatureRequestSignerId = signers[i].SignatureRequestSignerId,
					ApprovalChainStepId = ordered[i].Step.ApprovalChainStepId,
					StepOrder = ordered[i].Order,
					Label = StepLabel(ordered[i].Step),
					DelegatedToUserIds = ordered[i].Delegated.Count == 0 ? null : Clean(string.Join(",", ordered[i].Delegated), 400)
				}, cancellationToken, true);
			}

			Audit(input.DepartmentId, input.UserId, AuditLogTypes.ApprovalSubmitted, new
			{
				approval.ApprovalRequestId, approval.SubjectKind, approval.SubjectId, approval.Amount, approval.Currency, approval.ContentSha256, approval.SignatureRequestId,
				approval.ApprovalChainId, Steps = ordered.Count, Supersedes = latest?.ApprovalRequestId
			}, input.IpAddress, input.UserAgent);
			Publish(approval, ApprovalWorkflowChanges.Requested, input.UserId, null, null);

			await AnnounceRequestAsync(approval, now, cancellationToken);
			return ApprovalResult.Of(approval);
		}

		private async Task SupersedeAsync(ApprovalRequest previous, string userId, CancellationToken cancellationToken)
		{
			var from = previous.Status;
			if (!await _requests.ClaimStatusAsync(previous.ApprovalRequestId, from, (int)ApprovalRequestStatuses.Superseded, DateTime.UtcNow, cancellationToken))
				return;
			previous.Status = (int)ApprovalRequestStatuses.Superseded;
			// A pending chain for the old content is closed: its signatures stand only for the old content hash.
			if (from == (int)ApprovalRequestStatuses.Pending && previous.SignatureRequestId.HasValue)
				await _signatures.VoidAsync(previous.DepartmentId, previous.SignatureRequestId.Value, userId, "approval_subject_changed", cancellationToken);
			Audit(previous.DepartmentId, userId, AuditLogTypes.ApprovalSuperseded, new { previous.ApprovalRequestId, previous.SubjectKind, previous.SubjectId, FromStatus = from });
		}

		private sealed class RoutingContext
		{
			public List<MemberLeavePeriod> Leave { get; set; }
			public List<ApprovalDelegate> Delegates { get; set; }
			public HashSet<string> Active { get; set; }
		}

		private async Task<RoutingContext> RoutingContextAsync(int departmentId) => new RoutingContext
		{
			Leave = await _leave.GetForDepartmentAsync(departmentId) ?? new List<MemberLeavePeriod>(),
			Delegates = await GetDelegatesAsync(departmentId),
			Active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase)
		};

		/// <summary>
		/// The approvers of one chain step as an S3 eligibility rule, and the delegates added because a named approver is away. Null when
		/// nobody could approve (a named member who left, a group without admins and no fallback).
		/// </summary>
		private async Task<(SignerEligibility Rule, List<string> Delegated)> ResolveAsync(int departmentId, ApprovalChainStep step, ApprovalSubjectInfo info, string submitterUserId,
			bool allowSelfApproval, DateTime utcNow, RoutingContext context)
		{
			var rule = new SignerEligibility();
			var delegated = new List<string>();
			if (!allowSelfApproval && !string.IsNullOrWhiteSpace(submitterUserId))
				rule.ExcludedUserIds.Add(submitterUserId.Trim());

			switch ((ApprovalApproverKinds)step.ApproverKind)
			{
				case ApprovalApproverKinds.Role:
					if (step.PersonnelRoleId > 0)
						rule.PersonnelRoleIds.Add(step.PersonnelRoleId.Value);
					break;
				case ApprovalApproverKinds.CertifyingOfficial:
					if (step.CertificationTypeId > 0)
						rule.CertificationTypeIds.Add(step.CertificationTypeId.Value);
					if (step.PersonnelRoleId > 0)
						rule.PersonnelRoleIds.Add(step.PersonnelRoleId.Value);
					break;
				case ApprovalApproverKinds.DepartmentAdmins:
					rule.DepartmentAdmins = true;
					break;
				case ApprovalApproverKinds.NamedMember:
					if (string.IsNullOrWhiteSpace(step.UserId) || !context.Active.Contains(step.UserId.Trim()))
						return (null, delegated);
					rule.UserIds.Add(step.UserId.Trim());
					foreach (var d in ApprovalRouting.DelegatesFor(step.UserId, utcNow, context.Leave, context.Delegates).Where(context.Active.Contains))
					{
						AddUser(rule, d);
						delegated.Add(d);
					}
					break;
				case ApprovalApproverKinds.GroupAdmin:
					var groupId = step.DepartmentGroupId ?? info?.GroupId;
					if (groupId > 0)
					{
						var group = await _groups.GetGroupByIdAsync(groupId.Value);
						if (group != null && group.DepartmentId == departmentId)
							foreach (var admin in await _groups.GetAllAdminsForGroupAsync(groupId.Value) ?? new List<DepartmentGroupMember>())
								if (admin != null && context.Active.Contains(admin.UserId ?? string.Empty))
									AddUser(rule, admin.UserId);
					}
					// A subject without a group (or a group without admins) goes to the department admins.
					if (rule.UserIds.Count == 0)
						rule.DepartmentAdmins = true;
					break;
			}

			if (rule.IsEmpty)
				return (null, delegated);
			// Only the submitter can approve a named step: refused, as the chain would stall on the submitter.
			if (rule.PersonnelRoleIds.Count == 0 && rule.CertificationTypeIds.Count == 0 && !rule.DepartmentAdmins && rule.UserIds.All(rule.Excludes))
				return (null, delegated);
			return (rule, delegated);
		}

		private static void AddUser(SignerEligibility rule, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId) || rule.Names(userId) || rule.UserIds.Count >= MaxApproversPerStep)
				return;
			rule.UserIds.Add(userId.Trim());
		}

		private static string StepLabel(ApprovalChainStep step) => !string.IsNullOrWhiteSpace(step.Label) ? step.Label.Trim()
			: ((ApprovalApproverKinds)step.ApproverKind) switch
			{
				ApprovalApproverKinds.CertifyingOfficial => "Certifying official",
				ApprovalApproverKinds.DepartmentAdmins => "Department admin",
				ApprovalApproverKinds.GroupAdmin => "Group admin",
				_ => "Approver"
			} + " (" + step.StepOrder.ToString(CultureInfo.InvariantCulture) + ")";

		#endregion

		#region Decide

		public async Task<ApprovalResult> ApproveAsync(int departmentId, int approvalRequestId, SignatureProof proof, bool requirePasskey, CancellationToken cancellationToken = default)
		{
			var approval = await _requests.GetForDepartmentAsync(departmentId, approvalRequestId);
			if (approval == null || approval.DepartmentId != departmentId || proof == null || string.IsNullOrWhiteSpace(proof.UserId))
				return ApprovalResult.Refused(ApprovalRefusals.InvalidRequest);
			if (!approval.IsPending || !approval.SignatureRequestId.HasValue)
				return ApprovalResult.Refused(ApprovalRefusals.Closed);

			var info = await DescribeAsync(SourceFor((SignatureSubjectKinds)approval.SubjectKind), departmentId, approval.SubjectId);
			if (info == null)
				return ApprovalResult.Refused(ApprovalRefusals.SubjectNotFound);
			if (!SignatureTokens.HashEquals(info.ContentSha256, approval.ContentSha256))
				return ApprovalResult.Refused(ApprovalRefusals.SubjectChanged);

			var signed = await _signatures.SignStepAsync(new SignatureStepSignInput
			{
				DepartmentId = departmentId,
				SignatureRequestId = approval.SignatureRequestId.Value,
				Proof = proof,
				RequirePasskey = requirePasskey
			}, cancellationToken);
			if (!signed.Succeeded)
			{
				// S3 answers "not eligible" while an earlier group is open; an approver of a later step is told to wait for their turn.
				if (signed.Refusal == SignatureRequestRefusals.NotEligible && await ApprovesLaterStepAsync(departmentId, approval.SignatureRequestId.Value, proof.UserId))
					return ApprovalResult.Refused(ApprovalRefusals.NotYourTurn);
				return ApprovalResult.Refused(Map(signed.Refusal));
			}

			// A completed request was already applied by the completion hook; otherwise the next group's approvers are told now.
			approval = await _requests.GetForDepartmentAsync(departmentId, approvalRequestId) ?? approval;
			if (approval.IsPending)
				await AnnounceRequestAsync(approval, DateTime.UtcNow, cancellationToken);
			return ApprovalResult.Of(approval);
		}

		private async Task<bool> ApprovesLaterStepAsync(int departmentId, int signatureRequestId, string userId)
		{
			var request = await _signatures.GetAsync(departmentId, signatureRequestId);
			var signers = await _signatures.GetSignersAsync(departmentId, signatureRequestId) ?? new List<SignatureRequestSigner>();
			if (request == null)
				return false;
			foreach (var later in signers.Where(s => s.IsMember && !s.IsDone && !SignatureOrdering.IsTurn(s, signers, request.Ordered)))
			{
				var rule = SignerEligibility.Parse(later.EligibilityJson);
				if (rule != null && _eligibility != null && await _eligibility.IsEligibleAsync(departmentId, userId, rule))
					return true;
			}
			return false;
		}

		public async Task<ApprovalResult> RejectAsync(int departmentId, int approvalRequestId, string userId, string reason, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reason))
				return ApprovalResult.Refused(ApprovalRefusals.ReasonRequired);
			var approval = await _requests.GetForDepartmentAsync(departmentId, approvalRequestId);
			if (approval == null || approval.DepartmentId != departmentId || string.IsNullOrWhiteSpace(userId))
				return ApprovalResult.Refused(ApprovalRefusals.InvalidRequest);
			if (!approval.IsPending || !approval.SignatureRequestId.HasValue)
				return ApprovalResult.Refused(ApprovalRefusals.Closed);

			var declined = await _signatures.DeclineStepAsync(departmentId, approval.SignatureRequestId.Value, null, userId, Clean(reason, 1000), cancellationToken);
			if (!declined.Succeeded)
				return ApprovalResult.Refused(Map(declined.Refusal));

			await MarkRejectedAsync(approval, userId, Clean(reason, 1000), cancellationToken);
			return ApprovalResult.Of(approval);
		}

		public async Task<ApprovalResult> WithdrawAsync(int departmentId, int approvalRequestId, string userId, bool isDepartmentAdmin, CancellationToken cancellationToken = default)
		{
			var approval = await _requests.GetForDepartmentAsync(departmentId, approvalRequestId);
			if (approval == null || approval.DepartmentId != departmentId)
				return ApprovalResult.Refused(ApprovalRefusals.InvalidRequest);
			if (!isDepartmentAdmin && !ApprovalRouting.Same(approval.SubmittedByUserId, userId))
				return ApprovalResult.Refused(ApprovalRefusals.NotAllowed);
			if (!approval.IsPending || !await _requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Withdrawn, DateTime.UtcNow, cancellationToken))
				return ApprovalResult.Refused(ApprovalRefusals.Closed);

			approval.Status = (int)ApprovalRequestStatuses.Withdrawn;
			if (approval.SignatureRequestId.HasValue)
				await _signatures.VoidAsync(departmentId, approval.SignatureRequestId.Value, userId, "approval_withdrawn", cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ApprovalWithdrawn, new { approval.ApprovalRequestId, approval.SubjectKind, approval.SubjectId });
			return ApprovalResult.Of(approval);
		}

		private async Task MarkRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			if (!await _requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Rejected, now, cancellationToken))
				return;
			approval.Status = (int)ApprovalRequestStatuses.Rejected;
			approval.DecidedOn = now;
			approval.DecidedByUserId = userId;
			approval.RejectReason = reason;
			approval.UpdatedOn = now;
			await _requests.UpdateAsync(approval, cancellationToken, true);

			var source = SourceFor((SignatureSubjectKinds)approval.SubjectKind);
			if (source != null)
			{
				try { await source.OnRejectedAsync(approval, userId, reason, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, "A module could not take back a rejected approval subject."); }
			}

			Audit(approval.DepartmentId, userId, AuditLogTypes.ApprovalRejected, new { approval.ApprovalRequestId, approval.SubjectKind, approval.SubjectId, approval.ContentSha256 });
			Publish(approval, ApprovalWorkflowChanges.Rejected, userId, null, reason);
			await NotifyDecidedAsync(approval);
		}

		public async Task HandleSignatureCompletedAsync(SignatureRequestRecord request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.Status != (int)SignatureRequestStatuses.Completed)
				return;
			var approval = await _requests.GetBySignatureRequestAsync(request.DepartmentId, request.SignatureRequestId);
			if (approval == null || approval.DepartmentId != request.DepartmentId || !approval.IsPending)
				return;

			var now = DateTime.UtcNow;
			// The completion hook runs once per signer, and the worker may sync the same request: one claim wins.
			if (!await _requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Approved, now, cancellationToken))
				return;

			var signers = await _signatures.GetSignersAsync(request.DepartmentId, request.SignatureRequestId) ?? new List<SignatureRequestSigner>();
			var last = signers.Where(s => s.Status == (int)SignatureSignerStatuses.Signed).OrderByDescending(s => s.SignedOn).FirstOrDefault();
			approval.Status = (int)ApprovalRequestStatuses.Approved;
			approval.DecidedOn = request.CompletedOn ?? now;
			approval.DecidedByUserId = last?.UserId;
			approval.SealedDocumentId = request.SealedDocumentId;
			approval.UpdatedOn = now;

			var source = SourceFor((SignatureSubjectKinds)approval.SubjectKind);
			if (source != null)
			{
				try
				{
					await source.OnApprovedAsync(approval, last?.UserId, cancellationToken);
					approval.AppliedOn = DateTime.UtcNow;
				}
				catch (Exception ex)
				{
					// The approval stands (it is signed and sealed); the module's own rule refused to move the subject.
					approval.ApplyError = Clean(ex.Message, 500);
					Logging.LogException(ex, "A module could not apply an approval.");
				}
			}
			await _requests.UpdateAsync(approval, cancellationToken, true);

			Audit(approval.DepartmentId, last?.UserId, AuditLogTypes.ApprovalGranted, new
			{
				approval.ApprovalRequestId, approval.SubjectKind, approval.SubjectId, approval.ContentSha256, request.SignatureRequestId, request.SealedDocumentId, approval.ApplyError
			});
			Publish(approval, ApprovalWorkflowChanges.Granted, last?.UserId, null, null);
			await NotifyDecidedAsync(approval);
		}

		private static ApprovalRefusals Map(SignatureRequestRefusals refusal) => refusal switch
		{
			SignatureRequestRefusals.None => ApprovalRefusals.None,
			SignatureRequestRefusals.NotYourTurn => ApprovalRefusals.NotYourTurn,
			SignatureRequestRefusals.NotEligible => ApprovalRefusals.NotAnApprover,
			SignatureRequestRefusals.NotASigner => ApprovalRefusals.NotAnApprover,
			SignatureRequestRefusals.StepUpRequired => ApprovalRefusals.StepUpRequired,
			SignatureRequestRefusals.PasskeyRequired => ApprovalRefusals.PasskeyRequired,
			SignatureRequestRefusals.RevisionChanged => ApprovalRefusals.SubjectChanged,
			SignatureRequestRefusals.ArtifactMismatch => ApprovalRefusals.SubjectChanged,
			SignatureRequestRefusals.RequestClosed => ApprovalRefusals.Closed,
			SignatureRequestRefusals.SubjectNotFound => ApprovalRefusals.SubjectNotFound,
			_ => ApprovalRefusals.SignatureFailed
		};

		#endregion

		#region Reads

		public Task<ApprovalRequest> GetRequestAsync(int departmentId, int approvalRequestId) => _requests.GetForDepartmentAsync(departmentId, approvalRequestId);

		public async Task<ApprovalView> GetViewAsync(int departmentId, int approvalRequestId, string userId, bool isDepartmentAdmin)
		{
			var approval = await _requests.GetForDepartmentAsync(departmentId, approvalRequestId);
			if (approval == null || approval.DepartmentId != departmentId)
				return null;

			var view = new ApprovalView { Request = approval };
			if (approval.SignatureRequestId.HasValue)
			{
				view.SignatureRequest = await _signatures.GetAsync(departmentId, approval.SignatureRequestId.Value);
				view.StepView = await _signatures.GetStepViewAsync(departmentId, approval.SignatureRequestId.Value, userId);
			}

			var signers = view.StepView?.Signers ?? new List<SignatureRequestSigner>();
			var involved = isDepartmentAdmin || ApprovalRouting.Same(approval.SubmittedByUserId, userId) || view.StepView?.MyStep != null || view.StepView?.MyWaitingStep != null
				|| signers.Any(s => ApprovalRouting.Same(s.UserId, userId));
			if (!involved)
				return null;

			view.Subject = await DescribeAsync(SourceFor((SignatureSubjectKinds)approval.SubjectKind), departmentId, approval.SubjectId);
			view.Changed = view.Subject == null || !SignatureTokens.HashEquals(view.Subject.ContentSha256, approval.ContentSha256);
			var ordered = view.SignatureRequest?.Ordered ?? true;
			foreach (var step in await _steps.GetForRequestAsync(departmentId, approvalRequestId) ?? new List<ApprovalRequestStep>())
			{
				var signer = signers.FirstOrDefault(s => s.SignatureRequestSignerId == step.SignatureRequestSignerId);
				view.Steps.Add(new ApprovalStepState { Step = step, Signer = signer, IsTurn = signer != null && SignatureOrdering.IsTurn(signer, signers, ordered) });
			}
			view.CanApprove = approval.IsPending && !view.Changed && view.StepView?.MyStep != null;
			view.CanWithdraw = approval.IsPending && (isDepartmentAdmin || ApprovalRouting.Same(approval.SubmittedByUserId, userId));
			if (approval.SealedDocumentId.HasValue)
			{
				try { view.Sealed = await _sealing.GetByIdAsync(departmentId, approval.SealedDocumentId.Value); }
				catch (Exception ex) { Logging.LogException(ex, "An approval's sealed document could not be read."); }
			}
			return view;
		}

		public async Task<ApprovalSubjectStatus> GetSubjectStatusAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId, bool isDepartmentAdmin)
		{
			var status = new ApprovalSubjectStatus { Kind = kind, SubjectId = subjectId };
			var source = SourceFor(kind);
			if (source == null || departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return status;

			status.ModuleAvailable = await IsKindAvailableAsync(departmentId, kind);
			var chain = status.ModuleAvailable ? await GetActiveChainAsync(departmentId, kind) : null;
			status.ChainActive = chain != null;
			status.Subject = await DescribeAsync(source, departmentId, subjectId);
			status.History = (await _requests.GetForSubjectAsync(departmentId, (int)kind, subjectId) ?? new List<ApprovalRequest>()).Where(r => r.DepartmentId == departmentId).ToList();
			status.Latest = status.History.FirstOrDefault();
			status.Changed = status.Latest != null && status.Subject != null && !SignatureTokens.HashEquals(status.Latest.ContentSha256, status.Subject.ContentSha256);
			var blockedByLatest = status.Latest != null && !status.Changed
				&& status.Latest.Status is (int)ApprovalRequestStatuses.Pending or (int)ApprovalRequestStatuses.Approved or (int)ApprovalRequestStatuses.Rejected;
			status.CanSubmit = status.ChainActive && status.Subject != null && status.Subject.Submittable && !blockedByLatest
				&& (isDepartmentAdmin || ApprovalRouting.Same(status.Subject.OwnerUserId, userId))
				&& ApprovalRouting.Resolve(chain.Steps, status.Subject.Amount).Count > 0;
			return status;
		}

		public async Task<List<ApprovalInboxItem>> GetInboxAsync(int departmentId, string userId)
		{
			var items = new List<ApprovalInboxItem>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return items;

			foreach (var approval in await _requests.GetByStatusAsync(departmentId, (int)ApprovalRequestStatuses.Pending, BatchLimit) ?? new List<ApprovalRequest>())
			{
				if (approval.DepartmentId != departmentId || !approval.SignatureRequestId.HasValue || ApprovalRouting.Same(approval.SubmittedByUserId, userId))
					continue;
				try
				{
					var view = await _signatures.GetStepViewAsync(departmentId, approval.SignatureRequestId.Value, userId);
					if (view?.MyStep == null)
						continue;
					var steps = await _steps.GetForRequestAsync(departmentId, approval.ApprovalRequestId) ?? new List<ApprovalRequestStep>();
					items.Add(new ApprovalInboxItem
					{
						ApprovalRequestId = approval.ApprovalRequestId,
						SignatureRequestId = approval.SignatureRequestId.Value,
						SubjectKind = approval.SubjectKind,
						SubjectId = approval.SubjectId,
						Title = approval.Title,
						Amount = approval.Amount,
						Currency = approval.Currency,
						StepLabel = view.MyStep.StepLabel,
						SubmittedOn = approval.SubmittedOn,
						ExpiresOn = view.Request.ExpiresOn,
						Escalated = steps.Any(s => s.SignatureRequestSignerId == view.MyStep.SignatureRequestSignerId && s.EscalatedOn.HasValue),
						ContentCode = SignaturePolicy.ShortCode(approval.ContentSha256)
					});
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An approval could not be shown in the inbox.");
				}
			}

			return items.OrderByDescending(i => i.Escalated).ThenBy(i => i.ExpiresOn).ThenBy(i => i.ApprovalRequestId).ToList();
		}

		public async Task<List<ApprovalRequest>> GetRequestsAsync(int departmentId, ApprovalRequestStatuses status, int take) =>
			(await _requests.GetByStatusAsync(departmentId, (int)status, Math.Clamp(take, 1, BatchLimit)) ?? new List<ApprovalRequest>()).Where(r => r.DepartmentId == departmentId).ToList();

		public async Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, SignatureSubjectKinds kind, int take)
		{
			var source = SourceFor(kind);
			if (source == null || !await IsKindAvailableAsync(departmentId, kind))
				return new List<ApprovalSubjectInfo>();
			var candidates = new List<ApprovalSubjectInfo>();
			foreach (var info in await source.GetCandidatesAsync(departmentId, Math.Clamp(take, 1, 50)) ?? new List<ApprovalSubjectInfo>())
			{
				if (info == null || !info.Submittable || !SignaturePolicy.IsSha256(info.ContentSha256))
					continue;
				var latest = (await _requests.GetForSubjectAsync(departmentId, (int)kind, info.SubjectId) ?? new List<ApprovalRequest>()).FirstOrDefault();
				if (latest != null && SignatureTokens.HashEquals(latest.ContentSha256, info.ContentSha256)
					&& latest.Status is (int)ApprovalRequestStatuses.Pending or (int)ApprovalRequestStatuses.Approved or (int)ApprovalRequestStatuses.Rejected)
					continue;
				candidates.Add(info);
			}
			return candidates;
		}

		#endregion

		#region Worker 97

		public async Task<int> AnnounceTurnsAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var announced = 0;
			foreach (var group in (await _steps.GetUnannouncedTurnsAsync(Math.Max(1, batchSize)) ?? new List<ApprovalRequestStep>()).GroupBy(s => (s.DepartmentId, s.ApprovalRequestId)))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var approval = await _requests.GetForDepartmentAsync(group.Key.DepartmentId, group.Key.ApprovalRequestId);
					if (approval != null && approval.IsPending)
						announced += await AnnounceRequestAsync(approval, utcNow, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An approval step could not be announced.");
				}
			}
			return announced;
		}

		/// <summary>
		/// Announces every step of the request whose S3 turn has started and was not announced: a named approver who is away has their
		/// delegates added first (and the step escalates at once when they have none), the escalation time is stored, and the approvers
		/// are told. Each announcement is claimed, so the web path and the worker never both send it.
		/// </summary>
		private async Task<int> AnnounceRequestAsync(ApprovalRequest approval, DateTime utcNow, CancellationToken cancellationToken)
		{
			if (!approval.SignatureRequestId.HasValue)
				return 0;
			var request = await _signatures.GetAsync(approval.DepartmentId, approval.SignatureRequestId.Value);
			if (request == null || !request.IsOpen)
				return 0;
			var signers = await _signatures.GetSignersAsync(approval.DepartmentId, request.SignatureRequestId) ?? new List<SignatureRequestSigner>();
			var steps = await _steps.GetForRequestAsync(approval.DepartmentId, approval.ApprovalRequestId) ?? new List<ApprovalRequestStep>();
			var pending = steps.Where(s => !s.TurnNotifiedOn.HasValue).ToList();
			if (pending.Count == 0)
				return 0;

			var chainSteps = (await _chainSteps.GetForChainAsync(approval.DepartmentId, approval.ApprovalChainId) ?? new List<ApprovalChainStep>()).ToDictionary(s => s.ApprovalChainStepId);
			RoutingContext context = null;
			var announced = 0;
			foreach (var step in pending)
			{
				var signer = signers.FirstOrDefault(s => s.SignatureRequestSignerId == step.SignatureRequestSignerId);
				if (signer == null || signer.IsDone || !signer.TurnStartedOn.HasValue || !SignatureOrdering.IsTurn(signer, signers, request.Ordered))
					continue;
				chainSteps.TryGetValue(step.ApprovalChainStepId, out var chainStep);

				// Delegation is decided when the step's turn starts: the approver may have gone on leave since submission.
				var rule = SignerEligibility.Parse(signer.EligibilityJson) ?? new SignerEligibility();
				var escalateNow = false;
				if (chainStep != null && chainStep.ApproverKind == (int)ApprovalApproverKinds.NamedMember)
				{
					context ??= await RoutingContextAsync(approval.DepartmentId);
					if (ApprovalRouting.IsAway(chainStep.UserId, utcNow, context.Leave, context.Delegates))
					{
						var delegates = ApprovalRouting.DelegatesFor(chainStep.UserId, utcNow, context.Leave, context.Delegates).Where(context.Active.Contains)
							.Where(d => !rule.Excludes(d)).ToList();
						if (delegates.Count == 0)
							escalateNow = true;
						var added = delegates.Where(d => !rule.Names(d)).ToList();
						if (added.Count > 0)
						{
							foreach (var d in added)
								AddUser(rule, d);
							signer.EligibilityJson = rule.ToJson();
							await _signers.UpdateAsync(signer, cancellationToken, true);
							step.DelegatedToUserIds = Clean(string.Join(",", (step.DelegatedToUserIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Concat(added)
								.Distinct(StringComparer.OrdinalIgnoreCase)), 400);
							await _steps.UpdateAsync(step, cancellationToken, true);
						}
					}
				}

				var due = escalateNow ? utcNow : ApprovalRouting.EscalationDue(signer.TurnStartedOn, chainStep?.EscalateAfterHours);
				if (!await _steps.ClaimAnnouncementAsync(step.ApprovalRequestStepId, utcNow, due, cancellationToken))
					continue;
				step.TurnNotifiedOn = utcNow;
				step.EscalateDueOn = due;

				var recipients = (await EligibleUsersAsync(approval.DepartmentId, rule)).Where(u => !ApprovalRouting.Same(u, approval.SubmittedByUserId)).ToList();
				if (recipients.Count > 0 && _notifications != null)
				{
					try { await _notifications.NotifyAwaitingAsync(approval, recipients, false, cancellationToken); }
					catch (Exception ex) { Logging.LogException(ex, "An approval notification could not be sent."); }
				}
				announced++;
			}
			return announced;
		}

		public async Task<int> EscalateDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var escalated = 0;
			foreach (var step in await _steps.GetDueEscalationsAsync(utcNow, Math.Max(1, batchSize)) ?? new List<ApprovalRequestStep>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (await EscalateAsync(step, utcNow, cancellationToken))
						escalated++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An approval step could not be escalated.");
				}
			}
			return escalated;
		}

		private async Task<bool> EscalateAsync(ApprovalRequestStep step, DateTime utcNow, CancellationToken cancellationToken)
		{
			var approval = await _requests.GetForDepartmentAsync(step.DepartmentId, step.ApprovalRequestId);
			if (approval == null || !approval.IsPending || !approval.SignatureRequestId.HasValue)
				return false;
			var request = await _signatures.GetAsync(approval.DepartmentId, approval.SignatureRequestId.Value);
			var signers = request == null ? new List<SignatureRequestSigner>() : await _signatures.GetSignersAsync(approval.DepartmentId, request.SignatureRequestId) ?? new List<SignatureRequestSigner>();
			var signer = signers.FirstOrDefault(s => s.SignatureRequestSignerId == step.SignatureRequestSignerId);
			// Claimed before acting: two hosts never both escalate one step.
			if (!await _steps.ClaimEscalationAsync(step.ApprovalRequestStepId, utcNow, cancellationToken))
				return false;
			if (request == null || !request.IsOpen || signer == null || signer.IsDone || !SignatureOrdering.IsTurn(signer, signers, request.Ordered))
				return false;

			var chainStep = (await _chainSteps.GetForChainAsync(approval.DepartmentId, approval.ApprovalChainId) ?? new List<ApprovalChainStep>())
				.FirstOrDefault(s => s.ApprovalChainStepId == step.ApprovalChainStepId);
			var target = new SignerEligibility { ExcludedUserIds = SignerEligibility.Parse(signer.EligibilityJson)?.ExcludedUserIds ?? new List<string>() };
			switch ((ApprovalEscalationTargets)(chainStep?.EscalateTo ?? (int)ApprovalEscalationTargets.DepartmentAdmins))
			{
				case ApprovalEscalationTargets.Role when chainStep?.EscalateToRoleId > 0:
					target.PersonnelRoleIds.Add(chainStep.EscalateToRoleId.Value);
					break;
				case ApprovalEscalationTargets.NamedMember when !string.IsNullOrWhiteSpace(chainStep?.EscalateToUserId):
					target.UserIds.Add(chainStep.EscalateToUserId.Trim());
					break;
				default:
					target.DepartmentAdmins = true;
					break;
			}

			// The escalation target may approve the step too; the original approvers keep it.
			var rule = SignerEligibility.Parse(signer.EligibilityJson) ?? new SignerEligibility();
			rule.PersonnelRoleIds.AddRange(target.PersonnelRoleIds.Where(r => !rule.PersonnelRoleIds.Contains(r)));
			foreach (var user in target.UserIds)
				AddUser(rule, user);
			rule.DepartmentAdmins |= target.DepartmentAdmins;
			signer.EligibilityJson = rule.ToJson();
			await _signers.UpdateAsync(signer, cancellationToken, true);
			step.EscalatedOn = utcNow;

			var recipients = (await EligibleUsersAsync(approval.DepartmentId, target)).Where(u => !ApprovalRouting.Same(u, approval.SubmittedByUserId)).ToList();
			if (recipients.Count > 0 && _notifications != null)
			{
				try { await _notifications.NotifyAwaitingAsync(approval, recipients, true, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, "An approval escalation notification could not be sent."); }
			}

			Audit(approval.DepartmentId, null, AuditLogTypes.ApprovalEscalated, new { approval.ApprovalRequestId, step.ApprovalRequestStepId, step.Label, chainStep?.EscalateTo, Recipients = recipients.Count });
			Publish(approval, ApprovalWorkflowChanges.Escalated, null, step.Label, null);
			return true;
		}

		public async Task<int> SyncClosedAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var synced = 0;
			foreach (var approval in await _requests.GetPendingWithClosedSignatureAsync(Math.Max(1, batchSize)) ?? new List<ApprovalRequest>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var request = await _signatures.GetAsync(approval.DepartmentId, approval.SignatureRequestId.Value);
					if (request == null || request.IsOpen)
						continue;
					switch ((SignatureRequestStatuses)request.Status)
					{
						case SignatureRequestStatuses.Completed:
							await HandleSignatureCompletedAsync(request, cancellationToken);
							break;
						case SignatureRequestStatuses.Declined:
							// Declined on the shared signing page rather than here: the decliner and their reason are on the step.
							var signers = await _signatures.GetSignersAsync(approval.DepartmentId, request.SignatureRequestId) ?? new List<SignatureRequestSigner>();
							var decliner = signers.FirstOrDefault(s => s.Status == (int)SignatureSignerStatuses.Declined);
							await MarkRejectedAsync(approval, decliner?.UserId, Clean(decliner?.DeclineReason ?? request.DeclineReason, 1000), cancellationToken);
							break;
						case SignatureRequestStatuses.Expired:
							if (await _requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Expired, utcNow, cancellationToken))
							{
								approval.Status = (int)ApprovalRequestStatuses.Expired;
								approval.DecidedOn = utcNow;
								await _requests.UpdateAsync(approval, cancellationToken, true);
								await NotifyDecidedAsync(approval);
							}
							break;
						default:
							if (await _requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Withdrawn, utcNow, cancellationToken))
								Audit(approval.DepartmentId, request.VoidedByUserId, AuditLogTypes.ApprovalWithdrawn, new { approval.ApprovalRequestId, approval.SubjectKind, approval.SubjectId });
							break;
					}
					synced++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An approval could not be synced with its signature request.");
				}
			}
			return synced;
		}

		/// <summary>The active members a rule makes eligible (department admins resolved directly; others through the S3 resolver).</summary>
		private async Task<List<string>> EligibleUsersAsync(int departmentId, SignerEligibility rule)
		{
			if (rule == null || rule.IsEmpty || _eligibility == null)
				return new List<string>();
			try
			{
				return await _eligibility.GetEligibleUserIdsAsync(departmentId, rule) ?? new List<string>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The approvers of a step could not be resolved.");
				return new List<string>();
			}
		}

		#endregion

		#region Helpers

		private async Task NotifyDecidedAsync(ApprovalRequest approval)
		{
			if (_notifications == null)
				return;
			try { await _notifications.NotifyDecidedAsync(approval, CancellationToken.None); }
			catch (Exception ex) { Logging.LogException(ex, "An approval decision notification could not be sent."); }
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, object after, string ipAddress = null, string userAgent = null)
		{
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = JsonConvert.SerializeObject(after),
				Successful = true,
				IpAddress = ipAddress,
				UserAgent = userAgent,
				ServerName = Environment.MachineName
			});
		}

		private void Publish(ApprovalRequest approval, ApprovalWorkflowChanges change, string userId, string stepLabel, string reason) =>
			_events?.SendMessage(BuildEvent(approval, change, userId, stepLabel, reason));

		/// <summary>The workflow payload: ids, the subject's title and amount, the step and the reason; never contact details.</summary>
		public static ApprovalWorkflowEvent BuildEvent(ApprovalRequest approval, ApprovalWorkflowChanges change, string userId, string stepLabel, string reason) => new ApprovalWorkflowEvent
		{
			DepartmentId = approval.DepartmentId,
			Change = change,
			ApprovalRequestId = approval.ApprovalRequestId,
			SubjectKind = ((SignatureSubjectKinds)approval.SubjectKind).ToString(),
			SubjectId = approval.SubjectId,
			Title = approval.Title,
			Amount = approval.Amount,
			Currency = approval.Currency,
			SubmittedByUserId = approval.SubmittedByUserId,
			UserId = userId,
			StepLabel = stepLabel,
			Reason = reason,
			ContentCode = SignaturePolicy.ShortCode(approval.ContentSha256),
			OccurredOn = DateTime.UtcNow
		};

		private static string Clean(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
		}

		#endregion
	}
}
