using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A grouped (GROUP BY) breakdown, capped to top-N + "Other". Examples keyed as
	/// "callsByType", "callsByPriority", "callsByStatus", "personnelByState", "unitsByStatus".
	/// </summary>
	public class Breakdown
	{
		public string Key { get; set; }

		public List<BreakdownItem> Items { get; set; } = new List<BreakdownItem>();
	}
}
