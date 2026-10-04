namespace Resgrid.Web.Areas.User.Models.Calls
{
	/// <summary>Where a location history widget (Views/Shared/_CallLocationHistory) loads from and when.</summary>
	public class CallLocationHistoryPanel
	{
		public string Id { get; set; } = "locationHistory";
		/// <summary>The JSON endpoint returning a <see cref="CallLocationHistoryJson"/>.</summary>
		public string Url { get; set; }
		public string Header { get; set; }
		public string Help { get; set; }
		/// <summary>Load as soon as the page is ready (a section always on screen).</summary>
		public bool AutoLoad { get; set; }
		/// <summary>CSS selector of the tab link that loads it on first click (a section inside a tab).</summary>
		public string TriggerSelector { get; set; }
	}
}
