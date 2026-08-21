using Newtonsoft.Json;
using ProtoBuf;
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
	[ProtoContract]
	public class RunCard : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int RunCardId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		[ProtoMember(3)]
		public string Name { get; set; }

		[MaxLength(500)]
		[ProtoMember(4)]
		public string Description { get; set; }

		[ProtoMember(5)]
		public bool IsDisabled { get; set; }

		/// <summary>
		/// Per-card override of DepartmentSettingTypes.DispatchRecommendationMode.
		/// Null = use department default; otherwise a DispatchRecommendationModes value
		/// (Off here means "manual only for this card" even when the department automates).
		/// </summary>
		[ProtoMember(6)]
		public int? DispatchModeOverride { get; set; }

		/// <summary>
		/// Per-card override of DepartmentSettingTypes.DispatchRecommendationAutoDispatch.
		/// Null = department default, 0 = pre-populate only, 1 = auto-dispatch.
		/// </summary>
		[ProtoMember(7)]
		public int? AutoDispatchOverride { get; set; }

		/// <summary>
		/// Per-card override of the department minimum UnitStaffingLevel gate.
		/// Null = department default, 0 = no gate, otherwise minimum UnitStaffingLevel value.
		/// </summary>
		[ProtoMember(8)]
		public int? MinimumStaffingLevelOverride { get; set; }

		/// <summary>
		/// Station group used to anchor the nearest-station cascade when the call has no
		/// usable location. Null means the card cannot fill without a call location.
		/// </summary>
		[ProtoMember(9)]
		public int? HomeStationGroupId { get; set; }

		[Required]
		[ProtoMember(10)]
		public DateTime AddedOn { get; set; }

		[Required]
		[ProtoMember(11)]
		public string AddedByUserId { get; set; }

		[ProtoMember(12)]
		public DateTime? UpdatedOn { get; set; }

		[ProtoMember(13)]
		public string UpdatedByUserId { get; set; }

		[ProtoMember(14)]
		public virtual ICollection<RunCardTrigger> Triggers { get; set; }

		[ProtoMember(15)]
		public virtual ICollection<RunCardAlarmLevel> AlarmLevels { get; set; }

		[ProtoMember(16)]
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
