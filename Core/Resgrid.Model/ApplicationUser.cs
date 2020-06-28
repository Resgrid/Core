using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	public class ApplicationUser_Mapping : EntityTypeConfiguration<IdentityUser>
	{
		public ApplicationUser_Mapping()
		{
			ToTable("AspNetUsers");
			this.HasKey(t => t.Id);
			this.Ignore(t => t.UserId);
			this.Property(t => t.Id).IsRequired();
			this.Property(t => t.AccessFailedCount).IsRequired();
			this.Property(t => t.ConcurrencyStamp).IsOptional();
			this.Property(t => t.Email).HasMaxLength(256).IsOptional();

			this.Property(t => t.EmailConfirmed).IsRequired();
			this.Property(t => t.LockoutEnabled).IsOptional();
			this.Property(t => t.LockoutEnd).IsOptional();
			this.Property(t => t.NormalizedEmail).HasMaxLength(256).IsOptional();
			this.Property(t => t.NormalizedUserName).HasMaxLength(256).IsOptional();
			this.Property(t => t.PasswordHash).IsOptional();
			this.Property(t => t.PhoneNumber).IsOptional();
			this.Property(t => t.PhoneNumberConfirmed).IsRequired();
			this.Property(t => t.SecurityStamp).IsOptional();
			this.Property(t => t.TwoFactorEnabled).IsRequired();
			this.Property(t => t.UserName).HasMaxLength(256).IsOptional();
		}
	}
}
