using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CommandDefinitions")]
	public class CommandDefinition : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CommandDefinitionId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int? CallTypeId { get; set; }

		public virtual CallType CallType { get; set; }

		public bool Timer { get; set; }
		
		public int TimerMinutes { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public virtual ICollection<CommandDefinitionRole> Assignments { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandDefinitionId; }
			set { CommandDefinitionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CommandDefinitions";

		[NotMapped]
		public string IdName => "CommandDefinitionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "CallType", "Assignments" };
	}
}
