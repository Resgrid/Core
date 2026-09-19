using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>Per-department invoice numbering (plan decision 7). Only the repository's atomic increment writes it.</summary>
	public class InvoiceNumberSequence : IEntity
	{
		[Required]
		public int DepartmentId { get; set; }
		public int NextInvoiceNumber { get; set; } = 1;

		[NotMapped]
		public string TableName => "InvoiceNumberSequences";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
