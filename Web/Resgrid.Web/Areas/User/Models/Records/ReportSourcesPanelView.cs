namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>The report editors' "Call data" panel (Views/Shared/_ReportSourcesPanel).</summary>
	public class ReportSourcesPanelView
	{
		/// <summary>The call, or null on a run report that has not picked one yet.</summary>
		public int? CallId { get; set; }

		/// <summary>The report being written; left out of the run report list.</summary>
		public string RecordId { get; set; }

		/// <summary>The form the panel fills.</summary>
		public string FormId { get; set; }

		/// <summary>"incident" or "run" (run reports follow their call picker and can fill blanks from the call).</summary>
		public string Mode { get; set; } = "incident";

		/// <summary>CSS selector of the narrative textarea run report narratives are appended to; null hides that action.</summary>
		public string NarrativeTarget { get; set; }

		/// <summary>True to fill the blank fields from the call as soon as it loads (a new run report started from a call).</summary>
		public bool AutoFill { get; set; }
	}
}
