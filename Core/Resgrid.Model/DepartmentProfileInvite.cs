using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentProfileInvites")]
	public class DepartmentProfileInvite : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentProfileInviteId { get; set; }

		[Required]
		public int DepartmentProfileId { get; set; }

		[ForeignKey("DepartmentProfileId")]
		public virtual DepartmentProfile Profile { get; set; }

		public string Code { get; set; }

		public DateTime? UsedOn { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentProfileInviteId; }
			set { DepartmentProfileInviteId = (int)value; }
		}
	}
}