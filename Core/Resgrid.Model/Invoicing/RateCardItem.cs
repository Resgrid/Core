using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>One priced line on a rate card. Rates are decimal(18,4); rounding and minimums are applied by the draft-line generator (plan decision 9).</summary>
	public class RateCardItem : IEntity
	{
		[Required]
		public string RateCardItemId { get; set; }

		[Required]
		public string RateCardId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary><see cref="RateCardItemTypes"/>.</summary>
		public int ItemType { get; set; }

		[Required]
		public string Name { get; set; }
		public string Description { get; set; }
		public decimal Rate { get; set; }

		/// <summary>Printed after the quantity ("hour", "mile", "each").</summary>
		public string UnitLabel { get; set; }

		/// <summary>Hourly items: bill at least this many minutes.</summary>
		public int? MinimumMinutes { get; set; }

		/// <summary>Hourly items: round elapsed time up to this increment.</summary>
		public int? RoundingMinutes { get; set; }
		public decimal? MinimumCharge { get; set; }

		/// <summary>Hourly-unit items: only units whose Unit.Type equals this type name generate a line.</summary>
		public string UnitTypeFilter { get; set; }

		/// <summary>Hourly-personnel items: only personnel holding this role generate a line.</summary>
		public int? PersonnelRoleIdFilter { get; set; }
		public bool Taxable { get; set; } = true;
		public int SortOrder { get; set; }
		public bool Active { get; set; } = true;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped]
		public string TableName => "RateCardItems";

		[NotMapped]
		public string IdName => "RateCardItemId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RateCardItemId; }
			set { RateCardItemId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
