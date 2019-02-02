using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	//[Table("AspNetRoles")]
	//public class ApplicationRole: IdentityRole//, IEntity
	//{
	//	//public object Id
	//	//{
	//	//	get { return Id; }
	//	//	set { base.Id = (string)value; }
	//	//}
	//}

	public class ApplicationRole_Mapping : EntityTypeConfiguration<IdentityRole>
	{
		public ApplicationRole_Mapping()
		{
			ToTable("AspNetRoles");
			this.HasKey(t => t.Id);
			this.Property(t => t.Id).IsRequired();
			this.Property(t => t.ConcurrencyStamp).IsOptional();
			this.Property(t => t.Name).HasMaxLength(256).IsOptional();
			this.Property(t => t.NormalizedName).HasMaxLength(256).IsOptional();
		}
	}
}