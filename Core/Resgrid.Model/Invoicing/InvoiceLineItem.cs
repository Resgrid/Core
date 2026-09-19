using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>One invoice line. CallId links a line to the call it bills (plan decision 8): one invoice may consolidate many calls, and a call may yield several lines.</summary>
	public class InvoiceLineItem : IEntity
	{
		[Required]
		public string InvoiceLineItemId { get; set; }

		[Required]
		public string InvoiceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }
		public int? CallId { get; set; }
		public string RateCardItemId { get; set; }

		[Required]
		public string Description { get; set; }
		public decimal Quantity { get; set; } = 1;
		public decimal UnitRate { get; set; }
		public decimal Amount { get; set; }
		public bool Taxable { get; set; } = true;
		public int SortOrder { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		[NotMapped]
		public string TableName => "InvoiceLineItems";

		[NotMapped]
		public string IdName => "InvoiceLineItemId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InvoiceLineItemId; }
			set { InvoiceLineItemId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
