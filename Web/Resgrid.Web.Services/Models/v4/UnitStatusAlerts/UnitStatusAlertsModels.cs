using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.UnitStatusAlerts
{
	/// <summary>
	/// The acknowledgements covering each unit's current status episode.
	/// </summary>
	public class GetUnitStatusAlertAcknowledgementsResult : StandardApiResponseV4Base
	{
		public List<UnitStatusAlertAcknowledgementResultData> Data { get; set; }

		public GetUnitStatusAlertAcknowledgementsResult()
		{
			Data = new List<UnitStatusAlertAcknowledgementResultData>();
		}
	}

	/// <summary>
	/// The outcome of acknowledging or clearing a unit status timer alert.
	/// </summary>
	public class SaveUnitStatusAlertAcknowledgementResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Null on success. Otherwise one of: unit_alert_not_found, unit_alert_status_changed (the unit moved to a
		/// new status since the board loaded), unit_alert_not_overdue, unit_alert_conflict (another dispatcher
		/// acknowledged it first; their acknowledgement is in Data), unit_alert_invalid_mode,
		/// unit_alert_invalid_level, unit_alert_note_too_long.
		/// </summary>
		public string Error { get; set; }

		public UnitStatusAlertAcknowledgementResultData Data { get; set; }
	}

	public class UnitStatusAlertAcknowledgementResultData
	{
		public string UnitStatusAlertAcknowledgementId { get; set; }

		public string UnitId { get; set; }

		/// <summary>The status record this acknowledgement covers. Compare it with the unit's CurrentUnitStateId.</summary>
		public int UnitStateId { get; set; }

		/// <summary>1 = warning, 2 = alert. If the unit goes past this level, the acknowledgement no longer covers it.</summary>
		public int Level { get; set; }

		/// <summary>0 = acknowledged (still highlighted, marked as seen), 1 = muted.</summary>
		public int Mode { get; set; }

		/// <summary>When a mute runs out. Null on a mute means it lasts until the unit changes status.</summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? MutedUntilUtc { get; set; }

		public string Note { get; set; }

		public string AcknowledgedByUserId { get; set; }

		public string AcknowledgedByName { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime AcknowledgedOnUtc { get; set; }

		/// <summary>Set when the acknowledgement has been withdrawn (only returned by Clear).</summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? ClearedOnUtc { get; set; }
	}

	public class AcknowledgeUnitStatusAlertInput
	{
		[Required]
		[Range(1, int.MaxValue)]
		public int UnitId { get; set; }

		/// <summary>The unit's CurrentUnitStateId as the board saw it.</summary>
		[Required]
		[Range(1, int.MaxValue)]
		public int UnitStateId { get; set; }

		/// <summary>The level the board is showing: 1 = warning, 2 = alert.</summary>
		[Range(1, 2)]
		public int Level { get; set; }

		/// <summary>0 = acknowledge, 1 = mute.</summary>
		[Range(0, 1)]
		public int Mode { get; set; }

		/// <summary>For a mute, the minutes to mute for. 0 mutes until the unit changes status.</summary>
		[Range(0, 1440)]
		public int MuteMinutes { get; set; }

		[MaxLength(500)]
		public string Note { get; set; }
	}
}
