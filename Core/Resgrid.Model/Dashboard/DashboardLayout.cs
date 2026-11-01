using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard
{
	/// <summary>
	/// A dashboard board's layout (plan §5.1), stored as JSON in UserDashboards / DepartmentDashboardTemplates. Order is the
	/// array order; one layout renders at every width (D4).
	/// </summary>
	public sealed class DashboardLayout
	{
		public const int CurrentSchemaVersion = 1;

		public const int MaxWidgets = 40;

		/// <summary>Serialized limit; a larger layout is rejected rather than truncated.</summary>
		public const int MaxJsonLength = 65536;

		public int SchemaVersion { get; set; } = CurrentSchemaVersion;

		/// <summary>"comfortable" or "compact".</summary>
		public string Density { get; set; } = DashboardDensities.Comfortable;

		public List<DashboardLayoutWidget> Widgets { get; set; } = new List<DashboardLayoutWidget>();
	}

	public static class DashboardLimits
	{
		/// <summary>Boards (tabs) per member per department, home included (plan Q5).</summary>
		public const int MaxBoards = 5;

		public const int MaxTemplatesPerDepartment = 25;

		public const int MaxNameLength = 100;

		public const int MaxDescriptionLength = 500;
	}

	public static class DashboardDensities
	{
		public const string Comfortable = "comfortable";
		public const string Compact = "compact";
	}

	/// <summary>One widget instance on a board.</summary>
	public sealed class DashboardLayoutWidget
	{
		/// <summary>Client-generated instance id (GUID string); unique within the board.</summary>
		public string Id { get; set; }

		public string Key { get; set; }

		public string Size { get; set; }

		/// <summary>Validated against the widget's setting allow-list; unknown names are dropped.</summary>
		public Dictionary<string, object> Settings { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);

		public bool Collapsed { get; set; }
	}
}
