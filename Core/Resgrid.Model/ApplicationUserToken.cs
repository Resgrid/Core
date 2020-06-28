using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	public class ApplicationUserToken_Mapping : EntityTypeConfiguration<IdentityUserToken>
	{
		public ApplicationUserToken_Mapping()
		{
			ToTable("AspNetUserTokens");
			this.HasKey(t => new { t.UserId });
			this.Property(t => t.UserId).IsRequired();
			this.Property(t => t.LoginProvider).IsRequired();
			this.Property(t => t.Name).IsRequired();
			this.Property(t => t.Value).IsOptional();
		}
	}
}
