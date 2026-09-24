namespace Resgrid.Web.Mcp
{
	/// <summary>
	/// Every v4 API route the MCP server calls, grouped by the HTTP method it is called with. Query strings are
	/// appended at the call site.
	/// </summary>
	/// <remarks>
	/// Call the API only through these constants. McpRouteConformanceTests checks each one against the route and
	/// HTTP method attributes of the v4 controllers, and fails on any "api/v4/" literal outside this class, so a
	/// renamed or removed endpoint fails a test instead of returning 404 at runtime.
	/// </remarks>
	public static class V4Routes
	{
		public static class Get
		{
			public const string ActiveCalls = "/api/v4/Calls/GetActiveCalls";
			public const string Call = "/api/v4/Calls/GetCall";

			public const string AllPersonnelInfos = "/api/v4/Personnel/GetAllPersonnelInfos";
			public const string MapDataAndMarkers = "/api/v4/Mapping/GetMapDataAndMarkers";

			public const string AllUnitsInfos = "/api/v4/Units/GetAllUnitsInfos";
			public const string AllUnitStatuses = "/api/v4/UnitStatus/GetAllUnitStatuses";

			public const string InboxMessages = "/api/v4/Messages/GetInboxMessages";
			public const string OutboxMessages = "/api/v4/Messages/GetOutboxMessages";
			public const string Message = "/api/v4/Messages/GetMessage";

			public const string DepartmentCalendarItems = "/api/v4/Calendar/GetDepartmentCalendarItems";
			public const string DepartmentCalendarItemsInRange = "/api/v4/Calendar/GetDepartmentCalendarItemsInRange";

			public const string Shifts = "/api/v4/Shifts/GetShifts";
			public const string Shift = "/api/v4/Shifts/GetShift";
			public const string TodaysShifts = "/api/v4/Shifts/GetTodaysShifts";

			public const string InventoryItems = "/api/v4/Inventory/GetAll";
			public const string InventoryItem = "/api/v4/Inventory/GetItem";
			public const string LowStockItems = "/api/v4/Inventory/GetLowStockItems";

			public const string ResponseTimesReport = "/api/v4/Reporting/GetResponseTimes";
			public const string ParticipationReport = "/api/v4/Reporting/GetParticipation";
			public const string UtilizationReport = "/api/v4/Reporting/GetUtilization";
			public const string DashboardReport = "/api/v4/Reporting/GetDashboard";
		}

		public static class Post
		{
			public const string Token = "/api/v4/connect/token";
			public const string SaveCall = "/api/v4/Calls/SaveCall";
			public const string SavePersonStatus = "/api/v4/PersonnelStatuses/SavePersonStatus";
			public const string SaveUnitStatus = "/api/v4/UnitStatus/SaveUnitStatus";
			public const string SendMessage = "/api/v4/Messages/SendMessage";
			public const string SignupForShiftDay = "/api/v4/Shifts/SignupForShiftDay";
		}

		public static class Put
		{
			public const string CloseCall = "/api/v4/Calls/CloseCall";
			public const string UpdateInventoryItem = "/api/v4/Inventory/UpdateItem";
		}

		public static class Delete
		{
			public const string Message = "/api/v4/Messages/DeleteMessage";
		}
	}
}
