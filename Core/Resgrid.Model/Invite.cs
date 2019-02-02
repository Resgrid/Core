using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("Invites")]
	public class Invite : IEntity
	{
		[Key]
		[Required]
		public int InviteId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public Guid Code { get; set; }

		[Required]
		public string EmailAddress { get; set; }

		[Required]
		public string SendingUserId { get; set; }

		[Required]
		public DateTime SentOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public string CompletedUserId { get; set; }


		[ForeignKey("SendingUserId")]
		public virtual IdentityUser SendingUser { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[NotMapped]
		public object Id
		{
			get { return InviteId; }
			set { InviteId = (int)value; }
		}
	}

	public class Invite_Mapping : EntityTypeConfiguration<Invite>
	{
		public Invite_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
		}
	}
}