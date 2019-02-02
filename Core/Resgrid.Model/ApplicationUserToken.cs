using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	//[Table("AspNetUserTokens")]
	//public class ApplicationUserToken : IdentityUserToken, IEntity
	//{
	//	[NotMapped]
	//	public object Id
	//	{
	//		get { return Id; }
	//		set { base.UserId = (string)value; }
	//	}
	//}

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