using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("DepartmentProfileArticles")]
	public class DepartmentProfileArticle : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentProfileArticleId { get; set; }

		[Required]
		public int DepartmentProfileId { get; set; }

		[ForeignKey("DepartmentProfileId")]
		public virtual DepartmentProfile Profile { get; set; }

		[Required]
		public string Title { get; set; }

		[Required]
		public string Body { get; set; }

		public byte[] SmallImage { get; set; }

		public byte[] LargeImage { get; set; }

		public string Keywords { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public virtual IdentityUser CreatedByUser { get; set; }

		public DateTime StartOn { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public bool Deleted { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentProfileArticleId; }
			set { DepartmentProfileArticleId = (int)value; }
		}
	}

	public class DepartmentProfileArticle_Mapping : EntityTypeConfiguration<DepartmentProfileArticle>
	{
		public DepartmentProfileArticle_Mapping()
		{
			this.HasRequired(t => t.CreatedByUser).WithMany().HasForeignKey(t => t.CreatedByUserId).WillCascadeOnDelete(false);
		}
	}
}
