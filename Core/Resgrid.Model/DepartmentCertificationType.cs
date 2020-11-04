using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentCertificationTypes")]
	public class DepartmentCertificationType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentCertificationTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Type { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return DepartmentCertificationTypeId; }
			set { DepartmentCertificationTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentCertificationTypes";

		[NotMapped]
		public string IdName => "DepartmentCertificationTypeId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Department" };
	}
}
