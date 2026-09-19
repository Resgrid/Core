using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>Department-scoped per-call pricing (plan decision 5). A department has at most one default card; a billing profile may pin another.</summary>
	public class RateCard : IEntity
	{
		[Required]
		public string RateCardId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsDefault { get; set; }
		public bool Active { get; set; } = true;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		[NotMapped]
		public List<RateCardItem> Items { get; set; }

		[NotMapped]
		public string TableName => "RateCards";

		[NotMapped]
		public string IdName => "RateCardId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RateCardId; }
			set { RateCardId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Items" };
	}
}
