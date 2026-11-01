namespace Resgrid.Model.Dashboard.Widgets.Intelligence
{
	/// <summary>
	/// ai.assistant (plan §7.11): the client embeds the existing assistant conversation (the component behind &lt;rg-assistant&gt;) in the
	/// frame; the conversation's text-only restrictions stay server-enforced. The provider only confirms availability, so the widget
	/// never renders for a department whose gates have since failed.
	/// </summary>
	public sealed class AssistantWidgetData
	{
		public bool Available { get; set; }
	}
}
