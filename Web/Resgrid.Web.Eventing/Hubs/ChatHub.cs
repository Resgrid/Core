using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Eventing.Hubs
{
	/// <summary>
	/// Realtime chat hub. Carries only ephemeral traffic (channel group membership, typing, presence,
	/// read/delivered pointers) — message writes go through the REST API and fan back out via the
	/// RabbitMQ eventing topic and this host's Worker. Group naming: chat:{channelId}:{accessVersion}
	/// per channel (the version rotates on authorization changes),
	/// chatuser:{deptId}:{userId} for personal events, chatdept:{deptId} for channel-list updates.
	/// </summary>
	[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = ResgridResources.Messages_View)]
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

		// Typing timestamps are only useful for the brief throttle window; anything older is dead weight.
		// Sweep opportunistically (single sweeper per interval) so the dictionary can't grow unbounded as
		// users disconnect or channels are archived.
		private static readonly TimeSpan TypingCleanupInterval = TimeSpan.FromMinutes(5);
		private static long _lastTypingCleanupTicks = DateTime.MinValue.Ticks;

		public ChatHub(IChatChannelService chatChannelService, IChatPermissionService chatPermissionService,
			IChatMessageService chatMessageService, IChatPresenceService chatPresenceService)
		{
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_chatMessageService = chatMessageService;
			_chatPresenceService = chatPresenceService;
		}

		// ClaimsAuthorizationHelper reads IHttpContextAccessor.HttpContext, which is not flowed into
		// hub invocations on every transport — it comes back null and NREs. HubCallerContext.User is
		// the connection's authenticated principal and is the supported claim source inside a hub.
		private int GetDepartmentId()
		{
			var claim = Context.User?.FindFirst(ClaimTypes.PrimaryGroupSid);

			return claim != null && int.TryParse(claim.Value, out var departmentId) ? departmentId : 0;
		}

		private string GetUserId()
		{
			return Context.User?.FindFirst(ClaimTypes.PrimarySid)?.Value ?? String.Empty;
		}

		public override async Task OnConnectedAsync()
		{
			var departmentId = GetDepartmentId();
			var userId = GetUserId();

			if (departmentId > 0 && !string.IsNullOrWhiteSpace(userId) &&
				await _chatPermissionService.IsActiveDepartmentUserAsync(departmentId, userId))
			{
				Context.Items[DepartmentIdContextKey] = departmentId;
				Context.Items[UserIdContextKey] = userId;

				AddUserConnection(userId, Context.ConnectionId);
			}
			else
			{
				Context.Abort();
			}

			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception exception)
		{
			var userId = Context.Items.TryGetValue(UserIdContextKey, out var userIdValue) ? userIdValue as string : null;
			var departmentId = Context.Items.TryGetValue(DepartmentIdContextKey, out var departmentIdValue) && departmentIdValue is int id ? id : 0;

			if (!string.IsNullOrWhiteSpace(userId) && RemoveUserConnection(userId, Context.ConnectionId) && departmentId > 0)
			{
				try
				{
					await Clients.Group($"chatdept:{departmentId}").SendAsync("chatPresenceChanged", userId, false);
				}
				catch (Exception ex)
				{
					// Best-effort presence broadcast: the connection is already removed, so a transport
					// failure here must not abort the disconnect flow. Log with context and continue.
					Resgrid.Framework.Logging.LogException(ex, $"ChatHub presence-offline broadcast failed for user {userId} in department {departmentId}.");
				}
			}

			await base.OnDisconnectedAsync(exception);
		}

		// Add/Remove serialize on the per-user connection set so the "set is empty -> drop it from the map"
		// transition can't race a concurrent add. Without this, an add that fetched the same set via GetOrAdd
		// just before the set was removed from the map would orphan its connection, defeating server-side
		// eviction on access revocation.
		private static void AddUserConnection(string userId, string connectionId)
		{
			while (true)
			{
				var set = UserConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
				lock (set)
				{
					// The set may have been removed from the map by a concurrent disconnect after GetOrAdd
					// returned it; only add when it is still the live set for this user, else retry.
					if (UserConnections.TryGetValue(userId, out var current) && ReferenceEquals(current, set))
					{
						set[connectionId] = 0;
						return;
					}
				}
			}
		}

		/// <summary>Removes a connection; returns true only when it was the user's last (presence went offline).</summary>
		private static bool RemoveUserConnection(string userId, string connectionId)
		{
			if (!UserConnections.TryGetValue(userId, out var set))
				return false;

			lock (set)
			{
				set.TryRemove(connectionId, out _);

				if (set.IsEmpty)
				{
					UserConnections.TryRemove(userId, out _);
					return true;
				}
			}

			return false;
		}

		public async Task Connect()
		{
			var departmentId = GetDepartmentId();
			var userId = GetUserId();

			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) ||
				!await _chatPermissionService.IsActiveDepartmentUserAsync(departmentId, userId))
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
			var channel = await ResolveAccessibleChannelOrThrowAsync(channelId, asUnitId);
			var groupName = await GetCurrentChannelGroupNameAsync(channel.ChatChannelId);
			if (groupName == null)
				throw new HubException("Chat authorization is temporarily unavailable.");

			await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

			await Clients.Caller.SendAsync("onChatChannelJoined", channelId);
		}

		public async Task LeaveChannel(string channelId)
		{
			var groupName = await GetCurrentChannelGroupNameAsync(channelId);
			if (groupName != null)
				await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
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
				PruneStaleTypingTimestamps(now);

				var throttleKey = $"{userId}:{channelId}";

				if (LastTypingTimestamps.TryGetValue(throttleKey, out var lastTyping) &&
					(now - lastTyping).TotalMilliseconds < ChatConfig.TypingThrottleMs)
					return;

				LastTypingTimestamps[throttleKey] = now;
			}

			var groupName = await GetCurrentChannelGroupNameAsync(channelId);
			if (groupName == null)
				return;

			await Clients.OthersInGroup(groupName).SendAsync("chatTyping", new
			{
				ChannelId = channelId,
				UserId = userId,
				UnitId = asUnitId,
				DisplayName = displayName,
				IsTyping = isTyping
			});
		}

		// Evicts typing timestamps older than one cleanup interval. Interlocked guards ensure a single
		// thread sweeps per interval; the value-checked TryRemove never drops an entry refreshed mid-sweep.
		private static void PruneStaleTypingTimestamps(DateTime now)
		{
			var last = Interlocked.Read(ref _lastTypingCleanupTicks);
			if (now.Ticks - last < TypingCleanupInterval.Ticks)
				return;

			if (Interlocked.CompareExchange(ref _lastTypingCleanupTicks, now.Ticks, last) != last)
				return;

			var cutoff = now - TypingCleanupInterval;
			foreach (var entry in LastTypingTimestamps)
			{
				if (entry.Value < cutoff)
					LastTypingTimestamps.TryRemove(entry);
			}
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
			var departmentId = GetDepartmentId();
			var userId = GetUserId();

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

		public static string BuildChannelGroupName(string channelId, string accessVersion)
		{
			return string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(accessVersion)
				? null
				: $"chat:{channelId}:{accessVersion}";
		}

		private async Task<string> GetCurrentChannelGroupNameAsync(string channelId)
		{
			return BuildChannelGroupName(channelId,
				await _chatPermissionService.GetChannelAccessVersionAsync(channelId));
		}

		public async Task Heartbeat()
		{
			var departmentId = GetDepartmentId();
			var userId = GetUserId();

			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) ||
				!await _chatPermissionService.IsActiveDepartmentUserAsync(departmentId, userId))
			{
				Context.Abort();
				return;
			}

			await _chatPresenceService.TouchAsync(departmentId, userId);
		}

		/// <summary>
		/// Marks the channel the caller is actively viewing (null/empty clears it). Push notifications for
		/// a channel are suppressed only for viewers active in that channel — merely being online no longer
		/// suppresses them. Clients call this on conversation open/close and on app foreground/background.
		/// </summary>
		public async Task SetActiveChannel(string channelId, int? asUnitId = null)
		{
			var departmentId = GetDepartmentId();
			var userId = GetUserId();

			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return;

			if (string.IsNullOrWhiteSpace(channelId))
			{
				await _chatPresenceService.ClearActiveChannelAsync(departmentId, userId);
				return;
			}

			// Access-check before recording, so a forged channelId can't suppress someone else's pushes.
			var access = await ResolveAccessibleChannelAsync(channelId, asUnitId);
			if (access == null)
				return;

			await _chatPresenceService.SetActiveChannelAsync(departmentId, userId, channelId, asUnitId);
		}
	}
}
