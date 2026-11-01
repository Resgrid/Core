using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.legalHolds (plan §7.8): active preservation holds. Reference numbers and notes are protected and never included.</summary>
	public sealed class RecordsLegalHoldsWidgetData
	{
		public int Active { get; set; }

		/// <summary>Holds on one Record (the rest are scope holds by definition or period).</summary>
		public int OnRecords { get; set; }

		public DateTime? NewestPlacedOn { get; set; }

		/// <summary>Newest first, at most maxRows.</summary>
		public List<RecordsLegalHoldItem> Holds { get; set; } = new List<RecordsLegalHoldItem>();
	}

	public sealed class RecordsLegalHoldItem
	{
		public string HoldId { get; set; }

		/// <summary>Null for a scope hold (definition and/or period).</summary>
		public string RecordId { get; set; }

		public string DefinitionKey { get; set; }

		public DateTime? PeriodStart { get; set; }

		public DateTime? PeriodEnd { get; set; }

		public DateTime PlacedOn { get; set; }
	}
}
