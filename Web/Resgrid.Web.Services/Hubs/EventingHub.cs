using System.Threading.Tasks;
using CommonServiceLocator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Hubs
{
	public interface IEventingHub
	{
		Task Connect(int departmentId);

		Task SubscribeToDepartmentLink(int linkId);

		Task UnsubscribeToDepartmentLink(int linkId);

		Task PersonnelStatusUpdated(int departmentId, int id);

		Task PersonnelStaffingUpdated(int departmentId, int id);

		Task UnitStatusUpdated(int departmentId, int id);

		Task CallsUpdated(int departmentId, int id);

		Task DepartmentUpdated(int departmentId);
	}

	[AllowAnonymous]
	public class EventingHub : Hub
	{
		private readonly IDepartmentLinksService _departmentLinksService;

		public EventingHub()
		{
			_departmentLinksService = ServiceLocator.Current.GetInstance<IDepartmentLinksService>();
		}

		public async Task Connect(int departmentId)
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, departmentId.ToString());

			await Clients.Caller.SendAsync("onConnected", Context.ConnectionId);
		}

		public async Task SubscribeToDepartmentLink(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link != null && link.LinkEnabled)
				await Groups.AddToGroupAsync(Context.ConnectionId, link.DepartmentId.ToString());
		}

		public async Task UnsubscribeToDepartmentLink(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link != null)
				await Groups.RemoveFromGroupAsync(Context.ConnectionId, link.DepartmentId.ToString());
		}

		public async Task PersonnelStatusUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStatusUpdated", id);
		}

		public async Task PersonnelStaffingUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStaffingUpdated", id);
		}

		public async Task UnitStatusUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("unitStatusUpdated", id);
		}

		public async Task CallsUpdated(int departmentId, int id)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callsUpdated", id);
		}

		public async Task DepartmentUpdated(int departmentId)
		{
			var group = Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("departmentUpdated");
		}
	}
}
