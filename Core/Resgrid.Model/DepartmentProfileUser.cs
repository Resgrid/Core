using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentProfileUsers")]
	public class DepartmentProfileUser : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public string DepartmentProfileUserId { get; set; }

		[Required]
		public string Identity { get; set; }

		[Required]
		public string Name { get; set; }

		public string Email { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentProfileUserId; }
			set { DepartmentProfileUserId = value.ToString(); }
		}
	}
}