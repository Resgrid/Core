using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Intelligence;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Intelligence
{
	/// <summary>
	/// ai.assistant (plan §7.11). The client embeds the existing assistant conversation (the &lt;rg-assistant&gt; component); its
	/// restrictions are enforced by the chat/assistant endpoints, not here. Every gate (Ai.Assistant, Chat.System,
	/// IEnhancedAiAccessService.CanUseAsync(Ai.Assistant)) has already been re-checked for this call, so the provider reads nothing.
	/// </summary>
	[DashboardWidget("ai.assistant")]
	public class AssistantWidgetProvider : IDashboardWidgetDataProvider
	{
		public Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken) =>
			Task.FromResult<object>(new AssistantWidgetData { Available = true });
	}
}
