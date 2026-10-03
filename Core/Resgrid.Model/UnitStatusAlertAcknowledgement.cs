using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// How far past its <see cref="UnitStatusThresholds"/> limit a unit has gone. Ordered so a higher value is
	/// a more serious level.
	/// </summary>
	public enum UnitStatusAlertLevels
	{
		None = 0,
		Warn = 1,
		Alert = 2
	}

	/// <summary>What the dispatcher chose to do with the alert.</summary>
	public enum UnitStatusAlertAcknowledgementModes
	{
		/// <summary>Seen. The unit stays highlighted, marked with who saw it and any note.</summary>
		Acknowledged = 0,

		/// <summary>Moved out of the way until <see cref="UnitStatusAlertAcknowledgement.MutedUntil"/> (or the status changes).</summary>
		Muted = 1
	}

	/// <summary>
	/// A dispatcher's acknowledgement of a unit that has sat in a status past its threshold.
	/// </summary>
	/// <remarks>
	/// Keyed to the <see cref="UnitState"/> row that started the episode, not to the unit: when the unit
	/// reports a new status it gets a new <see cref="UnitStateId"/>, so the acknowledgement stops applying
	/// without any sweep. It also records the <see cref="Level"/> that was acknowledged, so a unit acknowledged
	/// at the warning level comes back when it crosses into the alert level. Rows are never deleted while the
	/// unit state exists; clearing or replacing one stamps <see cref="ClearedOn"/>, which leaves the history of
	/// who saw what and when.
	/// </remarks>
	public class UnitStatusAlertAcknowledgement : IEntity
	{
		/// <summary>Longest note a dispatcher may attach.</summary>
		public const int MaxNoteLength = 500;

		/// <summary>Longest mute that can be set. Anything longer is "until the status changes".</summary>
		public const int MaxMuteMinutes = 1440;

		public string UnitStatusAlertAcknowledgementId { get; set; }

		public int DepartmentId { get; set; }

		public int UnitId { get; set; }

		public int UnitStateId { get; set; }

		/// <summary>The <see cref="UnitStatusAlertLevels"/> value in force when acknowledged.</summary>
		public int Level { get; set; }

		/// <summary>The <see cref="UnitStatusAlertAcknowledgementModes"/> value.</summary>
		public int Mode { get; set; }

		/// <summary>UTC end of a mute. Null on a mute means until the status changes; ignored when acknowledged.</summary>
		public DateTime? MutedUntil { get; set; }

		public string Note { get; set; }

		public string AcknowledgedByUserId { get; set; }

		public DateTime AcknowledgedOn { get; set; }

		public string ClearedByUserId { get; set; }

		/// <summary>Set when the acknowledgement was withdrawn or replaced by a newer one for the same episode.</summary>
		public DateTime? ClearedOn { get; set; }

		[NotMapped]
		public string TableName => "UnitStatusAlertAcknowledgements";

		[NotMapped]
		public string IdName => "UnitStatusAlertAcknowledgementId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitStatusAlertAcknowledgementId; }
			set { UnitStatusAlertAcknowledgementId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
