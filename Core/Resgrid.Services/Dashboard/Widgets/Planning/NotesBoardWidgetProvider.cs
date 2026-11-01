using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// notes.board (plan §7.6, §9.6): GetNotesForDepartmentFilteredAsync (admin-only notes for department admins, the Notes
	/// page's rule), newest first, bodies reduced to plain text server-side so no stored HTML reaches the client.
	/// </summary>
	[DashboardWidget("notes.board")]
	public class NotesBoardWidgetProvider : IDashboardWidgetDataProvider
	{
		public const int PreviewLength = 200;

		private readonly INotesService _notesService;

		public NotesBoardWidgetProvider(INotesService notesService)
		{
			_notesService = notesService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var isAdmin = context.Viewer?.IsDepartmentAdmin == true;
			var category = context.GetString("category");
			var hideExpired = context.GetBool("hideExpired", true);

			var notes = (await _notesService.GetNotesForDepartmentFilteredAsync(context.DepartmentId, isAdmin) ?? new List<Note>())
				.Where(n => n != null && n.DepartmentId == context.DepartmentId && (isAdmin || !n.IsAdminOnly))
				.Where(n => category == null || string.Equals(n.Category, category, StringComparison.OrdinalIgnoreCase))
				.Where(n => !hideExpired || !n.ExpiresOn.HasValue || n.ExpiresOn.Value >= context.NowUtc)
				.OrderByDescending(n => n.AddedOn)
				.ThenByDescending(n => n.NoteId)
				.Take(context.GetInt("maxItems", 6));

			return new NotesBoardWidgetData
			{
				Items = notes.Select(n => new NotesBoardItem
				{
					NoteId = n.NoteId,
					Title = n.Title,
					Preview = Preview(n.Body),
					Category = n.Category,
					Color = n.Color,
					IsAdminOnly = n.IsAdminOnly,
					AddedOnUtc = n.AddedOn,
					ExpiresOnUtc = n.ExpiresOn,
					Url = "/User/Notes/View?noteId=" + n.NoteId.ToString(CultureInfo.InvariantCulture)
				}).ToList()
			};
		}

		/// <summary>Plain text (StringHelpers.HtmlToPlainText), cut at a word boundary to <see cref="PreviewLength"/> with an ellipsis.</summary>
		public static string Preview(string body)
		{
			var text = StringHelpers.HtmlToPlainText(body);
			if (text.Length <= PreviewLength)
				return text;

			var cut = text.LastIndexOf(' ', PreviewLength - 1);
			return (cut > PreviewLength / 2 ? text.Substring(0, cut) : text.Substring(0, PreviewLength - 1)).TrimEnd() + "…";
		}
	}
}
