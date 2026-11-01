using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class DocumentsRecentWidgetProviderTests
	{
		[TestCase(true)]
		[TestCase(false)]
		public void The_recent_query_never_selects_the_file_or_the_protected_columns(bool postgres)
		{
			foreach (var adminsOnly in new[] { true, false })
			foreach (var category in new[] { true, false })
			{
				var sql = DocumentRepository.BuildRecentSql(postgres, "public", adminsOnly, category).ToLowerInvariant();
				var columns = sql.Substring(0, sql.IndexOf(" from ", StringComparison.Ordinal));
				var where = sql.Substring(sql.IndexOf(" where ", StringComparison.Ordinal));

				columns.Should().NotContain("data").And.NotContain("description").And.NotContain("filename").And.NotContain("*");
				where.Should().Contain("@departmentid");
				where.Contains("adminsonly").Should().Be(!adminsOnly);
				where.Contains("@category").Should().Be(category);
			}
		}

		[Test]
		public void The_dialects_use_their_own_paging_and_citext_cast()
		{
			DocumentRepository.BuildRecentSql(true, "public", false, true).Should().Contain("LIMIT @Take").And.Contain("@Category::citext").And.Contain("public.documents");
			DocumentRepository.BuildRecentSql(false, "dbo", false, true).Should().StartWith("SELECT TOP (@Take)").And.Contain("dbo.[Documents]");
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Admins_only_documents_follow_the_documents_page(bool isAdmin)
		{
			var documents = new Mock<IDocumentsService>(MockBehavior.Strict);
			documents.Setup(x => x.GetRecentDocumentsByDepartmentIdAsync(DepartmentId, 8, isAdmin, null)).ReturnsAsync(new List<Document>
			{
				new Document { DocumentId = 1, DepartmentId = DepartmentId, Name = "SOP", Type = "application/pdf", AddedOn = Now },
				new Document { DocumentId = 2, DepartmentId = DepartmentId, Name = "Board minutes", AdminsOnly = true, AddedOn = Now.AddDays(-1) },
				new Document { DocumentId = 3, DepartmentId = OtherDepartmentId, Name = "Not ours", AddedOn = Now }
			});
			var provider = new DocumentsRecentWidgetProvider(documents.Object);

			var data = (DocumentsRecentWidgetData)await provider.GetDataAsync(For("documents.recent", isAdmin: isAdmin), CancellationToken.None);

			data.Items.Select(i => i.DocumentId).Should().Equal(isAdmin ? new[] { 1, 2 } : new[] { 1 });
			data.Items[0].Icon.Should().Be("fa fa-file-pdf-o");
			data.Items[0].Url.Should().Be("/User/Documents/ViewDocument?documentId=1");
		}

		[Test]
		public async Task The_category_setting_narrows_the_query()
		{
			var documents = new Mock<IDocumentsService>(MockBehavior.Strict);
			documents.Setup(x => x.GetRecentDocumentsByDepartmentIdAsync(DepartmentId, 3, false, "Protocols")).ReturnsAsync(new List<Document>());
			var provider = new DocumentsRecentWidgetProvider(documents.Object);

			await provider.GetDataAsync(For("documents.recent", CommsPlanningWidgetContexts.Settings(("category", "Protocols"), ("maxItems", 3))), CancellationToken.None);

			documents.VerifyAll();
		}
	}
}
