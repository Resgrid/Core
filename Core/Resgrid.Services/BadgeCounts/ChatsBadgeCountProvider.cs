using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Unread chat messages across the channels v4 Chat/GetChannels lists (with the active unit's when the member crews
	/// it), each channel's sequence read fresh. Nothing when chat is off for the department or beyond the member's plan.
	/// </summary>
	public class ChatsBadgeCountProvider : IBadgeCountProvider
	{
		/// <summary>The v4 chat controller's availability check uses this key and lifetime too, so they share one entry.</summary>
		private static readonly TimeSpan ChatEnabledCacheLength = TimeSpan.FromSeconds(30);

		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly ICacheProvider _cacheProvider;

		public ChatsBadgeCountProvider(IChatChannelService chatChannelService, IChatPermissionService chatPermissionService, IAuthorizationService authorizationService,
			IFeatureToggleService featureToggleService, ICacheProvider cacheProvider)
		{
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_authorizationService = authorizationService;
			_featureToggleService = featureToggleService;
			_cacheProvider = cacheProvider;
		}

		public string Key => BadgeCountKeys.Chats;

		public string RequiredPolicy => "Chat_View";

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			var departmentId = context.DepartmentId;
			var userId = context.UserId;

			if (!await IsChatEnabledAsync(departmentId, userId))
				return null;

			// The active unit is caller-supplied: count its channels only when the user crews it, as the channel list does.
			var activeUnitId = context.ActiveUnitId;
			if (activeUnitId.HasValue && (activeUnitId.Value <= 0 || !await _chatPermissionService.CanSendAsUnitAsync(userId, activeUnitId.Value, departmentId)))
				activeUnitId = null;

			var total = await _chatChannelService.GetUnreadMessageCountForUserAsync(departmentId, userId, activeUnitId);

			return new BadgeCount(Key, (int)Math.Min(int.MaxValue, Math.Max(0, total)));
		}

		/// <summary>Chat answers only for members within plan limits in a department with Chat.System on (v4 ChatController).</summary>
		private async Task<bool> IsChatEnabledAsync(int departmentId, string userId)
		{
			async Task<bool> isChatEnabled()
			{
				return await _authorizationService.IsUserValidWithinLimitsAsync(userId, departmentId) &&
					await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, departmentId);
			}

			if (_cacheProvider == null || !Config.SystemBehaviorConfig.CacheEnabled)
				return await isChatEnabled();

			return await _cacheProvider.RetrieveAsync($"chat:enabled:{departmentId}:{userId.ToLowerInvariant()}", isChatEnabled, ChatEnabledCacheLength);
		}
	}
}
