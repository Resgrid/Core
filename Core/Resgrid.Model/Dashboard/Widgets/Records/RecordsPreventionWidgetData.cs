namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.prevention (plan §7.8): prevention workload. A tile group is present (its Show* flag true) only when that
	/// module's flag is on, exactly as the sidebar shows its link; the client renders only those tiles.
	/// </summary>
	public sealed class RecordsPreventionWidgetData
	{
		public bool ShowOccupancies { get; set; }

		public int Occupancies { get; set; }

		public int OccupancyReviewsOverdue { get; set; }

		public bool ShowInspections { get; set; }

		public int InspectionsScheduled { get; set; }

		public int InspectionsDue { get; set; }

		public int ViolationsOpen { get; set; }

		public int ViolationsOverdue { get; set; }

		public bool ShowHydrants { get; set; }

		public int Hydrants { get; set; }

		public int HydrantsOutOfService { get; set; }

		public int HydrantsTestDue { get; set; }

		public bool ShowPermits { get; set; }

		public int PermitsActive { get; set; }

		public int PermitsExpiringSoon { get; set; }

		public int PermitsAwaitingReview { get; set; }

		public bool ShowCrr { get; set; }

		public int CrrActivitiesLast90Days { get; set; }

		public int CrrAlarmsLast90Days { get; set; }

		/// <summary>Investigations flag on and the viewer holds restricted Records access.</summary>
		public bool ShowInvestigations { get; set; }

		public int InvestigationCasesOpen { get; set; }
	}
}
