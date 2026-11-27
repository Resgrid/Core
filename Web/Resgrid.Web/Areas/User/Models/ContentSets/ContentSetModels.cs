using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.WebCore.Areas.User.Models.ContentSets
{
	/// <summary>What every set page knows (round 10 A3): the kind shown, whether the member manages sets, and the TempData alerts.</summary>
	public abstract class ContentSetPageModel
	{
		public ContentSetKinds Kind { get; set; }
		public bool CanManage { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public int OpenUpdates { get; set; }
	}

	public sealed class ContentSetIndexModel : ContentSetPageModel
	{
		public List<ContentSetDetail> Published { get; set; } = new List<ContentSetDetail>();
		public List<AvailableContentSet> Available { get; set; } = new List<AvailableContentSet>();
		public List<ContentSetSubscriptionView> Subscriptions { get; set; } = new List<ContentSetSubscriptionView>();
	}

	public sealed class ContentSetEditModel : ContentSetPageModel
	{
		public ContentSetDetail Detail { get; set; }
		public List<ContentSetCandidate> Candidates { get; set; } = new List<ContentSetCandidate>();
	}

	public sealed class ContentSetAvailableModel : ContentSetPageModel
	{
		public AvailableContentSet Set { get; set; }
	}

	public sealed class ContentSetInboxModel : ContentSetPageModel
	{
		public List<ContentSetInboxRow> Rows { get; set; } = new List<ContentSetInboxRow>();
		public bool ShowAll { get; set; }
	}

	public sealed class ContentSetUpdateModel : ContentSetPageModel
	{
		public ContentSetUpdateView View { get; set; }
	}

	public sealed class ContentSetCustomizeModel : ContentSetPageModel
	{
		public ContentSetCustomizeView View { get; set; }
	}

	public sealed class ContentSetProposeModel : ContentSetPageModel
	{
		public ContentSetCopyInfo Copy { get; set; }
		public string ItemTitle { get; set; }
	}

	public sealed class ContentSetProposalsModel : ContentSetPageModel
	{
		public List<ContentSetProposalView> Received { get; set; } = new List<ContentSetProposalView>();
		public List<ContentSetProposalView> Sent { get; set; } = new List<ContentSetProposalView>();
	}

	public sealed class ContentSetSponsorshipsModel : ContentSetPageModel
	{
		public List<LmsSponsorshipView> Sponsorships { get; set; } = new List<LmsSponsorshipView>();
		/// <summary>Linked departments the department shares Training with (it can sponsor them).</summary>
		public List<KeyValuePair<int, string>> Sponsorable { get; set; } = new List<KeyValuePair<int, string>>();
		public LmsSponsorshipGrant Grant { get; set; }
		public int DepartmentId { get; set; }
	}

	public sealed class ContentSetSettingsModel : ContentSetPageModel
	{
		public ContentSetSettings Settings { get; set; }
		/// <summary>The department's personnel roles (id, name) a notice can go to.</summary>
		public List<KeyValuePair<int, string>> Roles { get; set; } = new List<KeyValuePair<int, string>>();
	}
}
