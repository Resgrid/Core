using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// general.stickyNote (plan §7.6): no server data — the note is the instance's own "text" setting (validated and capped by
	/// the layout validator). The provider exists so the frame's data call succeeds; it echoes the setting back.
	/// </summary>
	[DashboardWidget("general.stickyNote")]
	public class StickyNoteWidgetProvider : IDashboardWidgetDataProvider
	{
		public const int MaxTextLength = 2000;

		public Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var text = context.GetString("text", string.Empty) ?? string.Empty;
			if (text.Length > MaxTextLength)
				text = text.Substring(0, MaxTextLength);

			return Task.FromResult<object>(new StickyNoteWidgetData { Text = text });
		}
	}
}
