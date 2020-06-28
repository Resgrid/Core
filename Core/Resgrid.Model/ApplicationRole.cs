using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	public class ApplicationRole_Mapping : EntityTypeConfiguration<IdentityRole>
	{
		public ApplicationRole_Mapping()
		{
			ToTable("AspNetRoles");
			this.HasKey(t => t.Id);
			this.Property(t => t.Id).IsRequired();
			this.Property(t => t.ConcurrencyStamp).IsOptional();
			this.Property(t => t.Name).HasMaxLength(256).IsOptional();
			this.Property(t => t.NormalizedName).HasMaxLength(256).IsOptional();
		}
	}
}
