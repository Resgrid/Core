using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	//[Table("AspNetUserRoles")]
	//public class ApplicationUserRole : IdentityUserRole, IEntity
	//{
	//	[NotMapped]
	//	public object Id
	//	{
	//		get { return base.UserId; }
	//		set { base.UserId = (string)value; }
	//	}
	//}

	public class ApplicationUserRole_Mapping : EntityTypeConfiguration<IdentityUserRole>
	{
		public ApplicationUserRole_Mapping()
		{
			ToTable("AspNetUserRoles");

			this.HasKey(t => t.Id);
			this.Property(t => t.Id).HasDatabaseGeneratedOption(DatabaseGeneratedOption.Identity);
			this.Property(t => t.UserId).IsRequired();
			this.Property(t => t.RoleId).IsRequired();
		}
	}
}