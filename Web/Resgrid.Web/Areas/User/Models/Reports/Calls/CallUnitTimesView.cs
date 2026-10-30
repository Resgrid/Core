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
		public bool AnyHistory { get; set; }
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
		public string UnitType { get; set; }
		public string Group { get; set; }
		public CallUnitTimesRow Times { get; set; }

		/// <summary>The unit's status history on the call, in Times.Timeline order, with each status as the department named it.</summary>
		public List<CallUnitTimesStatus> History { get; set; } = new List<CallUnitTimesStatus>();
	}

	public class CallUnitTimesStatus
	{
		public CallUnitTimelineEntry Entry { get; set; }

		/// <summary>The status as the department named it.</summary>
		public string Text { get; set; }

		/// <summary>Hex colours, or null for a status with none.</summary>
		public string Color { get; set; }
		public string TextColor { get; set; }

		/// <summary>What the status means (its base type), for comparing across status sets; empty when it has none.</summary>
		public string Meaning { get; set; }

		/// <summary>How it is tied to the call, as the report's Linked By column shows it.</summary>
		public CallUnitTimesStatusSources Source { get; set; }
	}

	public enum CallUnitTimesStatusSources
	{
		Explicit = 0,
		AutoLinked = 1,
		Inferred = 2,

		/// <summary>Not on the call's record: set after the unit left it (<see cref="StatusDestinationSources.FollowOn"/>).</summary>
		FollowOn = 3
	}
}
