using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Records
{
	/// <summary>A call's report sources and the run reports written for it (GET ReportSources/GetCallSources).</summary>
	public class CallSourcesResult : StandardApiResponseV4Base
	{
		public CallSourcesResultData Data { get; set; }
	}

	public class CallSourcesResultData
	{
		/// <summary>
		/// The call's times, units (with crews), personnel, the full status/dispatch/command timeline with who set each entry
		/// (<see cref="CallSourceEntry.Origin"/> is a <see cref="StatusSetOrigins"/> value) and the Incident Command summary,
		/// including the NERIS tactic timestamps its objectives name. Times are UTC.
		/// </summary>
		public CallSourceData Sources { get; set; }

		/// <summary>The run and callback reports on the call the caller may view, newest first (the report being written excluded).</summary>
		public List<CallRunReport> RunReports { get; set; } = new List<CallRunReport>();
	}
}
