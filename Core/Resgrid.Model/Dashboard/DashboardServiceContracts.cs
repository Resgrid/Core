using System.Collections.Generic;

namespace Resgrid.Model.Dashboard
{
	/// <summary>Where a resolved board came from (plan §5.3).</summary>
	public static class DashboardLayoutSources
	{
		public const string User = "user";
		public const string Template = "template";
		public const string Default = "default";
	}

	/// <summary>A member's board after resolution: their saved row, a matching template, or the built-in default.</summary>
	public sealed class DashboardResolvedBoard
	{
		/// <summary>UserDashboards id when <see cref="Source"/> is "user"; otherwise null.</summary>
		public string BoardId { get; set; }

		public string Name { get; set; }

		public int SortOrder { get; set; }

		public DashboardLayout Layout { get; set; }

		/// <summary>The saved row's RowVersion (0 when there is no row yet).</summary>
		public int RowVersion { get; set; }

		public string Source { get; set; }

		/// <summary>The template id the layout came from, when Source is "template" (or the saved row's TemplateId).</summary>
		public string TemplateId { get; set; }

		public bool UseClassic { get; set; }
	}

	/// <summary>Every board a member has (home first), plus the classic switch.</summary>
	public sealed class DashboardBoards
	{
		public List<DashboardResolvedBoard> Boards { get; set; } = new List<DashboardResolvedBoard>();

		public bool UseClassic { get; set; }
	}

	public enum DashboardSaveErrors
	{
		None = 0,
		/// <summary>The saved row changed since the caller loaded it (plan §5.4).</summary>
		Conflict = 1,
		Invalid = 2,
		TooLarge = 3,
		NotFound = 4,
		LimitReached = 5,
		NotAllowed = 6
	}

	public sealed class DashboardSaveResult
	{
		public DashboardSaveErrors Error { get; set; }

		public DashboardResolvedBoard Board { get; set; }

		/// <summary>On a conflict, the board as it is now.</summary>
		public DashboardResolvedBoard Current { get; set; }

		public bool Saved => Error == DashboardSaveErrors.None;
	}

	public sealed class DashboardTemplateSaveRequest
	{
		public string DepartmentDashboardTemplateId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public DashboardTemplateAudiences Audience { get; set; }

		public int? TargetId { get; set; }

		public int Priority { get; set; }

		public bool IsDefault { get; set; }

		public DashboardLayout Layout { get; set; }
	}

	public sealed class DashboardTemplateSaveResult
	{
		public DashboardSaveErrors Error { get; set; }

		public DepartmentDashboardTemplate Template { get; set; }
	}
}
