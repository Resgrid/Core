using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentProfileUserFollows")]
	public class DepartmentProfileUserFollow : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentProfileUserFollowId { get; set; }

		[Required]
		public string DepartmentProfileUserId { get; set; }

		[ForeignKey("DepartmentProfileUserId")]
		public virtual DepartmentProfileUser DepartmentProfileUser { get; set; }

		[Required]
		public int DepartmentProfileId { get; set; }

		[ForeignKey("DepartmentProfileId")]
		public virtual DepartmentProfile DepartmentProfile { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentProfileUserFollowId; }
			set { DepartmentProfileUserFollowId = (int)value; }
		}
	}
}