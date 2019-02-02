using Autofac;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Hubs
{
	[HubName("eventingHub")]
	public class EventingHub : Hub
	{
		private readonly IInboundEventProvider _inboundEventProvider;
		private readonly IDepartmentLinksService _departmentLinksService;

		public EventingHub()
		{
			_inboundEventProvider = WebBootstrapper.GetKernel().Resolve<IInboundEventProvider>();
			_departmentLinksService = WebBootstrapper.GetKernel().Resolve<IDepartmentLinksService>();
			_inboundEventProvider.RegisterForEvents(PersonnelStatusUpdated, UnitStatusUpdated, CallsUpdated, PersonnelStaffingUpdated);
		}

		public void Connect(int departmentId)
		{
			Groups.Add(Context.ConnectionId, departmentId.ToString());

			Clients.Caller.onConnected(Context.ConnectionId);
		}

		public void SubscribeToDepartmentLink(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link != null && link.LinkEnabled)
				Groups.Add(Context.ConnectionId, link.DepartmentId.ToString());
		}

		public void UnsubscribeToDepartmentLink(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link != null)
				Groups.Remove(Context.ConnectionId, link.DepartmentId.ToString());
		}

		public void PersonnelStatusUpdated(int departmentId, int id)
		{
			Clients.Group(departmentId.ToString()).personnelStatusUpdated(id);
		}

		public void PersonnelStaffingUpdated(int departmentId, int id)
		{
			Clients.Group(departmentId.ToString()).personnelStaffingUpdated(id);
		}

		public void UnitStatusUpdated(int departmentId, int id)
		{
			Clients.Group(departmentId.ToString()).unitStatusUpdated(id);
		}

		public void CallsUpdated(int departmentId, int id)
		{
			Clients.Group(departmentId.ToString()).callsUpdated(id);
		}
	}
}
