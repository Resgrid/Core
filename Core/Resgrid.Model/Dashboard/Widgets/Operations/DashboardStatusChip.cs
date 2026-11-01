namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>
	/// A unit status, personnel status or staffing level the way a widget shows it (home-dashboard-implementation-contract.md
	/// §2.3): the department's own option (ButtonText, ButtonColor, TextColor) or the built-in value's name and colour, so
	/// the client never keeps a status colour table of its own. Shared by the operations and personnel widgets.
	/// </summary>
	public sealed class DashboardStatusChip
	{
		/// <summary>The stored value: a built-in enum value, or the department's CustomStateDetailId.</summary>
		public int Id { get; set; }

		/// <summary>The department's button text, or the built-in value's English name (the client may localize it when <see cref="IsCustom"/> is false).</summary>
		public string Text { get; set; }

		/// <summary>Background colour, "#RRGGBB".</summary>
		public string Color { get; set; }

		/// <summary>Text colour on <see cref="Color"/>, "#RRGGBB".</summary>
		public string TextColor { get; set; }

		/// <summary>The option's meaning: an ActionBaseTypes value for statuses, a StaffingBaseTypes value for staffing; -1 for none.</summary>
		public int BaseType { get; set; }

		/// <summary>Reporting.AvailabilityClass: 0 unknown, 1 available, 2 committed, 3 unavailable, 4 delayed.</summary>
		public int Availability { get; set; }

		/// <summary>True for one of the department's own options; false for a built-in value.</summary>
		public bool IsCustom { get; set; }
	}

	/// <summary>A call priority with the department's colour (built-in priorities 0-3 keep their fixed colours).</summary>
	public sealed class DashboardCallPriority
	{
		public int Id { get; set; }

		public string Name { get; set; }

		/// <summary>"#RRGGBB".</summary>
		public string Color { get; set; }
	}

	/// <summary>One endpoint a widget calls for an action (all same-origin; POSTs carry the anti-forgery header).</summary>
	public sealed class DashboardWidgetEndpoint
	{
		/// <summary>Same-origin path, e.g. "/User/Home/SetCustomAction".</summary>
		public string Url { get; set; }

		/// <summary>"POST" or "DELETE".</summary>
		public string Method { get; set; } = "POST";

		/// <summary>
		/// How parameters travel: "query" (simple ids in the query string; the action binds them from query or form),
		/// "form" (application/x-www-form-urlencoded body; free text such as notes must go here, never in the URL) or
		/// "json" (a JSON body).
		/// </summary>
		public string Body { get; set; }

		/// <summary>Parameter names the endpoint binds, in the casing it expects.</summary>
		public string[] Parameters { get; set; } = System.Array.Empty<string>();

		/// <summary>
		/// False when the endpoint exists but this page cannot reach it yet (e.g. a v4 API route not on the web BFF
		/// allowlist); the widget then hides the action.
		/// </summary>
		public bool Available { get; set; } = true;
	}
}
