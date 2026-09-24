namespace Resgrid.Model
{
	public enum ProcessLogTypes
	{
		ShiftNotifier = 1,
		TrainingNotifier = 2,

		/// <summary>A shift day's start reminder; the log id is the ShiftDayId (ShiftNotifier logs were keyed on the ShiftId).</summary>
		ShiftDayNotifier = 3
	}
}