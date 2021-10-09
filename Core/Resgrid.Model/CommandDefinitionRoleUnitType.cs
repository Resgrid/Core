using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CommandDefinitionRoleUnitTypes")]
	public class CommandDefinitionRoleUnitType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CommandDefinitionRoleUnitTypeId { get; set; }

		[Required]
		[ForeignKey("CommandRole"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int CommandDefinitionRoleId { get; set; }

		public virtual CommandDefinitionRole CommandRole { get; set; }

		[Required]
		[ForeignKey("UnitType"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int UnitTypeId { get; set; }

		public virtual UnitType UnitType { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandDefinitionRoleUnitTypeId; }
			set { CommandDefinitionRoleUnitTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CommandDefinitionRoleUnitTypes";

		[NotMapped]
		public string IdName => "CommandDefinitionRoleUnitTypeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CommandRole", "UnitType" };
	}
}
