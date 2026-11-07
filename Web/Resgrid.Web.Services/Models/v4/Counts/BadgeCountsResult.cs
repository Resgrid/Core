using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Counts
{
	/// <summary>The menu and tab badge counts for the signed-in member</summary>
	public class BadgeCountsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// One entry per asked-for count the member can see. A count that is missing (no permission, module or feature flag
		/// off, unknown key) means: show no badge for it, rather than zero.
		/// </summary>
		public List<BadgeCountResultData> Data { get; set; } = new List<BadgeCountResultData>();
	}

	/// <summary>One badge count</summary>
	public class BadgeCountResultData
	{
		/// <summary>
		/// The count's key in its canonical spelling (e.g. calls, messages, callsImOn, checkIns), whatever case it was asked
		/// for in: include=CALLS answers with calls, so match keys case-insensitively or ask with the canonical spelling
		/// </summary>
		public string Key { get; set; }

		/// <summary>The number to show on the badge</summary>
		public int Count { get; set; }

		/// <summary>What the count is made of, when the parts mean something on their own (e.g. weather: extreme, severe); null otherwise</summary>
		public Dictionary<string, int> Breakdown { get; set; }
	}
}
