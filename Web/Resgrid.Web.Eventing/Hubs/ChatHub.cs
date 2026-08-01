using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Config;
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

		private const string UserIdContextKey = "chatUserId";
		private const string DepartmentIdContextKey = "chatDepartmentId";

		/// <summary>userId -> connectionIds on this host; used by the Worker to evict revoked users from channel groups.</summary>
		public static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> UserConnections =
			new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

		private static readonly ConcurrentDictionary<string, DateTime> LastTypingTimestamps =
			new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

		public ChatHub(IChatChannelService chatChannelService, IChatPermissionService chatPermissionService,
			IChatMessageService chatMessageService, IChatPresenceService chatPresenceService)
		{
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_chatMessageService = chatMessageService;
			_chatPresenceService = chatPresenceService;
		}

		public override async Task OnConnectedAsync()
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			if (departmentId > 0 && !string.IsNullOrWhiteSpace(userId))
			{
				Context.Items[DepartmentIdContextKey] = departmentId;
				Context.Items[UserIdContextKey] = userId;

				UserConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>())[Context.ConnectionId] = 0;
			}

			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception exception)
		{
			var userId = Context.Items.TryGetValue(UserIdContextKey, out var userIdValue) ? userIdValue as string : null;
			var departmentId = Context.Items.TryGetValue(DepartmentIdContextKey, out var departmentIdValue) && departmentIdValue is int id ? id : 0;

			if (!string.IsNullOrWhiteSpace(userId) && UserConnections.TryGetValue(userId, out var connections))
			{
				connections.TryRemove(Context.ConnectionId, out _);

				if (connections.IsEmpty && UserConnections.TryRemove(userId, out _) && departmentId > 0)
					await Clients.Group($"chatdept:{departmentId}").SendAsync("chatPresenceChanged", userId, false);
			}

			await base.OnDisconnectedAsync(exception);
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
			await ResolveAccessibleChannelOrThrowAsync(channelId, asUnitId);

			await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{channelId}");

			await Clients.Caller.SendAsync("onChatChannelJoined", channelId);
		}

		public async Task LeaveChannel(string channelId)
		{
			await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{channelId}");
		}

		public async Task Typing(string channelId, string displayName = null, bool isTyping = true, int? asUnitId = null)
		{
			var access = await ResolveAccessibleChannelAsync(channelId, asUnitId);
			if (access == null)
				return;

			var userId = access.Value.UserId;

			if (isTyping)
			{
				var now = DateTime.UtcNow;
				var throttleKey = $"{userId}:{channelId}";

				if (LastTypingTimestamps.TryGetValue(throttleKey, out var lastTyping) &&
					(now - lastTyping).TotalMilliseconds < ChatConfig.TypingThrottleMs)
					return;

				LastTypingTimestamps[throttleKey] = now;
			}

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
			var access = await ResolveAccessibleChannelAsync(channelId, asUnitId);
			if (access == null)
				return;

			await _chatMessageService.MarkReadAsync(channelId, access.Value.Channel.DepartmentId, access.Value.UserId, asUnitId, seq);
		}

		public async Task MarkDelivered(string channelId, long seq, int? asUnitId = null)
		{
			var access = await ResolveAccessibleChannelAsync(channelId, asUnitId);
			if (access == null)
				return;

			await _chatMessageService.MarkDeliveredAsync(channelId, access.Value.Channel.DepartmentId, access.Value.UserId, asUnitId, seq);
		}

		/// <summary>
		/// Resolves the channel for a hub operation when the caller may access it: validates the caller's
		/// claims (before any query — an authenticated connection always has them, so their absence means
		/// malformed/forged claims), that the channel exists in the caller's department, and that the
		/// caller (optionally acting as a unit) can access it. Returns null on any failure — for the
		/// fire-and-forget signal methods (typing/receipts). Access checks are cached, keeping hot paths cheap.
		/// </summary>
		private async Task<(ChatChannel Channel, string UserId)?> ResolveAccessibleChannelAsync(string channelId, int? asUnitId)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();

			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return null;

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != departmentId)
				return null;

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, userId, asUnitId))
				return null;

			return (channel, userId);
		}

		/// <summary>
		/// Throwing variant of <see cref="ResolveAccessibleChannelAsync"/> for methods that surface an
		/// error to the caller (JoinChannel). Uses a single message for not-found and unauthorized so the
		/// channel's existence can't be enumerated over the hub.
		/// </summary>
		private async Task<ChatChannel> ResolveAccessibleChannelOrThrowAsync(string channelId, int? asUnitId)
		{
			var access = await ResolveAccessibleChannelAsync(channelId, asUnitId);
			if (access == null)
				throw new HubException("Not authorized for this channel.");

			return access.Value.Channel;
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
