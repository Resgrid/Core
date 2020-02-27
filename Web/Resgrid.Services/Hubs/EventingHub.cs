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
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				group.personnelStatusUpdated(id);
		}

		public void PersonnelStaffingUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				group.personnelStaffingUpdated(id);
		}

		public void UnitStatusUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				group.unitStatusUpdated(id);
		}

		public void CallsUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				group.callsUpdated(id);
		}

		public void DepartmentUpdated(int departmentId)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				group.departmentUpdated();
		}
	}
}
