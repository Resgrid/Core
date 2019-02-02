using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("PersonnelRoleUsers")]
	public class PersonnelRoleUser : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PersonnelRoleUserId { get; set; }

		[Required]
		public int PersonnelRoleId { get; set; }

		[ForeignKey("PersonnelRoleId")]
		public virtual PersonnelRole Role { get; set; }

		public int DepartmentId { get; set; }

		[Required]
		public string UserId { get; set; }

		[ForeignKey("UserId")]
		public IdentityUser User { get; set; }

		[NotMapped]
		public object Id
		{
			get { return PersonnelRoleUserId; }
			set { PersonnelRoleUserId = (int)value; }
		}
	}

	public class PersonnelRoleUser_Mapping : EntityTypeConfiguration<PersonnelRoleUser>
	{
		public PersonnelRoleUser_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}