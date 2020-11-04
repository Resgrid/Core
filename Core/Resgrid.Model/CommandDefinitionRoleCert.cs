using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CommandDefinitionRoleCerts")]
	public class CommandDefinitionRoleCert : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CommandDefinitionRoleCertId { get; set; }

		[Required]
		[ForeignKey("CommandRole"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int CommandDefinitionRoleId { get; set; }

		public virtual CommandDefinitionRole CommandRole { get; set; }

		[Required]
		[ForeignKey("Certification"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentCertificationTypeId { get; set; }

		public virtual DepartmentCertificationType Certification { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return CommandDefinitionRoleCertId; }
			set { CommandDefinitionRoleCertId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CommandDefinitionRoleCerts";

		[NotMapped]
		public string IdName => "CommandDefinitionRoleCertId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "CommandRole", "Certification" };
	}
}
