using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
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
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentProfileArticleId; }
			set { DepartmentProfileArticleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentProfileArticles";

		[NotMapped]
		public string IdName => "DepartmentProfileArticleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Profile", "CreatedByUser" };
	}
}
