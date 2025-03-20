using System;

namespace Resgrid.WebCore.Areas.User.Models.Orders
{
    public class ResourceOrderJson
    {
		public int Id { get; set; }

		public int DepartmentId { get; set; }

		public int Type { get; set; }

		public bool AllowPartialFills { get; set; }

		public string Title { get; set; }

		public string IncidentNumber { get; set; }

		public string IncidentName { get; set; }

		public string IncidentAddress { get; set; }

		public decimal? IncidentLatitude { get; set; }

		public decimal? IncidentLongitude { get; set; }

		public string Summary { get; set; }

		public DateTime OpenDate { get; set; }

		public DateTime NeededBy { get; set; }

		public DateTime? MeetupDate { get; set; }

		public DateTime? CloseDate { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public string SpecialInstructions { get; set; }

		public string MeetupLocation { get; set; }

		public string FinancialCode { get; set; }

		public bool AutomaticFillAcceptance { get; set; }

		public int Visibility { get; set; }

		public int Range { get; set; }

		public double? OriginLatitude { get; set; }

		public double? OriginLongitude { get; set; }

		public string Status { get; set; }

		public string VisibilityName { get; set; }

		public int ResourceOrderCount { get; set; }

		public int TotalUnitsOrdered { get; set; }

		public int TotalUntisFilled { get; set; }
	}
}