using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Validation.AspNetCore;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Hubs
{
	public interface IEventingHub
	{
		Task Connect(int departmentId);
		Task SubscribeToDepartmentLink(int linkId);
		Task UnsubscribeToDepartmentLink(int linkId);
	}

	[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
	public class EventingHub : Hub
	{
		private readonly IDepartmentLinksService _departmentLinksService;

		public EventingHub(IDepartmentLinksService departmentLinksService)
		{
			_departmentLinksService = departmentLinksService;
		}

		public async Task Connect(int departmentId)
		{
			var authenticatedDepartmentId = GetDepartmentId();
			if (authenticatedDepartmentId <= 0 || authenticatedDepartmentId != departmentId)
				throw new HubException("Not authorized for this department.");

			await Groups.AddToGroupAsync(Context.ConnectionId, departmentId.ToString());
			await Clients.Caller.SendAsync("onConnected", Context.ConnectionId);
		}

		public async Task SubscribeToDepartmentLink(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);
			var linkedDepartmentId = GetLinkedDepartmentForCaller(link);
			if (link == null || !link.LinkEnabled || !linkedDepartmentId.HasValue)
				throw new HubException("Not authorized for this department link.");

			await Groups.AddToGroupAsync(Context.ConnectionId, linkedDepartmentId.Value.ToString());
		}

		public async Task UnsubscribeToDepartmentLink(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);
			var linkedDepartmentId = GetLinkedDepartmentForCaller(link);
			if (linkedDepartmentId.HasValue)
				await Groups.RemoveFromGroupAsync(Context.ConnectionId, linkedDepartmentId.Value.ToString());
		}

		public Task PersonnelStatusUpdated(int departmentId, int id) =>
			PublishAsync("personnelStatusUpdated", departmentId, id);

		public Task PersonnelStaffingUpdated(int departmentId, int id) =>
			PublishAsync("personnelStaffingUpdated", departmentId, id);

		public Task UnitStatusUpdated(int departmentId, int id) =>
			PublishAsync("unitStatusUpdated", departmentId, id);

		public Task CallsUpdated(int departmentId, int id) =>
			PublishAsync("callsUpdated", departmentId, id);

		private Task PublishAsync(string method, int departmentId, int id)
		{
			DemandInternalPublisher();
			return Clients.Group(departmentId.ToString()).SendAsync(method, id);
		}

		private void DemandInternalPublisher()
		{
			var subject = Context.User?.FindFirst("sub")?.Value ??
				Context.User?.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (!string.Equals(subject, "system_eventing", StringComparison.Ordinal))
				throw new HubException("This operation is reserved for the eventing publisher.");
		}

		private int GetDepartmentId()
		{
			var claim = Context.User?.FindFirst(ClaimTypes.PrimaryGroupSid);
			return claim != null && int.TryParse(claim.Value, out var departmentId) ? departmentId : 0;
		}

		private int? GetLinkedDepartmentForCaller(Resgrid.Model.DepartmentLink link)
		{
			if (link == null)
				return null;
			var departmentId = GetDepartmentId();
			if (link.DepartmentId == departmentId)
				return link.LinkedDepartmentId;
			if (link.LinkedDepartmentId == departmentId)
				return link.DepartmentId;
			return null;
		}
	}
}
