using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// "This alarm level needs N people holding this personnel role" (e.g. 4x Firefighter).
	/// </summary>
	[Table("RunCardRoleRequirements")]
	public class RunCardRoleRequirement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardRoleRequirementId { get; set; }

		[Required]
		[ForeignKey("AlarmLevel"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int RunCardAlarmLevelId { get; set; }

		public virtual RunCardAlarmLevel AlarmLevel { get; set; }

		[Required]
		public int PersonnelRoleId { get; set; }

		[Required]
		public int RequiredCount { get; set; }

		public int SortOrder { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardRoleRequirementId; }
			set { RunCardRoleRequirementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardRoleRequirements";

		[NotMapped]
		public string IdName => "RunCardRoleRequirementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "AlarmLevel" };
	}
}
