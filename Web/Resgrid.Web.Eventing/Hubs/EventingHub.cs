using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Validation.AspNetCore;
using Resgrid.Model.Services;

namespace Resgrid.Web.Eventing.Hubs
{
	public interface IEventingHub
	{
		Task Connect(int departmentId);
		Task SubscribeToDepartmentLink(int linkId);
		Task UnsubscribeToDepartmentLink(int linkId);
		Task SubscribeToCall(int callId);
		Task UnsubscribeToCall(int callId);
	}

	/// <summary>
	/// Authenticated subscription-only hub for general department events. Events are published
	/// by the server-side worker through IHubContext; callers cannot manufacture broadcasts.
	/// </summary>
	[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
	public class EventingHub : Hub
	{
		private readonly IDepartmentLinksService _departmentLinksService;
		private readonly ICallsService _callsService;

		public EventingHub(IDepartmentLinksService departmentLinksService, ICallsService callsService)
		{
			_departmentLinksService = departmentLinksService;
			_callsService = callsService;
		}

		private int GetDepartmentId()
		{
			var claim = Context.User?.FindFirst(ClaimTypes.PrimaryGroupSid);
			return claim != null && int.TryParse(claim.Value, out var departmentId) ? departmentId : 0;
		}

		public async Task Connect(int departmentId)
		{
			var authenticatedDepartmentId = GetDepartmentId();
			if (authenticatedDepartmentId <= 0 || departmentId != authenticatedDepartmentId)
				throw new HubException("Not authorized for this department.");

			await Groups.AddToGroupAsync(Context.ConnectionId, authenticatedDepartmentId.ToString());
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

		public async Task SubscribeToCall(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != GetDepartmentId())
				throw new HubException("Not authorized for this call.");

			await Groups.AddToGroupAsync(Context.ConnectionId, GetCallGroupName(callId));
		}

		public Task UnsubscribeToCall(int callId) =>
			Groups.RemoveFromGroupAsync(Context.ConnectionId, GetCallGroupName(callId));

		/// <summary>
		/// Single source of truth for the per-call group name, so a publisher added later cannot drift from
		/// what subscribers actually joined.
		/// </summary>
		public static string GetCallGroupName(int callId) => $"CallUpdated:{callId}";

		public Task PersonnelStatusUpdated(int departmentId, int id) =>
			PublishAsync("PersonnelStatusUpdated", departmentId, id);

		public Task PersonnelStaffingUpdated(int departmentId, int id) =>
			PublishAsync("PersonnelStaffingUpdated", departmentId, id);

		public Task UnitStatusUpdated(int departmentId, int id) =>
			PublishAsync("UnitStatusUpdated", departmentId, id);

		public Task CallsUpdated(int departmentId, int id) =>
			PublishAsync("CallsUpdated", departmentId, id);

		private Task PublishAsync(string method, int departmentId, int id)
		{
			DemandInternalPublisher();
			return Clients.Group(departmentId.ToString()).SendAsync(method, id);
		}

		private void DemandInternalPublisher()
		{
			var subject = Context.User?.FindFirst("sub")?.Value ??
				Context.User?.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (!string.Equals(subject, "system_eventing", System.StringComparison.Ordinal))
				throw new HubException("This operation is reserved for the eventing publisher.");
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
