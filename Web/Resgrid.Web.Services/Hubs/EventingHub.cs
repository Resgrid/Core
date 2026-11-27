using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Validation.AspNetCore;
using Resgrid.Model;
using Resgrid.Model.Helpers;
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

			if (ConnectionClosed)
				return;

			await Groups.AddToGroupAsync(Context.ConnectionId, departmentId.ToString());
			await Clients.Caller.SendAsync("onConnected", Context.ConnectionId);
		}

		/// <summary>
		/// Joins the per-kind share groups of the other department for the kinds it shares toward the caller over an
		/// enabled link — never the other department's own group, which carries all of its traffic. The groups carry the
		/// other department's access epoch, so disabling, deleting or narrowing the link later strands this subscription.
		/// </summary>
		public async Task SubscribeToDepartmentLink(int linkId)
		{
			var shareGroups = await _departmentLinksService.GetShareGroupsForSubscriberAsync(linkId, GetDepartmentId());
			if (shareGroups == null)
				throw new HubException("Department link updates are temporarily unavailable.");
			if (shareGroups.Count == 0)
				throw new HubException("Not authorized for this department link.");

			if (ConnectionClosed)
				return;

			foreach (var shareGroup in shareGroups)
				await Groups.AddToGroupAsync(Context.ConnectionId, shareGroup);

			DepartmentLinkSharing.RecordSubscription(Context.Items, linkId, shareGroups);
		}

		/// <summary>
		/// Leaves the groups this connection joined for the link. Nothing is looked up: the link may be gone and the
		/// epoch rotated since, and a connection that never subscribed has nothing to leave.
		/// </summary>
		public async Task UnsubscribeToDepartmentLink(int linkId)
		{
			if (ConnectionClosed)
				return;

			foreach (var shareGroup in DepartmentLinkSharing.TakeSubscription(Context.Items, linkId))
				await Groups.RemoveFromGroupAsync(Context.ConnectionId, shareGroup);
		}

		/// <summary>
		/// True when the connection closed while this call was running (SignalR does not wait for in-flight
		/// calls before disconnecting). No server holds the connection any more, so the Redis backplane would
		/// wait 30 seconds for a group ack that never comes and then throw.
		/// </summary>
		private bool ConnectionClosed => Context.ConnectionAborted.IsCancellationRequested;

		public Task PersonnelStatusUpdated(int departmentId, int id) =>
			PublishAsync("personnelStatusUpdated", departmentId, id, DepartmentLinkShareTypes.Personnel);

		public Task PersonnelStaffingUpdated(int departmentId, int id) =>
			PublishAsync("personnelStaffingUpdated", departmentId, id, DepartmentLinkShareTypes.Personnel);

		public Task UnitStatusUpdated(int departmentId, int id) =>
			PublishAsync("unitStatusUpdated", departmentId, id, DepartmentLinkShareTypes.Units);

		public Task CallsUpdated(int departmentId, int id) =>
			PublishAsync("callsUpdated", departmentId, id, DepartmentLinkShareTypes.Calls);

		private async Task PublishAsync(string method, int departmentId, int id, DepartmentLinkShareTypes shareType)
		{
			DemandInternalPublisher();
			await Clients.Group(departmentId.ToString()).SendAsync(method, id);

			// The current epoch's group only; null when no epoch exists (nobody subscribed) or the cache is unavailable.
			var shareGroup = await _departmentLinksService.GetShareGroupNameAsync(departmentId, shareType);
			if (shareGroup != null)
				await Clients.Group(shareGroup).SendAsync(method, id);
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
	}
}
