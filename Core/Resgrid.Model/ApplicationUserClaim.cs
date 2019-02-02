using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	//[Table("AspNetUserClaims")]
	//public class ApplicationUserClaim: IdentityUserClaim<string>//, IEntity
	//{
	//	//[NotMapped]
	//	//public object Id
	//	//{
	//	//	get { return Id; }
	//	//	set { base.Id = (int)value; }
	//	//}
	//}

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