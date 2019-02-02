using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Calls
{
	public class CallSummaryView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public List<Tuple<string, int>> CallTypeCount { get; set; }

		public List<Tuple<string, int>> CallCloseCount { get; set; }
		
		public int TotalCalls { get; set; }

		public List<CallSummary> CallSummaries { get; set; } 
	}

	public class CallSummary
	{
		public string Number { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public DateTime LoggedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public DateTime? FirstOnSceneTime { get; set; }
		public int UnitsCount { get; set; }
		public int PersonnelCount { get; set; }

		public string GetCallLength()
		{
			if (ClosedOn.HasValue)
				return (ClosedOn.Value - LoggedOn).ToString(@"d\d\:h\h\:m\m\:s\s", System.Globalization.CultureInfo.InvariantCulture);
			
			return "Open";
		}

		public string GetOnSceneTime()
		{
			if (FirstOnSceneTime.HasValue)
				return (FirstOnSceneTime.Value - LoggedOn).ToString(@"d\d\:h\h\:m\m\:s\s", System.Globalization.CultureInfo.InvariantCulture);

			return "N/A";
		}
	}
}