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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.ContentSets;

namespace Resgrid.Services.Approvals
{
	/// <summary>
	/// The subject of a partner countersignature (round 10 A3): "{originDepartmentId}:{agreementId}". The request lives in the partner department.
	/// </summary>
	public static class MutualAidCountersignSubject
	{
		public static string For(int originDepartmentId, int mutualAidAgreementId) =>
			originDepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + mutualAidAgreementId.ToString(CultureInfo.InvariantCulture);

		public static bool TryParse(string subjectId, out int originDepartmentId, out int mutualAidAgreementId)
		{
			originDepartmentId = 0;
			mutualAidAgreementId = 0;
			var parts = (subjectId ?? string.Empty).Split(':');
			return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out originDepartmentId) &&
				int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out mutualAidAgreementId) && originDepartmentId > 0 && mutualAidAgreementId > 0;
		}

		/// <summary>
		/// Whether the partner may see and countersign the agreement over its link now: the agreement names the link and the partner, the link is
		/// enabled and its parties are exactly the two departments, and the department's chain approved the agreement.
		/// </summary>
		public static bool IsRoutable(MutualAidAgreement agreement, DepartmentLink link, int partnerDepartmentId) =>
			agreement != null && !agreement.IsDeleted && agreement.RequiresCountersignature && agreement.Status == (int)MutualAidAgreementStatuses.Approved &&
			agreement.DepartmentLinkId.HasValue && link != null && link.DepartmentLinkId == agreement.DepartmentLinkId.Value && link.LinkEnabled &&
			agreement.PartnerDepartmentId == partnerDepartmentId && partnerDepartmentId != agreement.DepartmentId &&
			DepartmentLinkSharing.GetOtherDepartmentId(link, agreement.DepartmentId) == partnerDepartmentId;
	}

	/// <summary>Mutual-aid countersignature over a Department Link (round 10 A3). See <see cref="IMutualAidCountersignService"/>.</summary>
	public class MutualAidCountersignService : IMutualAidCountersignService
	{
		private readonly IMutualAidAgreementsRepository _agreements;
		private readonly IDepartmentLinksService _links;
		private readonly IDepartmentsService _departments;
		// The signing layer reaches the approval chains (and so this service) through its subject sources: Lazy keeps construction acyclic.
		private readonly Lazy<ISignatureRequestService> _requests;
		private readonly IEventAggregator _events;

		public MutualAidCountersignService(IMutualAidAgreementsRepository agreements, IDepartmentLinksService links, IDepartmentsService departments,
			Lazy<ISignatureRequestService> requests, IEventAggregator events)
		{
			_agreements = agreements;
			_links = links;
			_departments = departments;
			_requests = requests;
			_events = events;
		}

		public async Task<MutualAidAgreement> RequestAsync(int departmentId, int mutualAidAgreementId, string userId, CancellationToken cancellationToken = default)
		{
			var agreement = await _agreements.GetForDepartmentAsync(departmentId, mutualAidAgreementId);
			if (agreement == null || agreement.DepartmentId != departmentId || !agreement.RequiresCountersignature ||
				agreement.CountersignStatus != (int)MutualAidCountersignStatuses.None || !agreement.PartnerDepartmentId.HasValue || !agreement.DepartmentLinkId.HasValue)
				return agreement;

			var link = await _links.GetLinkByIdAsync(agreement.DepartmentLinkId.Value);
			if (!MutualAidCountersignSubject.IsRoutable(agreement, link, agreement.PartnerDepartmentId.Value))
				return agreement;

			var result = await _requests.Value.CreateMultiSignerAsync(new MultiSignerRequestInput
			{
				DepartmentId = agreement.PartnerDepartmentId.Value,
				SubjectKind = SignatureSubjectKinds.MutualAidAgreementCountersignature,
				SubjectId = MutualAidCountersignSubject.For(departmentId, agreement.MutualAidAgreementId),
				Ordered = true,
				ExternalChannel = SignatureRequestChannels.InPerson,
				ExpiresInDays = 30,
				RequestedByUserId = userId,
				Steps = new List<SignatureStepInput>
				{
					new SignatureStepInput
					{
						Kind = SignatureSignerKinds.Member,
						Role = SignatureSignerRoles.Member,
						Order = 1,
						// Any of the partner's department admins, at L2 against the same content hash the department's chain signed.
						Eligibility = new SignerEligibility { DepartmentAdmins = true },
						Level = SignatureLevels.StepUp,
						StepLabel = "Partner department admin"
					}
				}
			}, cancellationToken);

			if (result?.Request == null)
			{
				Logging.LogError($"Mutual-aid agreement {agreement.MutualAidAgreementId}: the partner's countersignature request was refused ({result?.Refusal}).");
				return agreement;
			}

			agreement.CountersignStatus = (int)MutualAidCountersignStatuses.Requested;
			agreement.CountersignRequestId = result.Request.SignatureRequestId;
			agreement.CountersignRequestedOn = DateTime.UtcNow;
			agreement.UpdatedOn = DateTime.UtcNow;
			agreement = await _agreements.UpdateAsync(agreement, cancellationToken, true);
			Audit(agreement, userId, "countersignature requested");
			ContentSetWorkflowEvents.Countersign(_events, agreement.DepartmentId, MutualAidCountersignWorkflowChanges.Requested, agreement, result.Request.SignatureRequestId, userId);
			ContentSetWorkflowEvents.Countersign(_events, agreement.PartnerDepartmentId.Value, MutualAidCountersignWorkflowChanges.Requested, agreement, result.Request.SignatureRequestId, userId);
			return agreement;
		}

		public async Task<MutualAidAgreement> RefreshAsync(MutualAidAgreement agreement, CancellationToken cancellationToken = default)
		{
			if (agreement == null || agreement.CountersignStatus != (int)MutualAidCountersignStatuses.Requested || !agreement.CountersignRequestId.HasValue ||
				!agreement.PartnerDepartmentId.HasValue)
				return agreement;

			try
			{
				var request = await _requests.Value.GetAsync(agreement.PartnerDepartmentId.Value, agreement.CountersignRequestId.Value);
				if (request == null || request.IsOpen)
					return agreement;

				if (request.Status == (int)SignatureRequestStatuses.Completed)
					return await MarkCountersignedAsync(agreement, null, request.CompletedOn ?? DateTime.UtcNow, cancellationToken);

				agreement.CountersignStatus = (int)(request.Status == (int)SignatureRequestStatuses.Voided ? MutualAidCountersignStatuses.Lapsed : MutualAidCountersignStatuses.Declined);
				agreement.CountersignNote = Cut(request.DeclineReason ?? request.VoidReason, 1000);
				agreement.UpdatedOn = DateTime.UtcNow;
				agreement = await _agreements.UpdateAsync(agreement, cancellationToken, true);
				ContentSetWorkflowEvents.Countersign(_events, agreement.DepartmentId, MutualAidCountersignWorkflowChanges.Declined, agreement, request.SignatureRequestId, null);
				return agreement;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The partner countersignature state could not be read.");
				return agreement;
			}
		}

		/// <summary>The partner's step was signed (the request completed): the agreement records the countersignature.</summary>
		public async Task<MutualAidAgreement> MarkCountersignedAsync(MutualAidAgreement agreement, string partnerUserId, DateTime signedOnUtc, CancellationToken cancellationToken)
		{
			if (agreement == null || agreement.CountersignStatus == (int)MutualAidCountersignStatuses.Countersigned)
				return agreement;
			agreement.CountersignStatus = (int)MutualAidCountersignStatuses.Countersigned;
			agreement.CountersignedOn = signedOnUtc;
			agreement.CountersignedByUserId = partnerUserId ?? agreement.CountersignedByUserId;
			agreement.UpdatedOn = DateTime.UtcNow;
			agreement = await _agreements.UpdateAsync(agreement, cancellationToken, true);
			Audit(agreement, partnerUserId, "countersigned");
			ContentSetWorkflowEvents.Countersign(_events, agreement.DepartmentId, MutualAidCountersignWorkflowChanges.Countersigned, agreement, agreement.CountersignRequestId ?? 0, partnerUserId);
			if (agreement.PartnerDepartmentId.HasValue)
				ContentSetWorkflowEvents.Countersign(_events, agreement.PartnerDepartmentId.Value, MutualAidCountersignWorkflowChanges.Countersigned, agreement, agreement.CountersignRequestId ?? 0,
					partnerUserId);
			return agreement;
		}

		public async Task<List<MutualAidCountersignItem>> GetAwaitingAsync(int partnerDepartmentId)
		{
			var items = new List<MutualAidCountersignItem>();
			if (partnerDepartmentId <= 0)
				return items;

			foreach (var agreement in await _agreements.GetAwaitingCountersignatureAsync(partnerDepartmentId) ?? new List<MutualAidAgreement>())
			{
				if (!agreement.DepartmentLinkId.HasValue)
					continue;
				var link = await _links.GetLinkByIdAsync(agreement.DepartmentLinkId.Value);
				if (!MutualAidCountersignSubject.IsRoutable(agreement, link, partnerDepartmentId))
					continue;
				var request = agreement.CountersignRequestId.HasValue ? await _requests.Value.GetAsync(partnerDepartmentId, agreement.CountersignRequestId.Value) : null;
				items.Add(new MutualAidCountersignItem
				{
					Agreement = agreement,
					Request = request,
					OriginDepartmentName = (await _departments.GetDepartmentByIdAsync(agreement.DepartmentId, false))?.Name
				});
			}

			return items;
		}

		public async Task<int> LapseForLinkAsync(int? departmentLinkId, CancellationToken cancellationToken = default)
		{
			var lapsed = 0;
			var requested = departmentLinkId.HasValue
				? await _agreements.GetCountersignRequestedForLinkAsync(departmentLinkId.Value) ?? new List<MutualAidAgreement>()
				: await _agreements.GetAllCountersignRequestedAsync(2000) ?? new List<MutualAidAgreement>();

			foreach (var agreement in requested)
			{
				try
				{
					var link = agreement.DepartmentLinkId.HasValue ? await _links.GetLinkByIdAsync(agreement.DepartmentLinkId.Value) : null;
					if (agreement.PartnerDepartmentId.HasValue && MutualAidCountersignSubject.IsRoutable(agreement, link, agreement.PartnerDepartmentId.Value))
						continue;

					if (agreement.CountersignRequestId.HasValue && agreement.PartnerDepartmentId.HasValue)
					{
						try
						{
							await _requests.Value.VoidAsync(agreement.PartnerDepartmentId.Value, agreement.CountersignRequestId.Value, null, "The Department Link was disabled or removed.",
								cancellationToken);
						}
						catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
						{
							throw;
						}
						catch (Exception ex)
						{
							// The partner department (or its request) may be gone: the agreement still lapses, so the sweep does not retry it forever.
							Logging.LogException(ex, $"Mutual-aid agreement {agreement.MutualAidAgreementId}: the partner's request could not be voided.");
						}
					}
					agreement.CountersignStatus = (int)MutualAidCountersignStatuses.Lapsed;
					agreement.CountersignNote = "link";
					agreement.UpdatedOn = DateTime.UtcNow;
					await _agreements.UpdateAsync(agreement, cancellationToken, true);
					lapsed++;
					Audit(agreement, null, "countersignature lapsed");
					ContentSetWorkflowEvents.Countersign(_events, agreement.DepartmentId, MutualAidCountersignWorkflowChanges.Declined, agreement, agreement.CountersignRequestId ?? 0, null);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Mutual-aid agreement {agreement.MutualAidAgreementId}: the lapsed countersignature could not be voided.");
				}
			}

			return lapsed;
		}

		private void Audit(MutualAidAgreement agreement, string userId, string action) =>
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = agreement.DepartmentId,
				UserId = userId,
				Type = AuditLogTypes.MutualAidAgreementChanged,
				After = JsonConvert.SerializeObject(new
				{
					agreement.MutualAidAgreementId, agreement.PartnerDepartmentId, agreement.DepartmentLinkId, agreement.CountersignStatus, agreement.CountersignRequestId, Action = action
				}),
				Successful = true,
				ServerName = Environment.MachineName
			});

		private static string Cut(string value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim().Substring(0, max);
	}

	/// <summary>
	/// The partner's countersignature subject (round 10 A3, kind 26): the origin's agreement read over the link, with the same content hash the
	/// origin's approval chain signed, so both signatures bind to one document. Described in the partner department only while
	/// <see cref="MutualAidCountersignSubject.IsRoutable"/> holds.
	/// </summary>
	public class MutualAidCountersignatureSource : ISignatureSubjectSource
	{
		private readonly IMutualAidAgreementsRepository _agreements;
		private readonly IDepartmentLinksService _links;
		private readonly Lazy<IMutualAidCountersignService> _countersign;

		public MutualAidCountersignatureSource(IMutualAidAgreementsRepository agreements, IDepartmentLinksService links, Lazy<IMutualAidCountersignService> countersign)
		{
			_agreements = agreements;
			_links = links;
			_countersign = countersign;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.MutualAidAgreementCountersignature;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var agreement = await ReadAsync(departmentId, subjectId);
			if (agreement == null)
				return null;
			var described = MutualAidAgreementApprovalSource.Describe(agreement);
			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = subjectId,
				Title = agreement.Title,
				RevisionId = "agreement-" + agreement.MutualAidAgreementId.ToString(CultureInfo.InvariantCulture),
				RevisionSha256 = described.ContentSha256,
				DocumentHtml = described.DocumentHtml,
				RetentionYears = described.RetentionYears
			};
		}

		/// <summary>The origin's agreement when the partner department may countersign it over the link now; null otherwise.</summary>
		internal async Task<MutualAidAgreement> ReadAsync(int partnerDepartmentId, string subjectId)
		{
			if (partnerDepartmentId <= 0 || !MutualAidCountersignSubject.TryParse(subjectId, out var originId, out var agreementId) || originId == partnerDepartmentId)
				return null;
			var agreement = await _agreements.GetForDepartmentAsync(originId, agreementId);
			if (agreement == null || agreement.DepartmentId != originId || !agreement.DepartmentLinkId.HasValue)
				return null;
			if (agreement.CountersignStatus != (int)MutualAidCountersignStatuses.Requested && agreement.CountersignStatus != (int)MutualAidCountersignStatuses.Countersigned)
				return null;
			var link = await _links.GetLinkByIdAsync(agreement.DepartmentLinkId.Value);
			return MutualAidCountersignSubject.IsRoutable(agreement, link, partnerDepartmentId) ? agreement : null;
		}

		public async Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (request == null)
				return;
			var agreement = await ReadAsync(request.DepartmentId, request.SubjectId);
			if (agreement == null || agreement.CountersignRequestId != request.SignatureRequestId)
				return;
			if (_countersign.Value is MutualAidCountersignService service)
				await service.MarkCountersignedAsync(agreement, signer?.UserId ?? actingUserId, signer?.SignedOn ?? DateTime.UtcNow, cancellationToken);
		}
	}

	/// <summary>Who may open the partner's countersignature request (kind 26): the partner department's admins, while the link routes it.</summary>
	public class MutualAidCountersignatureAccessRule : ISignatureSubjectAccessRule
	{
		private readonly MutualAidCountersignatureSource _source;
		private readonly IDepartmentsService _departments;

		public MutualAidCountersignatureAccessRule(IMutualAidAgreementsRepository agreements, IDepartmentLinksService links, Lazy<IMutualAidCountersignService> countersign,
			IDepartmentsService departments)
		{
			_source = new MutualAidCountersignatureSource(agreements, links, countersign);
			_departments = departments;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.MutualAidAgreementCountersignature;

		public async Task<bool> CanActOnAsync(int departmentId, string userId, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(userId) || await _source.ReadAsync(departmentId, subjectId) == null)
				return false;
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			return department != null && (string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase) || department.IsUserAnAdmin(userId));
		}
	}
}
