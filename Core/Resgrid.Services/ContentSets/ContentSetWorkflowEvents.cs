using System;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;

namespace Resgrid.Services.ContentSets
{
	/// <summary>Raises the round 10 A3 set, sponsorship and countersignature workflow events (triggers 286-295). Never fatal.</summary>
	internal static class ContentSetWorkflowEvents
	{
		public static void Raise(IEventAggregator events, int departmentId, ContentSetWorkflowChanges change, ContentSet set, ContentSetItemKinds? itemKind, string itemTitle,
			int publisherDepartmentId, int subscriberDepartmentId, string versionLabel, DateTime? effectiveOn, string state, string actorUserId)
		{
			if (departmentId <= 0 || set == null)
				return;
			ContentWorkflowContext.Publish(events, new ContentSetWorkflowEvent
			{
				DepartmentId = departmentId,
				Change = change,
				SetId = set.ContentSetId,
				SetName = set.Name,
				SetKind = ((ContentSetKinds)set.Kind).ToString(),
				ItemKind = itemKind?.ToString(),
				ItemTitle = itemTitle,
				PublisherDepartmentId = publisherDepartmentId,
				SubscriberDepartmentId = subscriberDepartmentId,
				VersionLabel = versionLabel,
				EffectiveOn = effectiveOn,
				State = state,
				ActorUserId = actorUserId,
				OccurredOn = DateTime.UtcNow
			});
		}

		public static void Sponsorship(IEventAggregator events, int departmentId, LmsSponsorshipWorkflowChanges change, Model.Lms.LmsSponsorship sponsorship, string otherDepartment,
			string actorUserId)
		{
			if (departmentId <= 0 || sponsorship == null)
				return;
			ContentWorkflowContext.Publish(events, new LmsSponsorshipWorkflowEvent
			{
				DepartmentId = departmentId,
				Change = change,
				SponsorshipId = sponsorship.LmsSponsorshipId,
				SponsorDepartmentId = sponsorship.SponsorDepartmentId,
				SponsoredDepartmentId = sponsorship.DepartmentId,
				OtherDepartment = otherDepartment,
				SeatLimit = sponsorship.SeatLimit ?? 0,
				EndReason = change == LmsSponsorshipWorkflowChanges.Ended ? ((Model.Lms.LmsSponsorshipEndReasons)sponsorship.EndReason).ToString() : null,
				ActorUserId = actorUserId,
				OccurredOn = DateTime.UtcNow
			});
		}

		public static void Countersign(IEventAggregator events, int departmentId, MutualAidCountersignWorkflowChanges change, Model.Approvals.MutualAidAgreement agreement,
			int signatureRequestId, string actorUserId)
		{
			if (departmentId <= 0 || agreement == null)
				return;
			ContentWorkflowContext.Publish(events, new MutualAidCountersignWorkflowEvent
			{
				DepartmentId = departmentId,
				Change = change,
				MutualAidAgreementId = agreement.MutualAidAgreementId,
				AgreementTitle = agreement.Title,
				OriginDepartmentId = agreement.DepartmentId,
				PartnerDepartmentId = agreement.PartnerDepartmentId ?? 0,
				DepartmentLinkId = agreement.DepartmentLinkId ?? 0,
				SignatureRequestId = signatureRequestId,
				State = ((Model.Approvals.MutualAidCountersignStatuses)agreement.CountersignStatus).ToString(),
				ActorUserId = actorUserId,
				OccurredOn = DateTime.UtcNow
			});
		}
	}
}
