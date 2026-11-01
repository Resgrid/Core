using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.hydrants (plan §7.8): out-of-service and test-due counts; the longest out-of-service hydrants for the mini map.</summary>
	public sealed class RecordsHydrantsWidgetData
	{
		public int OutOfService { get; set; }

		public int TestDue { get; set; }

		public List<RecordsHydrantItem> OutOfServiceHydrants { get; set; } = new List<RecordsHydrantItem>();
	}

	public sealed class RecordsHydrantItem
	{
		/// <summary>Opens /User/RecordHydrants/Details?id={HydrantId}.</summary>
		public string HydrantId { get; set; }

		public string Number { get; set; }

		/// <summary>RmsHydrantType.</summary>
		public int Type { get; set; }

		public decimal Latitude { get; set; }

		public decimal Longitude { get; set; }

		public DateTime? OutOfServiceSince { get; set; }
	}
}
