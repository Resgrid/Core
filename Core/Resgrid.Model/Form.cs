using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	public class Form : IEntity
	{
		[Required]
		public string FormId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string Name { get; set; }

		public int Type { get; set; }

		public bool IsActive { get; set; }

		public bool IsDeleted { get; set; }

		public string Data { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedBy { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedBy { get; set; }

		public virtual ICollection<FormAutomation> Automations { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FormId; }
			set { FormId = (string)value; }
		}

		[NotMapped]
		public string TableName => "Forms";

		[NotMapped]
		public string IdName => "FormId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Automations" };
	}
}
