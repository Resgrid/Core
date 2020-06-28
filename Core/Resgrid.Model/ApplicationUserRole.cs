using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
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
