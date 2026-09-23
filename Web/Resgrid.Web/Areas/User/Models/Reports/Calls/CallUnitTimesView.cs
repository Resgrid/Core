using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Web.Areas.User.Models.Reports.Calls
{
	public class CallUnitTimesView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }

		/// <summary>The period shown, department-local.</summary>
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		/// <summary>Set when the report covers a single call.</summary>
		public int? CallId { get; set; }

		public List<CallUnitTimesCall> Calls { get; set; } = new List<CallUnitTimesCall>();

		public bool AnyAutoLinked { get; set; }
		public bool AnyInferred { get; set; }
	}

	public class CallUnitTimesCall
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public DateTime LoggedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public List<CallUnitTimesUnit> Units { get; set; } = new List<CallUnitTimesUnit>();
	}

	public class CallUnitTimesUnit
	{
		public string UnitName { get; set; }
		public string Group { get; set; }
		public CallUnitTimesRow Times { get; set; }
	}
}
