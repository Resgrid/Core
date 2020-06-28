using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	public class ApplicationUserClaim_Mapping : EntityTypeConfiguration<IdentityUserClaim>
	{
		public ApplicationUserClaim_Mapping()
		{
			ToTable("AspNetUserClaims");
			this.HasKey(t => t.Id);
			this.Property(t => t.Id).IsRequired();
			this.Property(t => t.ClaimType).IsOptional();
			this.Property(t => t.ClaimValue).IsOptional();
			this.Property(t => t.UserId).IsRequired();
		}
	}
}
