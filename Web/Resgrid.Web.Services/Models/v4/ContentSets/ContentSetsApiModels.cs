using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Models.v4.ContentSets
{
	/// <summary>One pending (or decided) set update in the subscriber's inbox.</summary>
	public class ContentSetUpdateData
	{
		public int ContentSetUpdateId { get; set; }
		public int ContentSetSubscriptionId { get; set; }
		public string SetName { get; set; }
		public string PublisherName { get; set; }
		/// <summary>NewVersion or NewItem.</summary>
		public string UpdateKind { get; set; }
		/// <summary>Protocol, LmsCourse, Training or LmsPath.</summary>
		public string ItemKind { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		public string ChangeSummary { get; set; }
		/// <summary>Locked, CustomizableBlocks or Open.</summary>
		public string LockMode { get; set; }
		public bool CanDecline { get; set; }
		/// <summary>Pending, Scheduled, Applied, Deferred, Declined, Superseded or Failed.</summary>
		public string Status { get; set; }
		public DateTime? SourceEffectiveOn { get; set; }
		public DateTime? DeadlineOn { get; set; }
		public DateTime? ApplyOn { get; set; }
		public DateTime? DeferredUntil { get; set; }
		public DateTime? AppliedOn { get; set; }
		public string FailureReason { get; set; }

		public static ContentSetUpdateData From(ContentSetInboxRow row)
		{
			var update = row?.Update;
			if (update == null)
				return null;
			return new ContentSetUpdateData
			{
				ContentSetUpdateId = update.ContentSetUpdateId,
				ContentSetSubscriptionId = update.ContentSetSubscriptionId,
				SetName = row.SetName,
				PublisherName = row.PublisherName,
				UpdateKind = ((ContentSetUpdateKinds)update.UpdateKind).ToString(),
				ItemKind = ((ContentSetItemKinds)update.ItemKind).ToString(),
				Title = update.Title,
				VersionLabel = update.SourceVersionLabel,
				ChangeSummary = update.ChangeSummary,
				LockMode = row.LockMode.ToString(),
				CanDecline = row.CanDecline,
				Status = ((ContentSetUpdateStatuses)update.Status).ToString(),
				SourceEffectiveOn = update.SourceEffectiveOn,
				DeadlineOn = update.DeadlineOn,
				ApplyOn = update.ApplyOn,
				DeferredUntil = update.DeferredUntil,
				AppliedOn = update.AppliedOn,
				FailureReason = update.FailureReason
			};
		}
	}

	public class ContentSetInboxResult : StandardApiResponseV4Base
	{
		public List<ContentSetUpdateData> Data { get; set; } = new List<ContentSetUpdateData>();
	}

	/// <summary>An update with the redline the subscriber reviews: the department's current text and the publisher's candidate (HTML).</summary>
	public class ContentSetUpdateDetailData
	{
		public ContentSetUpdateData Update { get; set; }
		public string CurrentHtml { get; set; }
		public string CandidateHtml { get; set; }
		/// <summary>Local changes the publisher's new version drops (customizable blocks it removed).</summary>
		public List<string> DroppedChanges { get; set; } = new List<string>();
		/// <summary>When accepting now would put it into force (the department's update time on or after the publisher's effective date).</summary>
		public DateTime SuggestedApplyOn { get; set; }
	}

	public class ContentSetUpdateResult : StandardApiResponseV4Base
	{
		public ContentSetUpdateDetailData Data { get; set; }
	}

	public class ContentSetAcceptInput
	{
		public int ContentSetUpdateId { get; set; }
		/// <summary>Optional later go-live (UTC); default the suggested time. Never before the publisher's effective date.</summary>
		public DateTime? ApplyOn { get; set; }
	}

	public class ContentSetDeferInput
	{
		public int ContentSetUpdateId { get; set; }
		/// <summary>UTC; a locked update cannot be deferred past its deadline.</summary>
		public DateTime Until { get; set; }
	}

	public class ContentSetUpdateIdInput
	{
		public int ContentSetUpdateId { get; set; }
	}

	/// <summary>The outcome of a set operation: "success", or Status "failure" with the ContentSetErrors code in Error (and a reason code).</summary>
	public class ContentSetActionResult : StandardApiResponseV4Base
	{
		public string Error { get; set; }
		public string Reason { get; set; }
		public int Id { get; set; }
	}

	public class ContentSetItemData
	{
		public int ContentSetItemId { get; set; }
		public string ItemKind { get; set; }
		public int ItemId { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		public string LockMode { get; set; }
		public bool NeedsLms { get; set; }
		/// <summary>Why it cannot travel now (a ContentSets Problem_* code), or null.</summary>
		public string Problem { get; set; }

		public static ContentSetItemData From(ContentSetItemView view) => view?.Item == null ? null : new ContentSetItemData
		{
			ContentSetItemId = view.Item.ContentSetItemId,
			ItemKind = ((ContentSetItemKinds)view.Item.ItemKind).ToString(),
			ItemId = view.Item.ItemId,
			Title = view.Title,
			VersionLabel = view.VersionLabel,
			LockMode = ((ContentSetLockModes)view.Item.LockMode).ToString(),
			NeedsLms = view.NeedsLms,
			Problem = view.Problem
		};
	}

	/// <summary>A set as its publisher sees it (with its subscribers), or as a linked department may subscribe to it.</summary>
	public class ContentSetData
	{
		public int ContentSetId { get; set; }
		public int PublisherDepartmentId { get; set; }
		public string PublisherName { get; set; }
		/// <summary>Protocols or Training.</summary>
		public string Kind { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int LockedDeferDays { get; set; }
		public bool IsArchived { get; set; }
		public List<ContentSetItemData> Items { get; set; } = new List<ContentSetItemData>();
		/// <summary>Publisher view: the subscribing departments.</summary>
		public List<ContentSetSubscriberData> Subscribers { get; set; } = new List<ContentSetSubscriberData>();
		public int OpenProposals { get; set; }
		/// <summary>Subscriber view: the department's open subscription (0 when none).</summary>
		public int ContentSetSubscriptionId { get; set; }
		public bool CanRunLms { get; set; }
		public bool SponsoredByPublisher { get; set; }

		public static ContentSetData From(ContentSet set) => set == null ? null : new ContentSetData
		{
			ContentSetId = set.ContentSetId,
			PublisherDepartmentId = set.DepartmentId,
			Kind = ((ContentSetKinds)set.Kind).ToString(),
			Name = set.Name,
			Description = set.Description,
			LockedDeferDays = set.LockedDeferDays,
			IsArchived = set.IsArchived
		};

		public static ContentSetData From(ContentSetDetail detail)
		{
			var data = From(detail?.Set);
			if (data == null)
				return null;
			data.Items = (detail.Items ?? new List<ContentSetItemView>()).Select(ContentSetItemData.From).Where(i => i != null).ToList();
			data.Subscribers = (detail.Subscribers ?? new List<ContentSetSubscriberView>()).Where(s => s?.Subscription != null).Select(s => new ContentSetSubscriberData
			{
				ContentSetSubscriptionId = s.Subscription.ContentSetSubscriptionId,
				DepartmentId = s.Subscription.DepartmentId,
				DepartmentName = s.DepartmentName,
				Status = ((ContentSetSubscriptionStatuses)s.Subscription.Status).ToString(),
				SubscribedOn = s.Subscription.SubscribedOn,
				Sponsored = s.Sponsored
			}).ToList();
			data.OpenProposals = detail.OpenProposals;
			return data;
		}

		public static ContentSetData From(AvailableContentSet available)
		{
			var data = From(available?.Set);
			if (data == null)
				return null;
			data.PublisherName = available.PublisherName;
			data.Items = (available.Items ?? new List<ContentSetItemView>()).Select(ContentSetItemData.From).Where(i => i != null).ToList();
			data.ContentSetSubscriptionId = available.Subscription?.ContentSetSubscriptionId ?? 0;
			data.CanRunLms = available.CanRunLms;
			data.SponsoredByPublisher = available.SponsoredByPublisher;
			return data;
		}
	}

	public class ContentSetSubscriberData
	{
		public int ContentSetSubscriptionId { get; set; }
		public int DepartmentId { get; set; }
		public string DepartmentName { get; set; }
		public string Status { get; set; }
		public DateTime SubscribedOn { get; set; }
		public bool Sponsored { get; set; }
	}

	public class ContentSetsResult : StandardApiResponseV4Base
	{
		public List<ContentSetData> Data { get; set; } = new List<ContentSetData>();
	}

	public class ContentSetResultData : StandardApiResponseV4Base
	{
		public ContentSetData Data { get; set; }
	}

	/// <summary>One of the department's subscriptions, with its managed copies.</summary>
	public class ContentSetSubscriptionData
	{
		public int ContentSetSubscriptionId { get; set; }
		public int ContentSetId { get; set; }
		public string SetName { get; set; }
		public string PublisherName { get; set; }
		/// <summary>Active, Suspended or Ended.</summary>
		public string Status { get; set; }
		public bool AutoAccept { get; set; }
		public int OpenUpdates { get; set; }
		public bool LinkOk { get; set; }
		public bool SponsoredByPublisher { get; set; }
		public List<ContentSetCopyData> Copies { get; set; } = new List<ContentSetCopyData>();
	}

	public class ContentSetCopyData
	{
		public int ContentSetCopyId { get; set; }
		public string ItemKind { get; set; }
		/// <summary>The department's own protocol, course, training or path the copy is.</summary>
		public int LocalItemId { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		public bool HasLocalChanges { get; set; }
	}

	public class ContentSetSubscriptionsResult : StandardApiResponseV4Base
	{
		public List<ContentSetSubscriptionData> Data { get; set; } = new List<ContentSetSubscriptionData>();
	}

	public class ContentSetProposalData
	{
		public int ContentSetProposalId { get; set; }
		public int ContentSetId { get; set; }
		public string SetName { get; set; }
		public string ItemTitle { get; set; }
		public string SubscriberName { get; set; }
		public string AuthorName { get; set; }
		/// <summary>The block (or section) the proposal is about, when it names one.</summary>
		public string Anchor { get; set; }
		public string Body { get; set; }
		/// <summary>Open, Accepted or Declined.</summary>
		public string Status { get; set; }
		public string ResponseBody { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? RespondedOn { get; set; }

		public static ContentSetProposalData From(ContentSetProposalView view) => view?.Proposal == null ? null : new ContentSetProposalData
		{
			ContentSetProposalId = view.Proposal.ContentSetProposalId,
			ContentSetId = view.Proposal.ContentSetId,
			SetName = view.SetName,
			ItemTitle = view.ItemTitle,
			SubscriberName = view.Proposal.SubscriberName,
			AuthorName = view.Proposal.AuthorName,
			Anchor = view.Proposal.Anchor,
			Body = view.Proposal.Body,
			Status = ((ContentSetProposalStatuses)view.Proposal.Status).ToString(),
			ResponseBody = view.Proposal.ResponseBody,
			CreatedOn = view.Proposal.CreatedOn,
			RespondedOn = view.Proposal.RespondedOn
		};
	}

	public class ContentSetProposalsData
	{
		/// <summary>Proposals other departments sent to this department's sets (managers only).</summary>
		public List<ContentSetProposalData> Received { get; set; } = new List<ContentSetProposalData>();
		/// <summary>Proposals this department sent upstream.</summary>
		public List<ContentSetProposalData> Sent { get; set; } = new List<ContentSetProposalData>();
	}

	public class ContentSetProposalsResult : StandardApiResponseV4Base
	{
		public ContentSetProposalsData Data { get; set; } = new ContentSetProposalsData();
	}

	public class ContentSetProposeInput
	{
		/// <summary>The department's managed copy the proposal is about.</summary>
		public int ContentSetCopyId { get; set; }
		public string Anchor { get; set; }
		public string Body { get; set; }
	}

	public class ContentSetRespondInput
	{
		public int ContentSetProposalId { get; set; }
		public bool Accept { get; set; }
		public string Response { get; set; }
	}
}
