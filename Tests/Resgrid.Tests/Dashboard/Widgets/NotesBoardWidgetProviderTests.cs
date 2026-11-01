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
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class NotesBoardWidgetProviderTests
	{
		private static Note Note(int id, int daysAgo, string body = "<p>Hello <b>crew</b></p><script>alert(1)</script>", string category = null, bool adminOnly = false,
			int? expiresInDays = null, int departmentId = DepartmentId) => new Note
		{
			NoteId = id, DepartmentId = departmentId, Title = "Note " + id, Body = body, Category = category, IsAdminOnly = adminOnly, AddedOn = Now.AddDays(-daysAgo),
			ExpiresOn = expiresInDays.HasValue ? Now.AddDays(expiresInDays.Value) : (System.DateTime?)null
		};

		[Test]
		public async Task Lists_newest_notes_as_plain_text_previews()
		{
			var notes = new Mock<INotesService>(MockBehavior.Strict);
			notes.Setup(x => x.GetNotesForDepartmentFilteredAsync(DepartmentId, false)).ReturnsAsync(new List<Note>
			{
				Note(1, 3), Note(2, 1), Note(3, 0, expiresInDays: -1), Note(4, 0, adminOnly: true), Note(5, 0, departmentId: OtherDepartmentId)
			});
			var provider = new NotesBoardWidgetProvider(notes.Object);

			var data = (NotesBoardWidgetData)await provider.GetDataAsync(For("notes.board"), CancellationToken.None);

			data.Items.Select(i => i.NoteId).Should().Equal(new[] { 2, 1 }, "expired, admin-only (for a member) and other-department notes are left out");
			data.Items[0].Preview.Should().Be("Hello crew");
			data.Items[0].Url.Should().Be("/User/Notes/View?noteId=2");
		}

		[Test]
		public async Task Admins_get_admin_notes_and_the_category_filter_applies()
		{
			var notes = new Mock<INotesService>(MockBehavior.Strict);
			notes.Setup(x => x.GetNotesForDepartmentFilteredAsync(DepartmentId, true)).ReturnsAsync(new List<Note>
			{
				Note(1, 1, category: "Dispatch", adminOnly: true), Note(2, 0, category: "Other"), Note(3, 2, category: "dispatch", expiresInDays: -3)
			});
			var provider = new NotesBoardWidgetProvider(notes.Object);

			var data = (NotesBoardWidgetData)await provider.GetDataAsync(
				For("notes.board", CommsPlanningWidgetContexts.Settings(("category", "Dispatch"), ("hideExpired", false)), isAdmin: true), CancellationToken.None);

			data.Items.Select(i => i.NoteId).Should().Equal(1, 3);
		}

		[Test]
		public void A_long_body_is_cut_at_a_word_with_an_ellipsis()
		{
			var preview = NotesBoardWidgetProvider.Preview(string.Join(" ", Enumerable.Repeat("word", 100)));

			preview.Length.Should().BeLessThanOrEqualTo(NotesBoardWidgetProvider.PreviewLength);
			preview.Should().EndWith("word…");
		}
	}
}
