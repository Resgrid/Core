using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Time-bucketing granularity for reporting series. All buckets are anchored in UTC.
	/// </summary>
	public enum ReportGranularity
	{
		[Description("Day")]
		[Display(Name = "Day")]
		Day = 0,

		[Description("Month")]
		[Display(Name = "Month")]
		Month = 1
	}
}
