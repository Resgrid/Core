using System.Collections.Generic;

namespace Resgrid.Model.Events
{
	/// <summary>
	/// A run card matched a call and its recommendations were applied (auto-dispatch)
	/// or accepted from the pre-populated New Call page.
	/// </summary>
	public class RunCardActivatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public int RunCardId { get; set; }
		public string RunCardName { get; set; }
		public int AlarmLevel { get; set; }
		public int ModeUsed { get; set; }
		public bool WasAutoDispatched { get; set; }
		public List<int> UnitIds { get; set; } = new List<int>();
		public List<string> UserIds { get; set; } = new List<string>();
	}

	/// <summary>A call was escalated to its next alarm level ("Strike Next Alarm").</summary>
	public class CallAlarmEscalatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public int PreviousAlarmLevel { get; set; }
		public int NewAlarmLevel { get; set; }
		public List<int> AddedUnitIds { get; set; } = new List<int>();
		public List<string> AddedUserIds { get; set; } = new List<string>();
	}

	/// <summary>Auto-dispatch completed but one or more run card requirements could not be filled.</summary>
	public class DispatchShortfallEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public int RunCardId { get; set; }
		public int AlarmLevel { get; set; }
		public List<RequirementShortfall> Shortfalls { get; set; } = new List<RequirementShortfall>();
	}

	/// <summary>The move-up pass found a station below its minimum coverage.</summary>
	public class StationCoverageGapEvent
	{
		public int DepartmentId { get; set; }
		public int? CallId { get; set; }
		public List<MoveUpRecommendation> MoveUps { get; set; } = new List<MoveUpRecommendation>();
	}
}
