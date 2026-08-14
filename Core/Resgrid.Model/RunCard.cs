using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// A CAD-style run card: a pre-planned response package matched to calls by
	/// priority/type triggers, defining required unit types and personnel roles per
	/// alarm level, plus which statuses/staffing levels count as dispatchable.
	/// </summary>
	[Table("RunCards")]
	public class RunCard : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		[MaxLength(500)]
		public string Description { get; set; }

		public bool IsDisabled { get; set; }

		/// <summary>
		/// Per-card override of DepartmentSettingTypes.DispatchRecommendationMode.
		/// Null = use department default; otherwise a DispatchRecommendationModes value
		/// (Off here means "manual only for this card" even when the department automates).
		/// </summary>
		public int? DispatchModeOverride { get; set; }

		/// <summary>
		/// Per-card override of DepartmentSettingTypes.DispatchRecommendationAutoDispatch.
		/// Null = department default, 0 = pre-populate only, 1 = auto-dispatch.
		/// </summary>
		public int? AutoDispatchOverride { get; set; }

		/// <summary>
		/// Per-card override of the department minimum UnitStaffingLevel gate.
		/// Null = department default, 0 = no gate, otherwise minimum UnitStaffingLevel value.
		/// </summary>
		public int? MinimumStaffingLevelOverride { get; set; }

		/// <summary>
		/// Station group used to anchor the nearest-station cascade when the call has no
		/// usable location. Null means the card cannot fill without a call location.
		/// </summary>
		public int? HomeStationGroupId { get; set; }

		[Required]
		public DateTime AddedOn { get; set; }

		[Required]
		public string AddedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public virtual ICollection<RunCardTrigger> Triggers { get; set; }

		public virtual ICollection<RunCardAlarmLevel> AlarmLevels { get; set; }

		public virtual ICollection<RunCardAvailabilitySelection> AvailabilitySelections { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardId; }
			set { RunCardId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCards";

		[NotMapped]
		public string IdName => "RunCardId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Triggers", "AlarmLevels", "AvailabilitySelections" };
	}
}
