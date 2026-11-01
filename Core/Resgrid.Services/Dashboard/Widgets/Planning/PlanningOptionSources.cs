using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// Department-specific setting options for the planning widgets (contract §2.7). Kept with the widgets that use them;
	/// the coordinator may move the names into DashboardOptionSources. Values are what the provider re-checks.
	/// </summary>
	public static class PlanningOptionSources
	{
		/// <summary>Calendar item types (value = CalendarItemTypeId).</summary>
		public const string CalendarTypes = DashboardOptionSources.CalendarTypes;

		/// <summary>Document categories (value = category name, which is what a document stores).</summary>
		public const string DocumentCategories = DashboardOptionSources.DocumentCategories;

		/// <summary>Note categories (value = category name, which is what a note stores).</summary>
		public const string NoteCategories = DashboardOptionSources.NoteCategories;
	}

	[DashboardOptionSource(PlanningOptionSources.CalendarTypes)]
	public class DashboardCalendarTypesOptionSource : IDashboardOptionSource
	{
		private readonly ICalendarService _calendarService;

		public DashboardCalendarTypesOptionSource(ICalendarService calendarService)
		{
			_calendarService = calendarService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var types = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(departmentId) ?? new List<CalendarItemType>();
			return types.Where(t => t != null && t.DepartmentId == departmentId)
				.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
				.Select(t => new KeyValuePair<string, string>(t.CalendarItemTypeId.ToString(CultureInfo.InvariantCulture), t.Name))
				.ToList();
		}
	}

	[DashboardOptionSource(PlanningOptionSources.DocumentCategories)]
	public class DashboardDocumentCategoriesOptionSource : IDashboardOptionSource
	{
		private readonly IDocumentsService _documentsService;

		public DashboardDocumentCategoriesOptionSource(IDocumentsService documentsService)
		{
			_documentsService = documentsService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var categories = await _documentsService.GetAllCategoriesByDepartmentIdAsync(departmentId) ?? new List<DocumentCategory>();
			return categories.Where(c => c != null && c.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(c.Name))
				.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First().Name)
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.Select(n => new KeyValuePair<string, string>(n, n))
				.ToList();
		}
	}

	[DashboardOptionSource(PlanningOptionSources.NoteCategories)]
	public class DashboardNoteCategoriesOptionSource : IDashboardOptionSource
	{
		private readonly INotesService _notesService;

		public DashboardNoteCategoriesOptionSource(INotesService notesService)
		{
			_notesService = notesService;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			var categories = await _notesService.GetAllCategoriesByDepartmentIdAsync(departmentId) ?? new List<NoteCategory>();
			return categories.Where(c => c != null && c.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(c.Name))
				.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First().Name)
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.Select(n => new KeyValuePair<string, string>(n, n))
				.ToList();
		}
	}
}
