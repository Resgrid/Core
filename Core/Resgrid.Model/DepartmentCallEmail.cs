using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentCallEmails")]
	public class DepartmentCallEmail : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentCallEmailId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[MaxLength(500)]
		public string Hostname { get; set; }

		[Required]
		public int Port { get; set; }

		[Required]
		public bool UseSsl { get; set; }

		[MaxLength(125)]
		public string Username { get; set; }

		[MaxLength(125)]
		public string Password { get; set; }

		public int FormatType { get; set; }

		public DateTime? LastCheck { get; set; }

		public bool IsFailure { get; set; }

		[MaxLength(1000)]
		public string ErrorMessage { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentCallEmailId; }
			set { DepartmentCallEmailId = (int)value; }
		}
	}
}
