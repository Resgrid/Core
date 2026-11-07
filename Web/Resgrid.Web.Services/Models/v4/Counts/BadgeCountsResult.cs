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
		/// <summary>The count's key, as asked for (e.g. calls, messages, chats, weather)</summary>
		public string Key { get; set; }

		/// <summary>The number to show on the badge</summary>
		public int Count { get; set; }

		/// <summary>What the count is made of, when the parts mean something on their own (e.g. weather: extreme, severe); null otherwise</summary>
		public Dictionary<string, int> Breakdown { get; set; }
	}
}
