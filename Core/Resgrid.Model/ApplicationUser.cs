using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	//[Table("AspNetUsers")]
	//public class ApplicationUser : IdentityUser//, IEntity
	//{
	//	//[NotMapped]
	//	//public object Id
	//	//{
	//	//	get { return Id; }
	//	//	set { base.Id = (string)value; }
	//	//}
	//}

	public class ApplicationUser_Mapping : EntityTypeConfiguration<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser>
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