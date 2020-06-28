using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("DepartmentGroupMembers")]
	public class DepartmentGroupMember : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentGroupMemberId { get; set; }

		[Required]
		[ForeignKey("DepartmentGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentGroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup DepartmentGroup { get; set; }

		public int DepartmentId { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		public bool? IsAdmin { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentGroupMemberId; }
			set { DepartmentGroupMemberId = (int)value; }
		}
	}

	public class DepartmentGroupMember_Mapping : EntityTypeConfiguration<DepartmentGroupMember>
	{
		public DepartmentGroupMember_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}
