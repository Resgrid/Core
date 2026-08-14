using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// One alarm level (1st alarm, 2nd alarm, ...) of a run card. Requirements attached
	/// to a level are ADDITIVE on top of the levels below it; escalating a call to level
	/// N dispatches only level N's requirements.
	/// </summary>
	[Table("RunCardAlarmLevels")]
	public class RunCardAlarmLevel : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardAlarmLevelId { get; set; }

		[Required]
		[ForeignKey("RunCard"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int RunCardId { get; set; }

		public virtual RunCard RunCard { get; set; }

		/// <summary>1-based alarm level number.</summary>
		[Required]
		public int AlarmLevel { get; set; }

		/// <summary>Optional display name, e.g. "Working Fire".</summary>
		[MaxLength(100)]
		public string Name { get; set; }

		public virtual ICollection<RunCardUnitRequirement> UnitRequirements { get; set; }

		public virtual ICollection<RunCardRoleRequirement> RoleRequirements { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardAlarmLevelId; }
			set { RunCardAlarmLevelId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardAlarmLevels";

		[NotMapped]
		public string IdName => "RunCardAlarmLevelId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "RunCard", "UnitRequirements", "RoleRequirements" };
	}
}
