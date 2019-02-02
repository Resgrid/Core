using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;
namespace Resgrid.Model
{
	//[Table("AspNetUserLogins")]
	//public class ApplicationUserLogin : IdentityUserLogin, IEntity
	//{
	//	[NotMapped]
	//	public object Id
	//	{
	//		get { return Id; }
	//		set { base.UserId = (string)value; }
	//	}
	//}

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