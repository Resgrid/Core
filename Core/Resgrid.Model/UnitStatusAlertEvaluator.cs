using System;

namespace Resgrid.Model
{
	/// <summary>
	/// The rules for whether a unit is past its status-time threshold, and whether an acknowledgement still
	/// covers it. The Big Board runs the same rules in <c>src/lib/unit-status-thresholds.ts</c>. If you change
	/// them here, change them there too, or the server and the board will disagree about what is overdue.
	/// </summary>
	public static class UnitStatusAlertEvaluator
	{
		/// <summary>
		/// The level a unit is at. <see cref="UnitStatusAlertLevels.None"/> when the department has no threshold
		/// for the status's base type, or the unit is still within it.
		/// </summary>
		public static UnitStatusAlertLevels Evaluate(UnitStatusThresholds thresholds, int? baseType, DateTime statusTimestampUtc, DateTime nowUtc)
		{
			if (thresholds == null || thresholds.IsEmpty || !baseType.HasValue)
				return UnitStatusAlertLevels.None;

			var threshold = thresholds.Find(baseType.Value);

			if (threshold == null)
				return UnitStatusAlertLevels.None;

			var elapsed = (nowUtc - statusTimestampUtc).TotalSeconds;

			if (elapsed < 0)
				return UnitStatusAlertLevels.None;

			if (threshold.AlertSeconds > 0 && elapsed >= threshold.AlertSeconds)
				return UnitStatusAlertLevels.Alert;

			if (threshold.WarnSeconds > 0 && elapsed >= threshold.WarnSeconds)
				return UnitStatusAlertLevels.Warn;

			return UnitStatusAlertLevels.None;
		}

		/// <summary>
		/// What an acknowledgement means right now. Returns null when it no longer covers the unit, so the alert
		/// shows as unacknowledged. That happens when the unit has moved to a new status (a different
		/// <paramref name="currentUnitStateId"/>), or when it has escalated past the level that was acknowledged.
		/// A mute that has run out falls back to acknowledged: someone did see it, and the row returning to the
		/// main list is the reminder.
		/// </summary>
		public static UnitStatusAlertAcknowledgementModes? Resolve(UnitStatusAlertAcknowledgement acknowledgement, int currentUnitStateId,
			UnitStatusAlertLevels currentLevel, DateTime nowUtc)
		{
			if (acknowledgement == null || acknowledgement.ClearedOn.HasValue)
				return null;

			if (currentUnitStateId <= 0 || acknowledgement.UnitStateId != currentUnitStateId)
				return null;

			if ((int)currentLevel > acknowledgement.Level)
				return null;

			if (acknowledgement.Mode == (int)UnitStatusAlertAcknowledgementModes.Muted &&
				(!acknowledgement.MutedUntil.HasValue || acknowledgement.MutedUntil.Value > nowUtc))
				return UnitStatusAlertAcknowledgementModes.Muted;

			return UnitStatusAlertAcknowledgementModes.Acknowledged;
		}
	}
}
