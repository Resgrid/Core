using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("CallQuickTemplates")]
	public class CallQuickTemplate : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CallQuickTemplateId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public bool IsDisabled { get; set; }

		[Required]
		public string Name { get; set; }

		public string CallName { get; set; }
		public string CallNature { get; set; }
		public string CallType { get; set; }
		public int CallPriority { get; set; }

		[ForeignKey("CreatedByUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string CreatedByUserId { get; set; }

		public virtual IdentityUser CreatedByUser { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallQuickTemplateId; }
			set { CallQuickTemplateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallQuickTemplates";

		[NotMapped]
		public string IdName => "CallQuickTemplateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "CreatedByUser" };
	}
}
