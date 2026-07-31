using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Eventing.Hubs
{
	/// <summary>
	/// Realtime chat hub. Carries only ephemeral traffic (channel group membership, typing, presence,
	/// read/delivered pointers) — message writes go through the REST API and fan back out via the
	/// RabbitMQ eventing topic and this host's Worker. Group naming: chat:{channelId} per channel,
	/// chatuser:{deptId}:{userId} for personal events, chatdept:{deptId} for channel-list updates.
	/// </summary>
	[Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
	public class ChatHub : Hub
	{
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IChatMessageService _chatMessageService;
		private readonly IChatPresenceService _chatPresenceService;

		public ChatHub(IChatChannelService chatChannelService, IChatPermissionService chatPermissionService,
			IChatMessageService chatMessageService, IChatPresenceService chatPresenceService)
		{
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_chatMessageService = chatMessageService;
			_chatPresenceService = chatPresenceService;
		}

		public async Task Connect()
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return;

			await Groups.AddToGroupAsync(Context.ConnectionId, $"chatuser:{departmentId}:{userId.ToLowerInvariant()}");
			await Groups.AddToGroupAsync(Context.ConnectionId, $"chatdept:{departmentId}");

			var cameOnline = await _chatPresenceService.SetOnlineAsync(departmentId, userId);
			if (cameOnline)
				await Clients.Group($"chatdept:{departmentId}").SendAsync("chatPresenceChanged", userId, true);

			await Clients.Caller.SendAsync("onChatConnected", Context.ConnectionId);
		}

		public async Task JoinChannel(string channelId, int? asUnitId = null)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != departmentId)
				throw new HubException("Channel not found.");

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, userId, asUnitId))
				throw new HubException("Not authorized for this channel.");

			await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{channelId}");

			await Clients.Caller.SendAsync("onChatChannelJoined", channelId);
		}

		public async Task LeaveChannel(string channelId)
		{
			await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{channelId}");
		}

		public async Task Typing(string channelId, bool isTyping, int? asUnitId = null, string displayName = null)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != departmentId)
				return;

			// Cached access check keeps this hot path cheap.
			if (!await _chatPermissionService.CanAccessChannelAsync(channel, userId, asUnitId))
				return;

			await Clients.OthersInGroup($"chat:{channelId}").SendAsync("chatTyping", new
			{
				ChannelId = channelId,
				UserId = userId,
				UnitId = asUnitId,
				DisplayName = displayName,
				IsTyping = isTyping
			});
		}

		public async Task MarkRead(string channelId, long seq, int? asUnitId = null)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != departmentId)
				return;

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, userId, asUnitId))
				return;

			await _chatMessageService.MarkReadAsync(channelId, departmentId, userId, asUnitId, seq);
		}

		public async Task MarkDelivered(string channelId, long seq, int? asUnitId = null)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != departmentId)
				return;

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, userId, asUnitId))
				return;

			await _chatMessageService.MarkDeliveredAsync(channelId, departmentId, userId, asUnitId, seq);
		}

		public async Task Heartbeat()
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			if (departmentId > 0 && !string.IsNullOrWhiteSpace(userId))
				await _chatPresenceService.TouchAsync(departmentId, userId);
		}
	}
}
