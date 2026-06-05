using System.Collections.Generic;
using Resgrid.Model.Reporting;

namespace Resgrid.Web.Services.Models.v4.Reporting
{
	/// <summary>Composite dashboard report (scalar totals, dense series, breakdowns).</summary>
	public class DashboardReportResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public DashboardReport Data { get; set; }
	}

	/// <summary>Response-time / NFPA analytics report.</summary>
	public class ResponseTimeReportResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public ResponseTimeReport Data { get; set; }
	}

	/// <summary>Unit Hour Utilization analytics report.</summary>
	public class UtilizationReportResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public UtilizationReport Data { get; set; }
	}

	/// <summary>Personnel participation analytics report.</summary>
	public class ParticipationReportResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public ParticipationReport Data { get; set; }
	}

	/// <summary>Lists the standardized required export fields Resgrid does not capture (the gap report).</summary>
	public class ExportGapReportResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public List<string> Data { get; set; } = new List<string>();
	}
}
