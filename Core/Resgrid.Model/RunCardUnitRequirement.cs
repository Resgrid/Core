using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// "This alarm level needs N units of this type" (e.g. 2x Engine).
	/// </summary>
	[Table("RunCardUnitRequirements")]
	public class RunCardUnitRequirement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardUnitRequirementId { get; set; }

		[Required]
		[ForeignKey("AlarmLevel"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int RunCardAlarmLevelId { get; set; }

		public virtual RunCardAlarmLevel AlarmLevel { get; set; }

		[Required]
		public int UnitTypeId { get; set; }

		[Required]
		public int RequiredCount { get; set; }

		public int SortOrder { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardUnitRequirementId; }
			set { RunCardUnitRequirementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardUnitRequirements";

		[NotMapped]
		public string IdName => "RunCardUnitRequirementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "AlarmLevel" };
	}
}
