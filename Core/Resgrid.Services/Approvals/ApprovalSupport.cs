using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.ContentNotices;
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
	/// Active members only, nothing where the department cannot transmit; the text names the subject, its amount and content code. The text
	/// comes from ContentNotices in each recipient's language, the amount written their way (round 9C).
	/// </summary>
	public class ApprovalNotificationService : IApprovalNotificationService
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;

		public ApprovalNotificationService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings,
			IUserProfileService profiles)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_profiles = profiles;
		}

		public static string Link(int approvalRequestId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Approvals/Detail?id=" + approvalRequestId.ToString(CultureInfo.InvariantCulture);

		public static string EventCode(int approvalRequestId) => "AP:" + approvalRequestId.ToString(CultureInfo.InvariantCulture);

		/// <summary>The subject and, when it has one, its amount written the reader's way ("Requisition 12 (1.250,00 EUR)").</summary>
		private static string Subject(ApprovalRequest r, CultureInfo culture) =>
			((r.Title ?? string.Empty) + (r.Amount.HasValue
				? " (" + (r.Amount.Value.ToString("N2", culture ?? ContentNoticeText.English) + (string.IsNullOrWhiteSpace(r.Currency) ? string.Empty : " " + r.Currency.Trim().ToUpperInvariant())) + ")"
				: string.Empty)).Trim();

		/// <summary>"Your approval is needed on ..." in <paramref name="culture"/> (English when none is given).</summary>
		public static string AwaitingMessage(ApprovalRequest r, bool escalated, CultureInfo culture = null) =>
			ContentNoticeText.Format(escalated ? "Approval.Escalated.Message" : "Approval.Needed.Message", culture, Subject(r, culture),
				SignaturePolicy.ShortCode(r.ContentSha256), Link(r.ApprovalRequestId));

		/// <summary>How the request was decided, in <paramref name="culture"/> (English when none is given).</summary>
		public static string DecidedMessage(ApprovalRequest r, CultureInfo culture = null)
		{
			var subject = Subject(r, culture);
			var link = Link(r.ApprovalRequestId);
			return (ApprovalRequestStatuses)r.Status switch
			{
				ApprovalRequestStatuses.Approved => ContentNoticeText.Format("Approval.Decided.Approved", culture, subject, link),
				ApprovalRequestStatuses.Rejected => ContentNoticeText.Format("Approval.Decided.Rejected", culture, subject, r.RejectReason ?? string.Empty, link),
				ApprovalRequestStatuses.Expired => ContentNoticeText.Format("Approval.Decided.Expired", culture, subject, link),
				_ => ContentNoticeText.Format("Approval.Decided.Other", culture, subject, link)
			};
		}

		public Task<int> NotifyAwaitingAsync(ApprovalRequest request, IReadOnlyCollection<string> userIds, bool escalated, CancellationToken cancellationToken = default) =>
			request == null ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, userIds, culture => new ContentNoticeDelivery.Notice(
					ContentNoticeText.Get(escalated ? "Approval.Escalated.Title" : "Approval.Needed.Title", culture), AwaitingMessage(request, escalated, culture)),
					EventCode(request.ApprovalRequestId), cancellationToken);

		public Task<int> NotifyDecidedAsync(ApprovalRequest request, CancellationToken cancellationToken = default) =>
			request == null || string.IsNullOrWhiteSpace(request.SubmittedByUserId) ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, new[] { request.SubmittedByUserId }, culture => new ContentNoticeDelivery.Notice(
					ContentNoticeText.Get("Approval.Decided.Title", culture), DecidedMessage(request, culture)), EventCode(request.ApprovalRequestId), cancellationToken);

		/// <summary>Active members only, each in their own language (<see cref="ContentNoticeDelivery"/>).</summary>
		private Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, Func<CultureInfo, ContentNoticeDelivery.Notice> compose, string eventCode,
			CancellationToken cancellationToken) =>
			ContentNoticeDelivery.SendAsync(_communication.Value, _departments, _settings, _profiles, departmentId, userIds, (culture, _) => compose(culture), eventCode,
				"Approval notification", cancellationToken);
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
		private readonly IDepartmentLinksService _links;

		public MutualAidAgreementService(IMutualAidAgreementsRepository agreements, IEventAggregator events, IDepartmentLinksService links)
		{
			_agreements = agreements;
			_events = events;
			_links = links;
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

			// Round 10 A3: an agreement made over a Department Link names the link's other party as the partner (never a posted id), and only
			// such an agreement can require the partner's countersignature.
			if (agreement.DepartmentLinkId > 0)
			{
				var link = _links == null ? null : await _links.GetLinkByIdAsync(agreement.DepartmentLinkId.Value);
				var other = DepartmentLinkSharing.GetOtherDepartmentId(link, agreement.DepartmentId);
				if (!other.HasValue)
					return null;
				agreement.PartnerDepartmentId = other.Value;
				if (string.IsNullOrWhiteSpace(agreement.PartnerName))
					agreement.PartnerName = link.DepartmentId == other.Value ? link.Department?.Name : link.LinkedDepartment?.Name;
			}
			else
			{
				agreement.RequiresCountersignature = false;
			}

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
			target.RequiresCountersignature = source.RequiresCountersignature && target.DepartmentLinkId.HasValue;
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
