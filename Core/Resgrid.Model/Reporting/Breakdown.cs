using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A grouped (GROUP BY) breakdown, capped to top-N + "Other". Examples keyed as
	/// "callsByType", "callsByPriority", "callsByStatus", "personnelByState", "unitsByStatus".
	/// </summary>
	[ProtoContract]
	public class Breakdown
	{
		[ProtoMember(1)]
		public string Key { get; set; }

		[ProtoMember(2)]
		public List<BreakdownItem> Items { get; set; } = new List<BreakdownItem>();
	}
}
