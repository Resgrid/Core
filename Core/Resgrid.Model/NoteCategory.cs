using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("NoteCategories")]
	public class NoteCategory : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public string NoteCategoryId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string Name { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedById { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return NoteCategoryId; }
			set { NoteCategoryId = (string)value; }
		}

		[NotMapped]
		public string TableName => "NoteCategories";

		[NotMapped]
		public string IdName => "NoteCategoryId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
