using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	public class FormAutomation : IEntity
	{
		[Required]
		public string FormAutomationId { get; set; }

		[Required]
		public string FormId { get; set; }

		public virtual Form Form { get; set; }

		[Required]
		public string TriggerField { get; set; }

		public string TriggerValue { get; set; }

		public int OperationType { get; set; }

		public string OperationValue { get; set; }


		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FormAutomationId; }
			set { FormAutomationId = (string)value; }
		}

		[NotMapped]
		public string TableName => "FormAutomations";

		[NotMapped]
		public string IdName => "FormAutomationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Department", "Message" };
	}
}
