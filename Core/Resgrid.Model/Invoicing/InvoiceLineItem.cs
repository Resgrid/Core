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

		/// <summary>Phase C provenance (M0219): the approved daily time report this line was generated from.</summary>
		public string DeploymentTimeReportId { get; set; }

		/// <summary>
		/// M0230: where a generated hourly unit line's on-scene time came from (<see cref="InvoiceLineTimeSources"/>), so the
		/// clerk can see when the billed time rests on a status Resgrid linked or inferred. Null for other lines.
		/// </summary>
		public int? TimeSource { get; set; }
		public int SortOrder { get; set; }

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
