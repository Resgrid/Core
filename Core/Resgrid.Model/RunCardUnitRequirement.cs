using Newtonsoft.Json;
using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// "This alarm level needs N units of this type" (e.g. 2x Engine).
	/// </summary>
	[Table("RunCardUnitRequirements")]
	[ProtoContract]
	public class RunCardUnitRequirement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int RunCardUnitRequirementId { get; set; }

		[Required]
		[ForeignKey("AlarmLevel"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int RunCardAlarmLevelId { get; set; }

		public virtual RunCardAlarmLevel AlarmLevel { get; set; }

		[Required]
		[ProtoMember(3)]
		public int UnitTypeId { get; set; }

		[Required]
		[ProtoMember(4)]
		public int RequiredCount { get; set; }

		[ProtoMember(5)]
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
