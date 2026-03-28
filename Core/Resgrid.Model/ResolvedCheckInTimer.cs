namespace Resgrid.Model
{
	public class ResolvedCheckInTimer
	{
		public int TargetType { get; set; }

		public int? UnitTypeId { get; set; }

		public string TargetEntityId { get; set; }

		public string TargetName { get; set; }

		public int DurationMinutes { get; set; }

		public int WarningThresholdMinutes { get; set; }

		public bool IsFromOverride { get; set; }

		public string ActiveForStates { get; set; }
	}
}
