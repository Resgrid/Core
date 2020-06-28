using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{	public class ApplicationRoleClaim_Mapping : EntityTypeConfiguration<IdentityRoleClaim>
	{
		public ApplicationRoleClaim_Mapping()
		{
			ToTable("AspNetRoleClaims");
			this.HasKey(t => t.Id);
			this.Property(t => t.Id).IsRequired();
			this.Property(t => t.ClaimType).IsOptional();
			this.Property(t => t.ClaimValue).IsOptional();
			this.Property(t => t.RoleId).IsRequired();
		}
	}
}
