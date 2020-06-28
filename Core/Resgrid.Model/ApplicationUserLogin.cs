using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	public class ApplicationUserLogin_Mapping : EntityTypeConfiguration<IdentityUserLogin>
	{
		public ApplicationUserLogin_Mapping()
		{
			ToTable("AspNetUserLogins");
			this.HasKey(t => new { t.UserId });
			this.Property(t => t.UserId).IsRequired();
			this.Property(t => t.LoginProvider).IsRequired();
			this.Property(t => t.ProviderKey).IsRequired();
			this.Property(t => t.ProviderDisplayName).IsOptional();
		}
	}
}
