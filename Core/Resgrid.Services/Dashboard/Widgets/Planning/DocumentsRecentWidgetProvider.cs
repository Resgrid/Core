using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// documents.recent (plan §7.6): one top-N metadata query (IDocumentsService.GetRecentDocumentsByDepartmentIdAsync) —
	/// no file bytes, no ADP-catalogued description or file name. Admins-only documents for department admins only, as on
	/// the Documents page. The category setting only narrows a department-scoped query, so a foreign value matches nothing.
	/// </summary>
	[DashboardWidget("documents.recent")]
	public class DocumentsRecentWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IDocumentsService _documentsService;

		public DocumentsRecentWidgetProvider(IDocumentsService documentsService)
		{
			_documentsService = documentsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var includeAdminsOnly = context.Viewer?.IsDepartmentAdmin == true;
			var documents = await _documentsService.GetRecentDocumentsByDepartmentIdAsync(context.DepartmentId, context.GetInt("maxItems", 8), includeAdminsOnly,
				context.GetString("category")) ?? new List<Document>();

			return new DocumentsRecentWidgetData
			{
				Items = documents
					.Where(d => d != null && d.DepartmentId == context.DepartmentId && (includeAdminsOnly || !d.AdminsOnly))
					.Select(d => new DocumentsRecentItem
					{
						DocumentId = d.DocumentId,
						Name = d.Name,
						Category = d.Category,
						Type = d.Type,
						Icon = d.GetIconType(),
						AdminsOnly = d.AdminsOnly,
						AddedOnUtc = d.AddedOn,
						Url = "/User/Documents/ViewDocument?documentId=" + d.DocumentId.ToString(CultureInfo.InvariantCulture)
					})
					.ToList()
			};
		}
	}
}
