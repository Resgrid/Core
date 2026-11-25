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
using Resgrid.Model.Helpers;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Approvals
{
	/// <summary>
	/// "Approve before …" (S4). Required only where the department has an active chain for the kind, the owning module is available and
	/// some step applies to the subject's amount; approved only when the latest request is granted and the subject still hashes to the
	/// content that was approved. Reads repositories and the module sources only (no signing service), so modules can ask it without a
	/// dependency cycle.
	/// </summary>
	public class ApprovalGateService : IApprovalGateService
	{
		private readonly IApprovalChainsRepository _chains;
		private readonly IApprovalChainStepsRepository _chainSteps;
		private readonly IApprovalRequestsRepository _requests;
		private readonly IEnumerable<IApprovalSubjectSource> _sources;

		public ApprovalGateService(IApprovalChainsRepository chains, IApprovalChainStepsRepository chainSteps, IApprovalRequestsRepository requests,
			IEnumerable<IApprovalSubjectSource> sources)
		{
			_chains = chains;
			_chainSteps = chainSteps;
			_requests = requests;
			_sources = sources ?? Enumerable.Empty<IApprovalSubjectSource>();
		}

		public async Task<ApprovalGateResult> CheckAsync(int departmentId, SignatureSubjectKinds kind, string subjectId)
		{
			var result = new ApprovalGateResult();
			var source = _sources.FirstOrDefault(s => s.Kind == kind);
			if (source == null || departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return result;

			var chain = await _chains.GetActiveForKindAsync(departmentId, (int)kind);
			if (chain == null || chain.DepartmentId != departmentId || !await source.IsAvailableAsync(departmentId))
				return result;

			var info = await source.DescribeAsync(departmentId, subjectId);
			if (info == null)
				return result;
			var steps = await _chainSteps.GetForChainAsync(departmentId, chain.ApprovalChainId) ?? new List<ApprovalChainStep>();
			if (ApprovalRouting.Resolve(steps, info.Amount).Count == 0)
				return result;

			result.Required = true;
			result.Latest = (await _requests.GetForSubjectAsync(departmentId, (int)kind, info.SubjectId) ?? new List<ApprovalRequest>()).FirstOrDefault(r => r.DepartmentId == departmentId);
			var granted = result.Latest != null && result.Latest.Status == (int)ApprovalRequestStatuses.Approved;
			var same = result.Latest != null && SignatureTokens.HashEquals(result.Latest.ContentSha256, info.ContentSha256);
			result.Approved = granted && same;
			result.ChangedSinceApproval = granted && !same;
			return result;
		}
	}

	/// <summary>
	/// The signing layer's view of an approval kind (one instance per kind): the S3 request describes the subject through the kind's
	/// module source (the content hash every approval binds to and the document the completed chain seals), and its completion hands
	/// back to the approval service.
	/// </summary>
	public class ApprovalSignatureSubjectSource : ISignatureSubjectSource
	{
		private readonly IEnumerable<IApprovalSubjectSource> _sources;
		private readonly Lazy<IApprovalChainService> _approvals;

		public ApprovalSignatureSubjectSource(SignatureSubjectKinds kind, IEnumerable<IApprovalSubjectSource> sources, Lazy<IApprovalChainService> approvals)
		{
			Kind = kind;
			_sources = sources ?? Enumerable.Empty<IApprovalSubjectSource>();
			_approvals = approvals;
		}

		public SignatureSubjectKinds Kind { get; }

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var source = _sources.FirstOrDefault(s => s.Kind == Kind);
			var info = source == null ? null : await source.DescribeAsync(departmentId, subjectId);
			if (info == null)
				return null;
			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = info.SubjectId,
				Title = info.Amount.HasValue ? ((info.Title ?? string.Empty) + " " + ApprovalRouting.AmountText(info.Amount, info.Currency)).Trim() : info.Title,
				RevisionId = info.RevisionId,
				RevisionSha256 = info.ContentSha256,
				DocumentHtml = info.DocumentHtml,
				RetentionYears = info.RetentionYears
			};
		}

		public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default) =>
			_approvals.Value.HandleSignatureCompletedAsync(request, cancellationToken);
	}

	/// <summary>
	/// Approval notifications (EventTypes 73-74): push + email through ICommunicationService with "AP:{approvalRequestId}" and the web link.
	/// Active members only, nothing where the department cannot transmit; the text names the subject, its amount and content code.
	/// </summary>
	public class ApprovalNotificationService : IApprovalNotificationService
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;

		public ApprovalNotificationService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
		}

		public static string Link(int approvalRequestId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Approvals/Detail?id=" + approvalRequestId.ToString(CultureInfo.InvariantCulture);

		public static string EventCode(int approvalRequestId) => "AP:" + approvalRequestId.ToString(CultureInfo.InvariantCulture);

		private static string Subject(ApprovalRequest r) =>
			(r.Title + (r.Amount.HasValue ? " (" + ApprovalRouting.AmountText(r.Amount, r.Currency) + ")" : string.Empty)).Trim();

		public static string AwaitingMessage(ApprovalRequest r, bool escalated) =>
			(escalated ? "Escalated: your approval is needed on " : "Your approval is needed on ") + Subject(r) + " (content code " + SignaturePolicy.ShortCode(r.ContentSha256) + "). " + Link(r.ApprovalRequestId);

		public static string DecidedMessage(ApprovalRequest r)
		{
			var outcome = (ApprovalRequestStatuses)r.Status switch
			{
				ApprovalRequestStatuses.Approved => "was approved",
				ApprovalRequestStatuses.Rejected => "was rejected: " + (r.RejectReason ?? string.Empty),
				ApprovalRequestStatuses.Expired => "expired before it was approved",
				_ => "was closed"
			};
			return "Your approval request for " + Subject(r) + " " + outcome + " " + Link(r.ApprovalRequestId);
		}

		public Task<int> NotifyAwaitingAsync(ApprovalRequest request, IReadOnlyCollection<string> userIds, bool escalated, CancellationToken cancellationToken = default) =>
			request == null ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, userIds, escalated ? "Approval escalated" : "Approval needed", AwaitingMessage(request, escalated), EventCode(request.ApprovalRequestId), cancellationToken);

		public Task<int> NotifyDecidedAsync(ApprovalRequest request, CancellationToken cancellationToken = default) =>
			request == null || string.IsNullOrWhiteSpace(request.SubmittedByUserId) ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, new[] { request.SubmittedByUserId }, "Approval decided", DecidedMessage(request), EventCode(request.ApprovalRequestId), cancellationToken);

		private async Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string title, string message, string eventCode, CancellationToken cancellationToken)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return 0;
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			// Inactive-member policy: deleted, disabled and hidden members are never addressed by automation.
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var sent = 0;
			foreach (var userId in wanted.Where(active.Contains))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, null, false, eventCode))
						sent++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Approval notification to {userId} in department {departmentId} failed.");
				}
			}
			return sent;
		}
	}

	/// <summary>
	/// The Incident Back Office ledger's approval gate (S4): where the department has an active chain for requisitions or plans, moving one
	/// to Approved needs the chain's approval of the content as it is now. Registered beside the module's own rules (its separation of
	/// duties and spending delegations still apply).
	/// </summary>
	public sealed class ApprovalChainLedgerRule<T> : IIncidentLedgerRule<T> where T : class, IIncidentLedgerEntity
	{
		private readonly IApprovalGateService _gate;
		private readonly SignatureSubjectKinds _kind;
		private readonly int _approvedStatus;
		private readonly Func<T, string> _id;

		public ApprovalChainLedgerRule(IApprovalGateService gate, SignatureSubjectKinds kind, int approvedStatus, Func<T, string> id)
		{
			_gate = gate;
			_kind = kind;
			_approvedStatus = approvedStatus;
			_id = id;
		}

		public async Task BeforeSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken)
		{
			if (change == null || change.IsDelete || !change.IsTransition || change.OldStatus == change.NewStatus || change.NewStatus != _approvedStatus)
				return;
			var gate = await _gate.CheckAsync(change.Actor.DepartmentId, _kind, _id(change.Entity));
			if (!gate.Allows)
				throw new IncidentRuleException("ApprovalChainRequired");
		}

		public Task AfterSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken) => Task.CompletedTask;
	}

	/// <summary>Mutual-aid agreement documents (S4). Plain-text terms; an approved agreement is not edited (copy it into a new draft).</summary>
	public class MutualAidAgreementService : IMutualAidAgreementService
	{
		private readonly IMutualAidAgreementsRepository _agreements;
		private readonly IEventAggregator _events;

		public MutualAidAgreementService(IMutualAidAgreementsRepository agreements, IEventAggregator events)
		{
			_agreements = agreements;
			_events = events;
		}

		public async Task<List<MutualAidAgreement>> GetAgreementsAsync(int departmentId) =>
			(await _agreements.GetAllForDepartmentAsync(departmentId) ?? new List<MutualAidAgreement>()).Where(a => a.DepartmentId == departmentId).ToList();

		public async Task<MutualAidAgreement> GetAgreementAsync(int departmentId, int mutualAidAgreementId)
		{
			var agreement = await _agreements.GetForDepartmentAsync(departmentId, mutualAidAgreementId);
			return agreement?.DepartmentId == departmentId ? agreement : null;
		}

		public async Task<MutualAidAgreement> SaveAsync(MutualAidAgreement agreement, string userId, CancellationToken cancellationToken = default)
		{
			if (agreement == null || agreement.DepartmentId <= 0 || string.IsNullOrWhiteSpace(agreement.Title) || string.IsNullOrWhiteSpace(userId))
				return null;
			if (agreement.EffectiveOn.HasValue && agreement.ExpiresOn.HasValue && agreement.ExpiresOn.Value < agreement.EffectiveOn.Value)
				return null;

			var now = DateTime.UtcNow;
			MutualAidAgreement saved;
			if (agreement.MutualAidAgreementId > 0)
			{
				var existing = await GetAgreementAsync(agreement.DepartmentId, agreement.MutualAidAgreementId);
				if (existing == null || existing.Status == (int)MutualAidAgreementStatuses.Approved || existing.Status == (int)MutualAidAgreementStatuses.Withdrawn)
					return null;
				Apply(existing, agreement);
				// An edit voids a pending approval (its content hash changes); the agreement is a draft again until it is submitted.
				existing.Status = (int)MutualAidAgreementStatuses.Draft;
				existing.UpdatedByUserId = userId;
				existing.UpdatedOn = now;
				saved = await _agreements.UpdateAsync(existing, cancellationToken, true);
			}
			else
			{
				var fresh = new MutualAidAgreement { DepartmentId = agreement.DepartmentId, Status = (int)MutualAidAgreementStatuses.Draft, CreatedByUserId = userId, CreatedOn = now };
				Apply(fresh, agreement);
				saved = await _agreements.InsertAsync(fresh, cancellationToken, true);
			}

			Audit(saved, userId, "saved");
			return saved;
		}

		private static void Apply(MutualAidAgreement target, MutualAidAgreement source)
		{
			target.Title = Trim(source.Title, 200);
			target.PartnerName = Trim(source.PartnerName, 200);
			target.PartnerDepartmentId = source.PartnerDepartmentId > 0 ? source.PartnerDepartmentId : null;
			target.DepartmentLinkId = source.DepartmentLinkId > 0 ? source.DepartmentLinkId : null;
			target.Terms = Trim(source.Terms, MutualAidAgreement.TermsMaxLength);
			target.EffectiveOn = source.EffectiveOn;
			target.ExpiresOn = source.ExpiresOn;
		}

		public async Task<bool> DeleteAsync(int departmentId, int mutualAidAgreementId, string userId, CancellationToken cancellationToken = default)
		{
			var existing = await GetAgreementAsync(departmentId, mutualAidAgreementId);
			if (existing == null || existing.Status == (int)MutualAidAgreementStatuses.Approved)
				return false;
			existing.IsDeleted = true;
			existing.UpdatedByUserId = userId;
			existing.UpdatedOn = DateTime.UtcNow;
			await _agreements.UpdateAsync(existing, cancellationToken, true);
			Audit(existing, userId, "deleted");
			return true;
		}

		private void Audit(MutualAidAgreement agreement, string userId, string action) =>
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = agreement.DepartmentId,
				UserId = userId,
				Type = AuditLogTypes.MutualAidAgreementChanged,
				After = JsonConvert.SerializeObject(new { agreement.MutualAidAgreementId, agreement.Title, agreement.PartnerName, agreement.DepartmentLinkId, agreement.Status, Action = action }),
				Successful = true,
				ServerName = Environment.MachineName
			});

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var t = value.Trim();
			return t.Length <= max ? t : t.Substring(0, max);
		}
	}
}
