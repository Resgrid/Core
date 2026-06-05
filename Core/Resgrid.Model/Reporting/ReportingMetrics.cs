namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Canonical <see cref="ReportingDailyRollup.Metric"/> names shared by the rollup worker (writer)
	/// and the reporting service (reader). Durations are stored in seconds; rates are 0..1.
	/// </summary>
	public static class ReportingMetrics
	{
		/// <summary>Count of calls in the bucket (uses ItemCount only).</summary>
		public const string CallCount = "CallCount";

		/// <summary>Alarm handling / call processing time: LoggedOn -> DispatchOn (NFPA 1221), seconds.</summary>
		public const string CallProcessingSeconds = "CallProcessingSeconds";

		/// <summary>Turnout time: dispatch -> first unit enroute/responding, seconds.</summary>
		public const string TurnoutSeconds = "TurnoutSeconds";

		/// <summary>Travel time: enroute -> first on-scene, seconds.</summary>
		public const string TravelSeconds = "TravelSeconds";

		/// <summary>Total response time: LoggedOn -> first on-scene (NFPA 1710/1720), seconds.</summary>
		public const string TotalResponseSeconds = "TotalResponseSeconds";

		/// <summary>Unit Hour Utilization (committed time / in-service time), 0..1.</summary>
		public const string UnitHourUtilization = "Uhu";

		/// <summary>Member call response rate (calls responded / calls in period), 0..1.</summary>
		public const string MemberResponseRate = "ResponseRate";
	}
}
