using Microsoft.AspNet.SignalR;
using Resgrid.Model.Events;
using Resgrid.Services.CoreWeb;

namespace Resgrid.Web.Services.Hubs
{
	public class SignalRWebEventPublisher : IWebEventPublisher
	{
		public void Publish(IDepartmentEvent message)
		{
			//var @event = new EventWrapper(message);
			//var context = GlobalHost.ConnectionManager.GetConnectionContext<WebEvents>();
			//context.Groups.Send(message.DepartmentId.ToString(), @event);
		}
	}
}