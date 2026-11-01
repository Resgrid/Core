using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class StickyNoteWidgetProviderTests
	{
		[Test]
		public async Task Echoes_the_note_capped_by_the_layout_validator()
		{
			var provider = new StickyNoteWidgetProvider();

			var empty = (StickyNoteWidgetData)await provider.GetDataAsync(For("general.stickyNote"), CancellationToken.None);
			var long_ = (StickyNoteWidgetData)await provider.GetDataAsync(
				For("general.stickyNote", CommsPlanningWidgetContexts.Settings(("text", new string('x', 5000)))), CancellationToken.None);

			empty.Text.Should().BeEmpty();
			long_.Text.Should().HaveLength(StickyNoteWidgetProvider.MaxTextLength);
		}
	}
}
