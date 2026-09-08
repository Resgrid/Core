using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>Display helpers for the RMS-6 analytics pages: seconds as m:ss, percentages, deltas.</summary>
	public static class RmsAnalyticsDisplay
	{
		public static string Seconds(double? value)
		{
			if (!value.HasValue) return "-";
			var total = (int)Math.Round(value.Value);
			var hours = total / 3600; var minutes = (total % 3600) / 60; var seconds = total % 60;
			return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
		}

		public static string Percent(double? value) => value.HasValue ? value.Value.ToString("0.0") + "%" : "-";
		public static string Number(double? value) => value.HasValue ? value.Value.ToString("0.#") : "-";
		public static string Hours(double value) => value.ToString("0.#");

		public static string DeltaClass(double? change) => !change.HasValue ? "text-muted" : change.Value > 0 ? "text-navy" : change.Value < 0 ? "text-danger" : "text-muted";
		public static string Delta(double? change) => !change.HasValue ? "-" : (change.Value > 0 ? "+" : "") + change.Value.ToString("0.#") + "%";

		/// <summary>Per-cell heat-map class from 0 (empty) to 4 (busiest) relative to the page's maximum.</summary>
		public static string Heat(int count, int max) => count == 0 || max == 0 ? "heat-0" : "heat-" + Math.Clamp((int)Math.Ceiling(4.0 * count / max), 1, 4);
	}

	/// <summary>Filter state shared by every analytics page; the controller resolves it from the query string.</summary>
	public abstract class RecordsAnalyticsBaseView : RecordsPreventionBaseView
	{
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public int? StationGroupId { get; set; }
		public string DefinitionKey { get; set; }
		public int TurnoutTargetSeconds { get; set; } = 80;
		public int TravelTargetSeconds { get; set; } = 240;
		public List<SelectListItem> Groups { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Definitions { get; set; } = new List<SelectListItem>();

		/// <summary>The controller action this page posts its filter back to.</summary>
		public string Action { get; set; }
		public bool ShowDefinitionFilter { get; set; }
		public bool ShowTargets { get; set; }
		public abstract RecordsAnalyticsBase Result { get; }
	}

	public class RecordsExecutiveView : RecordsAnalyticsBaseView
	{
		public RecordsExecutiveSummary Summary { get; set; }
		public override RecordsAnalyticsBase Result => Summary;
	}

	public class RecordsResponsePerformanceView : RecordsAnalyticsBaseView
	{
		public RecordsResponsePerformance Performance { get; set; }
		public override RecordsAnalyticsBase Result => Performance;
	}

	public class RecordsWorkloadView : RecordsAnalyticsBaseView
	{
		public RecordsWorkload Workload { get; set; }
		public override RecordsAnalyticsBase Result => Workload;
	}

	public class RecordsAccreditationView : RecordsAnalyticsBaseView
	{
		public RecordsAccreditation Accreditation { get; set; }
		public override RecordsAnalyticsBase Result => Accreditation;
	}

	public class RecordsCommunityRiskView : RecordsAnalyticsBaseView
	{
		public RecordsCommunityRisk Risk { get; set; }
		public override RecordsAnalyticsBase Result => Risk;
	}
}
