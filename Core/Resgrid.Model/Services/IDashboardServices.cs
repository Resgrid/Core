using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;

namespace Resgrid.Model.Services
{
	/// <summary>Boards, templates and layouts for the home dashboard (home-dashboard-redesign-plan.md §4.2, §5).</summary>
	public interface IDashboardService
	{
		/// <summary>Every board the member has, resolved (plan §5.3); a member who never saved gets one resolved home board.</summary>
		Task<DashboardBoards> GetBoardsAsync(int departmentId, string userId, DashboardViewer viewer);

		/// <summary>One board: <paramref name="boardId"/>, or the home board when null/unknown.</summary>
		Task<DashboardResolvedBoard> GetBoardAsync(int departmentId, string userId, DashboardViewer viewer, string boardId = null);

		/// <summary>
		/// Validates and saves a board. A null <paramref name="boardId"/> saves the home board (creating the row on first save).
		/// <paramref name="rowVersion"/> must equal the stored value or the result is a Conflict carrying the current board.
		/// </summary>
		Task<DashboardSaveResult> SaveBoardAsync(int departmentId, string userId, DashboardViewer viewer, string boardId, int rowVersion,
			DashboardLayout layout, string name, CancellationToken cancellationToken);

		/// <summary>Adds an extra board (cap DashboardLimits.MaxBoards) seeded from the resolved default.</summary>
		Task<DashboardSaveResult> AddBoardAsync(int departmentId, string userId, DashboardViewer viewer, string name, CancellationToken cancellationToken);

		/// <summary>Deletes an extra board; the home board cannot be deleted (reset it instead).</summary>
		Task<bool> DeleteBoardAsync(int departmentId, string userId, string boardId, CancellationToken cancellationToken);

		/// <summary>Resets a board to the member's resolved department default (the home board's row is removed).</summary>
		Task<DashboardResolvedBoard> ResetBoardAsync(int departmentId, string userId, DashboardViewer viewer, string boardId, CancellationToken cancellationToken);

		/// <summary>The member's "use classic dashboard" switch (plan D10).</summary>
		Task SetUseClassicAsync(int departmentId, string userId, bool useClassic, CancellationToken cancellationToken);

		Task<bool> GetUseClassicAsync(int departmentId, string userId);

		/// <summary>The built-in default for an audience (plan §8), or the Classic layout when <paramref name="classic"/>.</summary>
		DashboardLayout GetBuiltInLayout(DashboardAudiences audience, bool classic = false);

		Task<List<DepartmentDashboardTemplate>> GetTemplatesAsync(int departmentId);

		Task<DepartmentDashboardTemplate> GetTemplateAsync(int departmentId, string templateId);

		/// <summary>Department-admin only (checked by the caller); validates the layout like a board save.</summary>
		Task<DashboardTemplateSaveResult> SaveTemplateAsync(int departmentId, string userId, DashboardTemplateSaveRequest request, CancellationToken cancellationToken);

		Task<bool> DeleteTemplateAsync(int departmentId, string userId, string templateId, CancellationToken cancellationToken);

		/// <summary>Options for settings that draw from department data (DashboardOptionSources), as value → label.</summary>
		Task<Dictionary<string, List<KeyValuePair<string, string>>>> GetSettingOptionsAsync(int departmentId, string userId, IEnumerable<string> sources);

		/// <summary>Drops the member's cached boards (after a save elsewhere, or a role/group change).</summary>
		Task InvalidateUserAsync(int departmentId, string userId);
	}

	/// <summary>Widget visibility for a viewer (plan §9): one snapshot per request, checks evaluated only when referenced.</summary>
	public interface IDashboardEntitlementService
	{
		/// <summary>
		/// Completes a viewer the web tier started (department, user, admin flags, granted policies, roles, groups): resolves
		/// the feature flags, access checks, module permissions, add-ons and ADP facts the catalog references, and the audience.
		/// </summary>
		Task<DashboardViewer> CompleteViewerAsync(DashboardViewer viewer);

		/// <summary>Completes only what <paramref name="definition"/> needs (the per-call re-check of the data endpoint).</summary>
		Task<DashboardViewer> CompleteViewerForWidgetAsync(DashboardViewer viewer, DashboardWidgetDefinition definition);

		bool IsVisible(DashboardWidgetDefinition definition, DashboardViewer viewer);

		/// <summary>Department admin, every gate passes except the widget's add-on, and the add-on is offered (plan D8).</summary>
		bool IsLockedForAdmin(DashboardWidgetDefinition definition, DashboardViewer viewer);

		IReadOnlyList<DashboardWidgetDefinition> GetVisibleWidgets(DashboardViewer viewer);

		IReadOnlyList<DashboardWidgetDefinition> GetLockedWidgets(DashboardViewer viewer);
	}

	/// <summary>Runs widget providers with gates re-checked and settings validated (plan §4.2, D3).</summary>
	public interface IDashboardWidgetDataService
	{
		/// <summary>Data for a stored instance (settings come from the stored layout, never the request).</summary>
		Task<DashboardWidgetDataResult> GetDataAsync(DashboardViewer viewer, DashboardLayoutWidget instance, string protectedGrantToken,
			CancellationToken cancellationToken);

		/// <summary>Data for unsaved settings (edit mode, gallery, template editor); same validation and gates.</summary>
		Task<DashboardWidgetDataResult> GetPreviewAsync(DashboardViewer viewer, string key, IDictionary<string, object> settings, string size,
			string protectedGrantToken, CancellationToken cancellationToken);
	}

	/// <summary>
	/// One widget's data (plan §11). Implementations carry [DashboardWidget("key")] and are registered keyed by that key
	/// (InstancePerLifetimeScope) by assembly scanning — no module edit per widget. Return a DTO from
	/// Resgrid.Model.Dashboard.Widgets; never an entity, never a catalogued (ADP) value outside DashboardProtectedText.
	/// </summary>
	public interface IDashboardWidgetDataProvider
	{
		Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Department-specific options for a setting (DashboardOptionSources). Implementations carry [DashboardOptionSource("name")]
	/// and are registered keyed by assembly scanning. Values are ids as strings; labels are display text.
	/// </summary>
	public interface IDashboardOptionSource
	{
		Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId);
	}
}
