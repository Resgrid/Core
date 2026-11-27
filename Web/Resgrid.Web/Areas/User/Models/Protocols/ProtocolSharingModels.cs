using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>What every Sharing page knows about the reader (the section strip and the actions shown).</summary>
	public abstract class ProtocolSharingPageModel
	{
		public bool CanApprove { get; set; }
		public bool CanAuthor { get; set; }
		public bool IsManager { get; set; }
		public bool IsReviewer { get; set; }
		public bool CanSubmit { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	public sealed class ProtocolPublicLinksModel : ProtocolSharingPageModel
	{
		public List<ProtocolPublicLinkView> Links { get; set; } = new List<ProtocolPublicLinkView>();
		/// <summary>Protocols that could be made public now (published, no audience, not licensed, authored).</summary>
		public List<DispatchProtocol> Eligible { get; set; } = new List<DispatchProtocol>();
		/// <summary>Protocols that cannot, with why (shown so an approver understands the list).</summary>
		public List<KeyValuePair<DispatchProtocol, ProtocolPublicEligibility>> NotEligible { get; set; } = new List<KeyValuePair<DispatchProtocol, ProtocolPublicEligibility>>();
		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		/// <summary>The site's base address the page and embed links are built on.</summary>
		public string BaseUrl { get; set; }
	}

	public sealed class ProtocolTemplatesModel : ProtocolSharingPageModel
	{
		public IReadOnlyList<ProtocolExchangeDocument> Sets { get; set; } = new List<ProtocolExchangeDocument>();
		public ProtocolImportResult Result { get; set; }

		/// <summary>Keys of the sets the department's operating profile suggests (round 9C); they are listed first.</summary>
		public ISet<string> Suggested { get; set; } = new HashSet<string>(System.StringComparer.Ordinal);
	}

	public sealed class ExchangeBrowseModel : ProtocolSharingPageModel
	{
		public ExchangeBrowseQuery Query { get; set; } = new ExchangeBrowseQuery();
		public List<ExchangeListing> Items { get; set; } = new List<ExchangeListing>();
		public int Total { get; set; }
		public IReadOnlyList<ExchangeItemKinds> Kinds { get; set; } = new List<ExchangeItemKinds>();
		public int Page { get; set; }
		public int Pages { get; set; }
	}

	public sealed class ExchangeListingModel : ProtocolSharingPageModel
	{
		public ExchangeListingView View { get; set; }
	}

	public sealed class ExchangeSubmitModel : ProtocolSharingPageModel
	{
		public IReadOnlyList<ExchangeItemKinds> Kinds { get; set; } = new List<ExchangeItemKinds>();
		public int Kind { get; set; }
		public List<ExchangeSourceItem> Items { get; set; } = new List<ExchangeSourceItem>();
		public string SourceRef { get; set; }
		/// <summary>The stripped payload's preview and findings for the chosen item.</summary>
		public ExchangePreparedItem Prepared { get; set; }
		public string PreviewHtml { get; set; }
		public string DepartmentName { get; set; }
		public string License { get; set; } = ContentLicenses.CcBy;
		public string AttributionName { get; set; }
		public string Summary { get; set; }
	}

	public sealed class ExchangeListModel : ProtocolSharingPageModel
	{
		/// <summary>"mine" (the department's submissions) or "review" (the Resgrid review queue).</summary>
		public string Mode { get; set; }
		public List<ExchangeListing> Items { get; set; } = new List<ExchangeListing>();
	}

	public sealed class ProtocolOpenFormatModel : ProtocolSharingPageModel
	{
		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public int ReadableCount { get; set; }
	}

	public sealed class ProtocolAnalyticsModel : ProtocolSharingPageModel
	{
		public ProtocolAnalyticsReport Report { get; set; }
		/// <summary>The period as the department's local dates (yyyy-MM-dd), for the filter.</summary>
		public string From { get; set; }
		public string To { get; set; }
	}
}
