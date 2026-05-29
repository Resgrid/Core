using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Maps the "Modern" <see cref="PushSoundTypes"/> values to the bundled audio
	/// file stem that the mobile apps ship (iOS uses "{stem}.caf", Android / Novu
	/// use "{stem}.wav"). The push payload carries the sound type as its integer
	/// string in the notification "type" field; providers resolve it to a filename.
	///
	/// Legacy sound types (0-6) keep their historical, per-provider filenames and
	/// are resolved inline in each provider. The Modern set (7+) is centralized
	/// here so all push paths stay in sync.
	/// </summary>
	public static class PushSoundFile
	{
		private static readonly Dictionary<string, string> ModernStems = new Dictionary<string, string>
		{
			{ ((int)PushSoundTypes.ModernCallEmergency).ToString(),     "moderncallemergency" },
			{ ((int)PushSoundTypes.ModernCallHigh).ToString(),          "moderncallhigh" },
			{ ((int)PushSoundTypes.ModernCallMedium).ToString(),        "moderncallmedium" },
			{ ((int)PushSoundTypes.ModernCallLow).ToString(),           "moderncalllow" },
			{ ((int)PushSoundTypes.ModernNotification).ToString(),      "modernnotification" },
			{ ((int)PushSoundTypes.ModernMessage).ToString(),           "modernmessage" },
			{ ((int)PushSoundTypes.ModernChat).ToString(),              "modernchat" },
			{ ((int)PushSoundTypes.ModernCallUpdated).ToString(),       "moderncallupdated" },
			{ ((int)PushSoundTypes.ModernCallClosed).ToString(),        "moderncallclosed" },
			{ ((int)PushSoundTypes.ModernTroubleAlert).ToString(),      "moderntroublealert" },
			{ ((int)PushSoundTypes.ModernPersonnelStatus).ToString(),   "modernpersonnelstatus" },
			{ ((int)PushSoundTypes.ModernUnitStatus).ToString(),        "modernunitstatus" },
			{ ((int)PushSoundTypes.ModernStaffing).ToString(),          "modernstaffing" },
			{ ((int)PushSoundTypes.ModernShift).ToString(),             "modernshift" },
			{ ((int)PushSoundTypes.ModernTraining).ToString(),          "moderntraining" },
			{ ((int)PushSoundTypes.ModernCalendar).ToString(),          "moderncalendar" },
			{ ((int)PushSoundTypes.ModernAvailabilityAlert).ToString(), "modernavailabilityalert" },
			{ ((int)PushSoundTypes.ModernWeatherAlert).ToString(),      "modernweatheralert" },
			{ ((int)PushSoundTypes.ModernUnitNotice).ToString(),        "modernunitnotice" },
			{ ((int)PushSoundTypes.ModernResourceOrder).ToString(),     "modernresourceorder" },
		};

		/// <summary>
		/// Returns the bundled file stem (no extension) for a Modern push sound
		/// type, or null when <paramref name="type"/> is not part of the Modern set
		/// (e.g. legacy types or custom department tones like "c5").
		/// </summary>
		public static string GetModernStem(string type)
		{
			if (string.IsNullOrWhiteSpace(type))
				return null;

			return ModernStems.TryGetValue(type, out var stem) ? stem : null;
		}
	}
}
