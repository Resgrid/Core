using System.Collections.Generic;
using Resgrid.Model.Dashboard;

namespace Resgrid.Web.Areas.User.Models.Dashboard
{
	/// <summary>Host page for the rg-dashboard element (home-dashboard-redesign-plan.md §4.1).</summary>
	public class DashboardModernView
	{
		/// <summary>HomeDashboard + DashboardWidgets resx families for the current culture, key → text.</summary>
		public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();

		public bool FirstRun { get; set; }

		/// <summary>"board" (a member's dashboard) or "template" (the template editor).</summary>
		public string Mode { get; set; } = "board";

		public string TemplateId { get; set; }

		public bool ShowAdpReveal { get; set; }

		/// <summary>The request's UI culture (e.g. "de"), so the element formats numbers and dates like the rest of the page.</summary>
		public string Culture { get; set; }
	}

	public class DashboardBootstrapResult
	{
		public DashboardBoardResult Board { get; set; }

		public List<DashboardBoardSummaryResult> Boards { get; set; } = new List<DashboardBoardSummaryResult>();

		public bool UseClassic { get; set; }

		public List<DashboardWidgetDefinitionResult> Widgets { get; set; } = new List<DashboardWidgetDefinitionResult>();

		public List<DashboardLockedWidgetResult> Locked { get; set; } = new List<DashboardLockedWidgetResult>();

		public List<DashboardCategoryResult> Categories { get; set; } = new List<DashboardCategoryResult>();

		public DashboardHeaderResult Header { get; set; }

		public DashboardAdpResult Adp { get; set; }

		public DashboardMapResult Map { get; set; }

		/// <summary>Department option lists for settings that use an OptionsSource: source → options.</summary>
		public Dictionary<string, List<DashboardOptionResult>> Options { get; set; } = new Dictionary<string, List<DashboardOptionResult>>();

		public int DepartmentId { get; set; }

		public int MaxBoards { get; set; } = DashboardLimits.MaxBoards;

		public int MaxWidgets { get; set; } = DashboardLayout.MaxWidgets;

		/// <summary>Set in template mode: the template being edited (its layout is in Board).</summary>
		public DashboardTemplateResult Template { get; set; }
	}

	public class DashboardBoardResult
	{
		public string BoardId { get; set; }

		public string Name { get; set; }

		public int RowVersion { get; set; }

		public string Source { get; set; }

		public DashboardLayout Layout { get; set; }
	}

	public class DashboardBoardSummaryResult
	{
		public string BoardId { get; set; }

		public string Name { get; set; }

		public bool IsHome { get; set; }
	}

	public class DashboardWidgetDefinitionResult
	{
		public string Key { get; set; }

		public int Version { get; set; }

		public string Category { get; set; }

		public string Title { get; set; }

		public string Description { get; set; }

		public string Icon { get; set; }

		public string ModuleUrl { get; set; }

		public List<string> AllowedSizes { get; set; }

		public string DefaultSize { get; set; }

		public List<DashboardSettingResult> Settings { get; set; } = new List<DashboardSettingResult>();

		public List<string> RealtimeTopics { get; set; }

		public int DefaultRefreshSeconds { get; set; }

		public int MinRefreshSeconds { get; set; }

		public bool MultipleInstances { get; set; }

		public bool HasProtectedFields { get; set; }

		/// <summary>The department enforces ADP and the viewer's ADP permission covers at least one of the widget's protected fields (plan §9A.3).</summary>
		public bool CanRevealProtected { get; set; }
	}

	public class DashboardSettingResult
	{
		public string Name { get; set; }

		/// <summary>"number", "toggle", "select", "multiSelect", "text".</summary>
		public string Type { get; set; }

		public string Label { get; set; }

		public int? Min { get; set; }

		public int? Max { get; set; }

		public object Default { get; set; }

		public List<DashboardOptionResult> Options { get; set; } = new List<DashboardOptionResult>();

		public string OptionsSource { get; set; }
	}

	public class DashboardOptionResult
	{
		public string Value { get; set; }

		public string Label { get; set; }
	}

	public class DashboardLockedWidgetResult
	{
		public string Key { get; set; }

		public string Category { get; set; }

		public string Title { get; set; }

		public string Description { get; set; }

		public string Icon { get; set; }

		public string AddonName { get; set; }

		public string AddonUrl { get; set; }
	}

	public class DashboardCategoryResult
	{
		public string Key { get; set; }

		public string Name { get; set; }

		public string Color { get; set; }
	}

	public class DashboardHeaderResult
	{
		public string FirstName { get; set; }

		public string DepartmentName { get; set; }

		public string DepartmentCode { get; set; }

		/// <summary>IANA time zone for Intl (converted from the department's Windows id).</summary>
		public string TimeZone { get; set; }

		public string Audience { get; set; }

		public bool IsDepartmentAdmin { get; set; }

		public bool CanCreateCall { get; set; }

		public bool CanSendMessage { get; set; }

		public bool CanEditTemplates { get; set; }

		public string NewCallUrl { get; set; }

		public string MessageUrl { get; set; }

		public string ClassicUrl { get; set; }

		public string TemplatesUrl { get; set; }
	}

	public class DashboardAdpResult
	{
		public bool Enforced { get; set; }

		public bool CanRevealCall { get; set; }

		public bool CanRevealOperational { get; set; }

		public bool CanRevealPersonnel { get; set; }

		public bool CanRevealContact { get; set; }
	}

	public class DashboardMapResult
	{
		public string TileUrl { get; set; }

		public string Attribution { get; set; }

		public string MapProvider { get; set; }

		public string StyleUrl { get; set; }

		public string AccessToken { get; set; }
	}

	public class DashboardTemplateResult
	{
		public string TemplateId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public int Audience { get; set; }

		public int? TargetId { get; set; }

		public int Priority { get; set; }

		public bool IsDefault { get; set; }
	}

	public class DashboardPulseItemResult
	{
		public string Key { get; set; }

		public string Icon { get; set; }

		public int Value { get; set; }

		public string Label { get; set; }

		public string Url { get; set; }

		/// <summary>"", "warn" or "bad".</summary>
		public string Tone { get; set; }
	}

	public class DashboardSaveInput
	{
		public string BoardId { get; set; }

		public int RowVersion { get; set; }

		public string Name { get; set; }

		public DashboardLayout Layout { get; set; }
	}

	public class DashboardSaveResponse
	{
		public string Error { get; set; }

		public DashboardBoardResult Board { get; set; }

		public DashboardBoardResult Current { get; set; }
	}

	public class DashboardBoardInput
	{
		public string BoardId { get; set; }

		public string Name { get; set; }
	}

	public class DashboardClassicInput
	{
		public bool UseClassic { get; set; }
	}

	public class DashboardPreviewInput
	{
		public string Key { get; set; }

		public string Size { get; set; }

		public Dictionary<string, object> Settings { get; set; }
	}

	public class DashboardTemplateInput
	{
		public string TemplateId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public int Audience { get; set; }

		public int? TargetId { get; set; }

		public int Priority { get; set; }

		public bool IsDefault { get; set; }

		public DashboardLayout Layout { get; set; }
	}

	public class DashboardTemplatesIndexView
	{
		public List<DashboardTemplateRow> Templates { get; set; } = new List<DashboardTemplateRow>();
	}

	public class DashboardTemplateRow
	{
		public string TemplateId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public string AudienceText { get; set; }

		public int Priority { get; set; }

		public bool IsDefault { get; set; }

		public int WidgetCount { get; set; }
	}
}
