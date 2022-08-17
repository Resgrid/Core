using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Units
{
	public class UnitStateHistoryView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Name { get; set; }

		public List<UnitStateSummary> Units { get; set; }

		public UnitStateHistoryView()
		{
			Units = new List<UnitStateSummary>();
		}
	}

	public class UnitStateSummary
	{
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalStatusChanges { get; set; }

		public List<UnitStateDetail> Details { get; set; }

		public Dictionary<string, int> GetDistinctStateCounts()
		{
			var qry = from d in Details
					  group d by d.State into grp
						  select new
						  {
							  State = grp.Key,
							  Count = grp.Select(x => x.State).Distinct().Count()
						  };

			return qry.ToDictionary(t => t.State, t => t.Count);
		}

		public UnitStateSummary()
		{
			Details = new List<UnitStateDetail>();
		}
	}

	public class UnitStateDetail
	{
		public string Timestamp { get; set; }
		public string State { get; set; }
		public string StateColor { get; set; }
		public string Note { get; set; }
	}
}
