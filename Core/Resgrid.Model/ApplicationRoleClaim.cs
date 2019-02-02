using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	//[Table("AspNetRoleClaims")]
	//public class ApplicationRoleClaim: IdentityRoleClaim//, IEntity
	//{
	//	//[NotMapped]
	//	//public object Id
	//	//{
	//	//	get { return Id; }
	//	//	set { base.Id = (int)value; }
	//	//}
	//}

	public class ApplicationRoleClaim_Mapping : EntityTypeConfiguration<IdentityRoleClaim>
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