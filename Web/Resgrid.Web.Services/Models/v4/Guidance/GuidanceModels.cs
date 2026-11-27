using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Guidance
{
	// A3 in-product guidance (protocols-training-knowledge-plan §15) for Omni: the member's role tour and the release's "What's new" lesson
	// (both remembered per member), and the free keyword search over the Resgrid Academy and the product reference. Texts are in the
	// member's language. All times are UTC.

	/// <summary>One task of a guided tour.</summary>
	public class GuidanceTourTaskData
	{
		/// <summary>The task's key (send it back to mark it done).</summary>
		public string Key { get; set; }

		public string Title { get; set; }

		public string Hint { get; set; }

		/// <summary>The web page the task opens (relative); null when there is none.</summary>
		public string PageUrl { get; set; }

		/// <summary>The Academy lesson that teaches it (absolute); null when the Academy is switched off.</summary>
		public string LessonUrl { get; set; }

		public bool Done { get; set; }
	}

	/// <summary>A guided tour with the member's progress.</summary>
	public class GuidanceTourData
	{
		/// <summary>administrator, dispatcher, training-officer or member.</summary>
		public string Key { get; set; }

		public string Title { get; set; }

		public string Intro { get; set; }

		/// <summary>The tour for the member's role.</summary>
		public bool IsMine { get; set; }

		public bool Dismissed { get; set; }

		public bool Completed { get; set; }

		public List<GuidanceTourTaskData> Tasks { get; set; } = new List<GuidanceTourTaskData>();
	}

	/// <summary>One item of a release's "What's new" lesson.</summary>
	public class GuidanceWhatsNewItemData
	{
		public string Key { get; set; }

		public string Title { get; set; }

		public string Body { get; set; }

		public string PageUrl { get; set; }

		public string LessonUrl { get; set; }
	}

	/// <summary>A release's "What's new" lesson.</summary>
	public class GuidanceWhatsNewData
	{
		/// <summary>The release ("2026.12"); dismiss with the key "whatsnew:" + Release.</summary>
		public string Release { get; set; }

		public DateTime PublishedOn { get; set; }

		public string Title { get; set; }

		public string Summary { get; set; }

		public double Minutes { get; set; }

		public bool Dismissed { get; set; }

		public List<GuidanceWhatsNewItemData> Items { get; set; } = new List<GuidanceWhatsNewItemData>();
	}

	/// <summary>The home card: the member's role tour while open, and the newest unread release lesson.</summary>
	public class GuidanceDashboardResult : StandardApiResponseV4Base
	{
		public GuidanceDashboardData Data { get; set; } = new GuidanceDashboardData();
	}

	/// <summary>The home card's content.</summary>
	public class GuidanceDashboardData
	{
		public GuidanceTourData Tour { get; set; }

		public GuidanceWhatsNewData WhatsNew { get; set; }
	}

	/// <summary>Every tour with the member's progress.</summary>
	public class GuidanceToursResult : StandardApiResponseV4Base
	{
		public List<GuidanceTourData> Data { get; set; } = new List<GuidanceTourData>();
	}

	/// <summary>One release lesson.</summary>
	public class GuidanceWhatsNewResult : StandardApiResponseV4Base
	{
		public GuidanceWhatsNewData Data { get; set; }
	}

	/// <summary>Marks a tour task done or not done.</summary>
	public class GuidanceTourTaskInput
	{
		public string Tour { get; set; }

		public string Task { get; set; }

		public bool Done { get; set; }
	}

	/// <summary>Closes a tour ("tour:member") or a release lesson ("whatsnew:2026.12") for the member.</summary>
	public class GuidanceDismissInput
	{
		public string Key { get; set; }
	}

	/// <summary>Whether a guidance change was saved.</summary>
	public class GuidanceSavedResult : StandardApiResponseV4Base
	{
		public bool Data { get; set; }
	}

	/// <summary>One search hit.</summary>
	public class GuidanceSearchHitData
	{
		/// <summary>Lesson, Feature or Reference.</summary>
		public string Kind { get; set; }

		public string Id { get; set; }

		public string Title { get; set; }

		public string Excerpt { get; set; }

		/// <summary>The Academy lesson, or the public documentation page.</summary>
		public string Url { get; set; }

		/// <summary>The web page (relative), when there is one.</summary>
		public string PageUrl { get; set; }

		public double Minutes { get; set; }
	}

	/// <summary>Keyword search results (free, no AI).</summary>
	public class GuidanceSearchResult : StandardApiResponseV4Base
	{
		public GuidanceSearchData Data { get; set; } = new GuidanceSearchData();
	}

	/// <summary>Lessons first, then features and reference articles.</summary>
	public class GuidanceSearchData
	{
		public string Query { get; set; }

		public List<GuidanceSearchHitData> Lessons { get; set; } = new List<GuidanceSearchHitData>();

		public List<GuidanceSearchHitData> Docs { get; set; } = new List<GuidanceSearchHitData>();
	}
}
