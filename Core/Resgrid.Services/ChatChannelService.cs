using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Channel lifecycle and idempotent provisioning. Every Ensure* method is safe to call repeatedly:
	/// lookups go through the unique keys the migrations create, and a losing racer re-reads the winner.
	/// </summary>
	public class ChatChannelService : IChatChannelService
	{
		private static readonly TimeSpan ChannelListCacheLength = TimeSpan.FromSeconds(45);

		/// <summary>How long a completed incident-channel backfill suppresses the next sweep for that command.</summary>
		private static readonly TimeSpan IncidentBackfillCacheLength = TimeSpan.FromMinutes(30);

		private readonly IChatChannelRepository _chatChannelRepository;
		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IChatChannelAccessRuleRepository _chatChannelAccessRuleRepository;
		private readonly IChatDepartmentSettingRepository _chatDepartmentSettingRepository;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICallsService _callsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;
		private readonly IUnitOfWork _unitOfWork;

		public ChatChannelService(IChatChannelRepository chatChannelRepository, IChatChannelMemberRepository chatChannelMemberRepository,
			IChatChannelAccessRuleRepository chatChannelAccessRuleRepository, IChatDepartmentSettingRepository chatDepartmentSettingRepository,
			IChatPermissionService chatPermissionService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IUnitsService unitsService, IUserProfileService userProfileService, ICallsService callsService, IEventAggregator eventAggregator,
			ICacheProvider cacheProvider, IUnitOfWork unitOfWork)
		{
			_chatChannelRepository = chatChannelRepository;
			_chatChannelMemberRepository = chatChannelMemberRepository;
			_chatChannelAccessRuleRepository = chatChannelAccessRuleRepository;
			_chatDepartmentSettingRepository = chatDepartmentSettingRepository;
			_chatPermissionService = chatPermissionService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_userProfileService = userProfileService;
			_callsService = callsService;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
			_unitOfWork = unitOfWork;
		}

		public async Task<ChatChannel> GetChannelByIdAsync(string chatChannelId)
		{
			return await _chatChannelRepository.GetByIdAsync(chatChannelId);
		}

		public async Task<List<ChatChannel>> GetChannelsByIdsAsync(IEnumerable<string> chatChannelIds)
		{
			var ids = chatChannelIds?
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (ids == null || ids.Count == 0)
				return new List<ChatChannel>();

			var channels = await _chatChannelRepository.GetByIdsAsync(ids);
			return channels?.ToList() ?? new List<ChatChannel>();
		}

		public async Task<List<ChatChannel>> GetChannelsForUserAsync(int departmentId, string userId, int? activeUnitId, bool includeArchived = false)
		{
			async Task<List<ChatChannel>> getChannels()
			{
				var results = new Dictionary<string, ChatChannel>(StringComparer.OrdinalIgnoreCase);

				var departmentChannel = await EnsureDepartmentChannelAsync(departmentId);
				if (departmentChannel != null)
					results[departmentChannel.ChatChannelId] = departmentChannel;

				// Loaded once: source for the admin group-channel matching here AND the
				// implicit-audience pass further down.
				var allChannels = await _chatChannelRepository.GetAllByDepartmentIdAsync(departmentId, includeArchived);

				// Department admins get every group's default channel; everyone else gets only the group
				// they belong to. Existing channels come from the bulk load above — only groups with no
				// channel yet hit the provisioning path, and a failure there is contained per group so one
				// bad group can never blank the admin's whole channel list.
				if (await _chatPermissionService.IsDepartmentAdminAsync(departmentId, userId))
				{
					var allGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId);
					if (allGroups != null && allGroups.Count > 0)
					{
						var groupChannelsByGroupId = allChannels?
							.Where(c => c.ChannelType == (int)ChatChannelType.GroupDefault && c.GroupId.HasValue)
							.GroupBy(c => c.GroupId.Value)
							.ToDictionary(g => g.Key, g => g.First());

						foreach (var departmentGroup in allGroups)
						{
							try
							{
								ChatChannel groupChannel;
								if (groupChannelsByGroupId == null || !groupChannelsByGroupId.TryGetValue(departmentGroup.DepartmentGroupId, out groupChannel))
									groupChannel = await EnsureGroupChannelAsync(departmentGroup);

								if (groupChannel != null)
									results[groupChannel.ChatChannelId] = groupChannel;
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
							}
						}
					}
				}
				else
				{
					var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
					if (group != null)
					{
						var groupChannel = await EnsureGroupChannelAsync(group);
						if (groupChannel != null)
							results[groupChannel.ChatChannelId] = groupChannel;
					}
				}

				// Chatbot channels are provisioned when a chatbot session starts — the list path only
				// surfaces an existing one, and only when the department has the chatbot enabled.
				var settings = await GetDepartmentSettingsAsync(departmentId);
				if (settings == null || settings.ChatbotEnabled)
				{
					var chatbotChannel = await _chatChannelRepository.GetChatbotChannelAsync(departmentId, userId);
					if (chatbotChannel != null)
						results[chatbotChannel.ChatChannelId] = chatbotChannel;
				}

				// Explicit memberships (DMs, ad-hoc groups, custom channel invites).
				var memberships = await _chatChannelMemberRepository.GetActiveByUserIdAsync(departmentId, userId);
				var membershipIds = memberships?.Select(m => m.ChatChannelId).Distinct().ToList();
				if (membershipIds != null && membershipIds.Count > 0)
				{
					var channels = await _chatChannelRepository.GetByIdsAsync(membershipIds);
					if (channels != null)
						foreach (var channel in channels)
							results[channel.ChatChannelId] = channel;
				}

				// Channels where the active unit is the participant (Dispatch/IC ↔ unit DMs, units
				// invited to groups) — the unit's operator must see them without a personal member row.
				// The caller-supplied unit only counts when the user actually crews it; otherwise any
				// department member could list another unit's private channels.
				if (activeUnitId.HasValue && await _chatPermissionService.CanSendAsUnitAsync(userId, activeUnitId.Value, departmentId))
				{
					// The unit's standing dispatch line is provisioned on the unit's first channel list
					// rather than at unit creation, so pre-existing units heal themselves. Best-effort:
					// the list must survive a provisioning failure.
					try
					{
						var unitDispatchChannel = await EnsureUnitDispatchChannelAsync(departmentId, activeUnitId.Value);
						if (unitDispatchChannel != null)
							results[unitDispatchChannel.ChatChannelId] = unitDispatchChannel;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}

					var unitMemberships = await _chatChannelMemberRepository.GetActiveByUnitIdAsync(departmentId, activeUnitId.Value);
					var unitChannelIds = unitMemberships?
						.Select(m => m.ChatChannelId)
						.Distinct()
						.Where(id => !results.ContainsKey(id))
						.ToList();
					if (unitChannelIds != null && unitChannelIds.Count > 0)
					{
						var channels = await _chatChannelRepository.GetByIdsAsync(unitChannelIds);
						if (channels != null)
							foreach (var channel in channels)
								results[channel.ChatChannelId] = channel;
					}
				}

				// Implicit-audience channels (custom rule-based, incident channels including the leads and
				// dispatch lines, and unit dispatch lines): evaluate access per channel; evaluations are
				// cached by the permission service.
				if (allChannels != null)
				{
					foreach (var channel in allChannels)
					{
						if (results.ContainsKey(channel.ChatChannelId))
							continue;

						var type = (ChatChannelType)channel.ChannelType;
						if (type != ChatChannelType.CustomLocked && type != ChatChannelType.Incident &&
							type != ChatChannelType.IncidentLane && type != ChatChannelType.IncidentCommand &&
							type != ChatChannelType.IncidentLeads && type != ChatChannelType.IncidentDispatch &&
							type != ChatChannelType.UnitDispatch)
							continue;

						if (await _chatPermissionService.CanAccessChannelAsync(channel, userId, activeUnitId))
							results[channel.ChatChannelId] = channel;
					}
				}

				return results.Values
					.Where(c => includeArchived || !c.IsArchived)
					.OrderByDescending(c => c.LastMessageOn ?? c.CreatedOn)
					.ToList();
			}

			if (!SystemBehaviorConfig.CacheEnabled)
				return await getChannels();

			// Brief per-user list cache; any channel mutation bumps the shared version (see
			// ChatPermissionService.InvalidateChannelCacheAsync) which rolls every list key forward.
			var version = await _cacheProvider.GetStringAsync(ChatPermissionService.ChannelListVersionCacheKey) ?? "0";
			var cacheKey = $"chatchannellist:{departmentId}:{userId?.ToLowerInvariant()}:{activeUnitId.GetValueOrDefault()}:{includeArchived}:{version}";

			return await _cacheProvider.RetrieveAsync(cacheKey, getChannels, ChannelListCacheLength);
		}

		public async Task<ChatChannel> GetOrCreateDirectMessageChannelAsync(int departmentId, string creatorUserId, string targetUserId, int? targetUnitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (string.IsNullOrWhiteSpace(targetUserId) && !targetUnitId.HasValue)
				return null;

			var dmKey = BuildDmKey(creatorUserId, targetUserId, targetUnitId);

			var existing = await _chatChannelRepository.GetByDmKeyAsync(departmentId, dmKey);
			if (existing != null)
				return existing;

			Unit targetUnit = null;
			if (targetUnitId.HasValue)
			{
				targetUnit = await _unitsService.GetUnitByIdAsync(targetUnitId.Value);
				if (targetUnit == null || targetUnit.DepartmentId != departmentId)
					throw new UnauthorizedAccessException("The target unit does not belong to this department.");
			}
			else if (!await _departmentsService.IsUserInDepartmentAsync(departmentId, targetUserId))
			{
				throw new UnauthorizedAccessException("The target user does not belong to this department.");
			}

			var channel = new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.DirectMessage,
				CreatedByUserId = creatorUserId,
				CreatedOn = DateTime.UtcNow,
				DmKey = dmKey
			};

			var members = new List<ChatChannelMember>
			{
				NewMemberRow(channel, ChatParticipantType.User, creatorUserId, null, null, creatorUserId)
			};

			if (targetUnitId.HasValue)
				members.Add(NewMemberRow(channel, ChatParticipantType.Unit, null, targetUnitId, targetUnit?.Name, creatorUserId));
			else
				members.Add(NewMemberRow(channel, ChatParticipantType.User, targetUserId, null, null, creatorUserId));

			ChatChannel saved;
			try
			{
				saved = await _chatChannelRepository.CreateDirectMessageChannelAsync(channel, members, cancellationToken);
			}
			catch (Exception)
			{
				// Unique (DepartmentId, DmKey) index backstops a true insert race; adopt the winner.
				var winner = await _chatChannelRepository.GetByDmKeyAsync(departmentId, dmKey);
				if (winner != null)
					return winner;

				throw;
			}

			if (saved == null)
				saved = await _chatChannelRepository.GetByDmKeyAsync(departmentId, dmKey);

			if (saved != null && string.Equals(saved.ChatChannelId, channel.ChatChannelId, StringComparison.OrdinalIgnoreCase))
			{
				// Roll the channel-list cache version so both participants see the new DM on their
				// next GetChannels instead of waiting out the 45s per-user list cache.
				await _chatPermissionService.InvalidateChannelCacheAsync(saved.ChatChannelId);
				PublishChannelEvent(saved, ChatEventKinds.ChannelProvisioned);
			}

			return saved;
		}

		public async Task<ChatChannel> CreateAdHocGroupChannelAsync(int departmentId, string creatorUserId, string name, List<string> memberUserIds, CancellationToken cancellationToken = default(CancellationToken))
		{
			// Validate all member memberships before any write, so an invalid member never leaves an
			// orphaned channel or partial member rows to roll back.
			var validatedMemberIds = memberUserIds == null
				? new List<string>()
				: memberUserIds.Where(m => !string.IsNullOrWhiteSpace(m) && !string.Equals(m, creatorUserId, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();

			// Batch-validate all memberships in a single query instead of one round trip per member.
			var membersInDepartment = await _departmentsService.GetMemberUserIdsInDepartmentAsync(departmentId, validatedMemberIds);
			if (validatedMemberIds.Any(id => !membersInDepartment.Contains(id)))
				throw new UnauthorizedAccessException("Every member must belong to this department.");

			var channel = new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.AdHocGroup,
				Name = name,
				CreatedByUserId = creatorUserId,
				CreatedOn = DateTime.UtcNow
			};

			await _chatChannelRepository.InsertAsync(channel, cancellationToken);

			await AddMemberRowAsync(channel, ChatParticipantType.User, creatorUserId, null, null, creatorUserId, cancellationToken, isModerator: true);

			foreach (var memberId in validatedMemberIds)
			{
				await AddMemberRowAsync(channel, ChatParticipantType.User, memberId, null, null, creatorUserId, cancellationToken);
			}

			await _chatPermissionService.InvalidateChannelCacheAsync(channel.ChatChannelId);
			PublishChannelEvent(channel, ChatEventKinds.ChannelProvisioned);

			return channel;
		}

		public async Task<ChatChannel> CreateCustomChannelAsync(int departmentId, string creatorUserId, string name, string topic, List<ChatChannelAccessRule> accessRules, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channel = new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.CustomLocked,
				Name = name,
				Topic = topic,
				CreatedByUserId = creatorUserId,
				CreatedOn = DateTime.UtcNow
			};

			await _chatChannelRepository.InsertAsync(channel, cancellationToken);

			await AddMemberRowAsync(channel, ChatParticipantType.User, creatorUserId, null, null, creatorUserId, cancellationToken, isModerator: true);

			if (accessRules != null)
			{
				foreach (var rule in accessRules)
				{
					rule.ChatChannelAccessRuleId = Guid.NewGuid().ToString();
					rule.ChatChannelId = channel.ChatChannelId;
					rule.DepartmentId = departmentId;
					rule.AddedByUserId = creatorUserId;
					rule.AddedOn = DateTime.UtcNow;

					await _chatChannelAccessRuleRepository.InsertAsync(rule, cancellationToken);
				}
			}

			await _chatPermissionService.InvalidateChannelCacheAsync(channel.ChatChannelId);
			PublishChannelEvent(channel, ChatEventKinds.ChannelProvisioned);

			return channel;
		}

		public async Task<ChatChannel> UpdateChannelAsync(string chatChannelId, string name, string topic, string byUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel == null)
				return null;

			// Targeted update: a full-row write here would rewind LastMessageSeq/LastMessageOn over the
			// atomic allocator's work.
			var modifiedOn = DateTime.UtcNow;
			await _chatChannelRepository.UpdateChannelInfoAsync(chatChannelId, name ?? channel.Name, topic, modifiedOn, cancellationToken);

			channel.Name = name ?? channel.Name;
			channel.Topic = topic;
			channel.ModifiedOn = modifiedOn;

			PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return channel;
		}

		public async Task<bool> SetChannelArchivedAsync(string chatChannelId, bool archived, string byUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel == null)
				return false;

			var archivedOn = archived ? DateTime.UtcNow : (DateTime?)null;
			await _chatChannelRepository.SetArchivedAsync(chatChannelId, archived, archivedOn, DateTime.UtcNow, cancellationToken);

			channel.IsArchived = archived;
			channel.ArchivedOn = archivedOn;
			channel.ModifiedOn = DateTime.UtcNow;

			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return true;
		}

		public async Task<List<ChatChannelMember>> GetMembersAsync(string chatChannelId)
		{
			var members = await _chatChannelMemberRepository.GetByChannelIdAsync(chatChannelId);
			return members?.ToList() ?? new List<ChatChannelMember>();
		}

		public async Task<List<ChatChannelMember>> GetActiveMembershipsForUserAsync(int departmentId, string userId)
		{
			var members = await _chatChannelMemberRepository.GetActiveByUserIdAsync(departmentId, userId);
			return members?.ToList() ?? new List<ChatChannelMember>();
		}

		public async Task<List<ChatChannelMember>> GetActiveMembershipsForUnitAsync(int departmentId, int unitId)
		{
			var members = await _chatChannelMemberRepository.GetActiveByUnitIdAsync(departmentId, unitId);
			return members?.ToList() ?? new List<ChatChannelMember>();
		}

		public async Task<List<ChatChannelMember>> GetActiveMembersForChannelsAsync(List<string> chatChannelIds)
		{
			if (chatChannelIds == null || chatChannelIds.Count == 0)
				return new List<ChatChannelMember>();

			var members = await _chatChannelMemberRepository.GetActiveByChannelIdsAsync(chatChannelIds);
			return members?.ToList() ?? new List<ChatChannelMember>();
		}

		public async Task<ChatChannelMember> GetUserMembershipAsync(string chatChannelId, string userId)
		{
			return await _chatChannelMemberRepository.GetUserMemberAsync(chatChannelId, userId);
		}

		public async Task<List<ChatChannelMember>> AddMembersAsync(string chatChannelId, List<string> userIds, string addedByUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel == null)
				return new List<ChatChannelMember>();

			if (channel.ChannelType == (int)ChatChannelType.DirectMessage)
				throw new InvalidOperationException("Direct message channels have a fixed membership.");

			if (channel.ChannelType == (int)ChatChannelType.CustomLocked &&
				!await _chatPermissionService.CanModerateChannelAsync(channel, addedByUserId))
				throw new UnauthorizedAccessException("Only channel moderators can add members to this channel.");

			var added = new List<ChatChannelMember>();

			if (userIds != null)
			{
				foreach (var userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
				{
					if (!await _departmentsService.IsUserInDepartmentAsync(channel.DepartmentId, userId))
						throw new UnauthorizedAccessException("Every member must belong to this department.");

					var existing = await _chatChannelMemberRepository.GetUserMemberAsync(chatChannelId, userId);
					if (existing != null)
					{
						if (existing.RemovedOn.HasValue)
						{
							// Targeted re-activation: a full-row write would rewind read/delivered pointers.
							await _chatChannelMemberRepository.SetMemberActiveAsync(existing.ChatChannelMemberId, true, cancellationToken);
							existing.RemovedOn = null;
							existing.JoinedOn = DateTime.UtcNow;
							existing.AddedByUserId = addedByUserId;
							existing.ModifiedOn = DateTime.UtcNow;
							added.Add(existing);
						}

						continue;
					}

					added.Add(await AddMemberRowAsync(channel, ChatParticipantType.User, userId, null, null, addedByUserId, cancellationToken));
				}
			}

			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			if (added.Count > 0)
				PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return added;
		}

		public async Task<bool> RemoveMemberAsync(string chatChannelId, string userId, string removedByUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var member = await _chatChannelMemberRepository.GetUserMemberAsync(chatChannelId, userId);
			if (member == null || member.RemovedOn.HasValue)
				return false;

			await _chatChannelMemberRepository.SetMemberActiveAsync(member.ChatChannelMemberId, false, cancellationToken);
			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel != null)
				PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return true;
		}

		public async Task<bool> ReplaceAccessRulesAsync(string chatChannelId, List<ChatChannelAccessRule> accessRules, string byUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel == null || channel.ChannelType != (int)ChatChannelType.CustomLocked)
				return false;

			// Open a shared connection/transaction so the delete and re-inserts commit atomically.
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _chatChannelAccessRuleRepository.DeleteByChannelIdAsync(chatChannelId, cancellationToken);

				if (accessRules != null)
				{
					foreach (var rule in accessRules)
					{
						rule.ChatChannelAccessRuleId = Guid.NewGuid().ToString();
						rule.ChatChannelId = chatChannelId;
						rule.DepartmentId = channel.DepartmentId;
						rule.AddedByUserId = byUserId;
						rule.AddedOn = DateTime.UtcNow;

						await _chatChannelAccessRuleRepository.InsertAsync(rule, cancellationToken);
					}
				}

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return true;
		}

		public async Task<ChatChannelMember> EnsureMemberStateAsync(string chatChannelId, int departmentId, string userId, int? unitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel == null)
				return null;

			// Invite-only channel types never self-grant membership: an existing row is reactivated,
			// a missing one is rejected. Implicit-audience types may lazily create the state row.
			var inviteOnly = channel.ChannelType == (int)ChatChannelType.DirectMessage ||
				channel.ChannelType == (int)ChatChannelType.AdHocGroup ||
				channel.ChannelType == (int)ChatChannelType.CustomLocked;

			if (unitId.HasValue)
			{
				var unitMember = await _chatChannelMemberRepository.GetUnitMemberAsync(chatChannelId, unitId.Value);
				if (unitMember != null)
				{
					if (inviteOnly && unitMember.RemovedOn.HasValue)
					{
						await _chatChannelMemberRepository.SetMemberActiveAsync(unitMember.ChatChannelMemberId, true, cancellationToken);
						unitMember.RemovedOn = null;
						unitMember.ModifiedOn = DateTime.UtcNow;
					}

					return unitMember;
				}

				if (inviteOnly)
					throw new UnauthorizedAccessException("Membership in this channel is by invitation only.");

				var unit = await _unitsService.GetUnitByIdAsync(unitId.Value);

				return await _chatChannelMemberRepository.InsertAsync(new ChatChannelMember
				{
					ChatChannelMemberId = Guid.NewGuid().ToString(),
					ChatChannelId = chatChannelId,
					DepartmentId = departmentId,
					ParticipantType = (int)ChatParticipantType.Unit,
					UnitId = unitId,
					DisplayNameOverride = unit?.Name,
					JoinedOn = DateTime.UtcNow
				}, cancellationToken);
			}

			var member = await _chatChannelMemberRepository.GetUserMemberAsync(chatChannelId, userId);
			if (member != null)
			{
				if (inviteOnly && member.RemovedOn.HasValue)
				{
					await _chatChannelMemberRepository.SetMemberActiveAsync(member.ChatChannelMemberId, true, cancellationToken);
					member.RemovedOn = null;
					member.ModifiedOn = DateTime.UtcNow;
				}

				return member;
			}

			if (inviteOnly)
				throw new UnauthorizedAccessException("Membership in this channel is by invitation only.");

			return await _chatChannelMemberRepository.InsertAsync(new ChatChannelMember
			{
				ChatChannelMemberId = Guid.NewGuid().ToString(),
				ChatChannelId = chatChannelId,
				DepartmentId = departmentId,
				ParticipantType = (int)ChatParticipantType.User,
				UserId = userId,
				JoinedOn = DateTime.UtcNow
			}, cancellationToken);
		}

		public async Task<bool> SetNotificationPreferenceAsync(string chatChannelId, int departmentId, string userId, ChatNotificationPreference preference, CancellationToken cancellationToken = default(CancellationToken))
		{
			var member = await EnsureMemberStateAsync(chatChannelId, departmentId, userId, null, cancellationToken);
			if (member == null)
				return false;

			return await _chatChannelMemberRepository.SetMemberNotificationPreferenceAsync(member.ChatChannelMemberId, (int)preference, cancellationToken);
		}

		public async Task<ChatChannel> EnsureDepartmentChannelAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var existing = await _chatChannelRepository.GetDepartmentDefaultAsync(departmentId);
			if (existing != null)
				return existing;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department == null)
				return null;

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.DepartmentDefault,
				Name = department.Name,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetDepartmentDefaultAsync(departmentId), cancellationToken);
		}

		public async Task<ChatChannel> EnsureGroupChannelAsync(DepartmentGroup group, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (group == null)
				return null;

			var existing = await _chatChannelRepository.GetByGroupIdAsync(group.DepartmentGroupId);
			if (existing != null)
				return existing;

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = group.DepartmentId,
				ChannelType = (int)ChatChannelType.GroupDefault,
				Name = group.Name,
				GroupId = group.DepartmentGroupId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetByGroupIdAsync(group.DepartmentGroupId), cancellationToken);
		}

		public async Task<ChatChannel> EnsureIncidentChannelAsync(int departmentId, int callId, string callName, CancellationToken cancellationToken = default(CancellationToken))
		{
			var existing = await _chatChannelRepository.GetByCallIdAndTypeAsync(callId, (int)ChatChannelType.Incident);
			if (existing != null)
				return existing;

			// Callers without the call in hand (the backfill) pass no name; resolve it here so healed
			// channels get the real call name instead of the "Call {id}" fallback.
			var name = !string.IsNullOrWhiteSpace(callName) ? callName.Trim() : await ResolveIncidentPrefixAsync(callId, null);

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.Incident,
				Name = name,
				CallId = callId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetByCallIdAndTypeAsync(callId, (int)ChatChannelType.Incident), cancellationToken);
		}

		public async Task<ChatChannel> EnsureLaneChannelAsync(CommandStructureNode node, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (node == null)
				return null;

			return await EnsureLaneChannelCoreAsync(node, await ResolveLanePrefixAsync(node.CallId), cancellationToken);
		}

		/// <summary>
		/// Incident-scoped channel names all start with the incident (or call) name; the incident channel's
		/// own name IS that prefix, so prefer reusing it over re-deriving from the call.
		/// </summary>
		private async Task<string> ResolveLanePrefixAsync(int callId)
		{
			var incidentChannel = await _chatChannelRepository.GetByCallIdAndTypeAsync(callId, (int)ChatChannelType.Incident);
			if (!string.IsNullOrWhiteSpace(incidentChannel?.Name))
				return incidentChannel.Name;

			return await ResolveIncidentPrefixAsync(callId, null);
		}

		private async Task<ChatChannel> EnsureLaneChannelCoreAsync(CommandStructureNode node, string prefix, CancellationToken cancellationToken)
		{
			var desiredName = BuildLaneChannelName(prefix, node.Name);

			var existing = await _chatChannelRepository.GetByCommandStructureNodeIdAsync(node.CommandStructureNodeId);
			if (existing != null)
				return await ApplyProvisionedNameAsync(existing, desiredName, cancellationToken);

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = node.DepartmentId,
				ChannelType = (int)ChatChannelType.IncidentLane,
				Name = desiredName,
				CallId = node.CallId,
				IncidentCommandId = node.IncidentCommandId,
				CommandStructureNodeId = node.CommandStructureNodeId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetByCommandStructureNodeIdAsync(node.CommandStructureNodeId), cancellationToken);
		}

		public async Task EnsureLaneChannelsAsync(IEnumerable<CommandStructureNode> nodes, CancellationToken cancellationToken = default(CancellationToken))
		{
			var nodeList = nodes?.Where(n => n != null).ToList();
			if (nodeList == null || nodeList.Count == 0)
				return;

			// One read for the whole call's channels instead of one lookup per node (the N+1 the
			// per-node EnsureLaneChannelAsync would incur). Nodes in a single establish share one call.
			var callId = nodeList[0].CallId;
			var existing = await _chatChannelRepository.GetByCallIdAsync(callId);
			var provisionedNodeIds = new HashSet<string>(
				(existing ?? Enumerable.Empty<ChatChannel>())
					.Where(c => c.ChannelType == (int)ChatChannelType.IncidentLane && !string.IsNullOrWhiteSpace(c.CommandStructureNodeId))
					.Select(c => c.CommandStructureNodeId),
				StringComparer.OrdinalIgnoreCase);

			var prefix = (existing ?? Enumerable.Empty<ChatChannel>())
				.FirstOrDefault(c => c.ChannelType == (int)ChatChannelType.Incident)?.Name;
			if (string.IsNullOrWhiteSpace(prefix))
				prefix = await ResolveIncidentPrefixAsync(callId, null);

			foreach (var node in nodeList)
			{
				if (provisionedNodeIds.Contains(node.CommandStructureNodeId))
					continue;

				// Provisioning inserts are serialized deliberately: they share the caller's unit-of-work
				// connection (single DbConnection is not concurrency-safe). N is the template lane count
				// (single digits) on a cold, once-per-incident path, so this is not a hot loop.
				await EnsureLaneChannelCoreAsync(node, prefix, cancellationToken);
				provisionedNodeIds.Add(node.CommandStructureNodeId);
			}
		}

		public async Task<ChatChannel> EnsureCommandChannelAsync(IncidentCommand command, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (command == null)
				return null;

			return await EnsureCommandScopedChannelCoreAsync(command.DepartmentId, command.CallId, command.IncidentCommandId,
				ChatChannelType.IncidentCommand, await ResolveIncidentPrefixAsync(command.CallId, command.Name), cancellationToken);
		}

		public async Task<ChatChannel> EnsureLeadsChannelAsync(IncidentCommand command, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (command == null)
				return null;

			return await EnsureCommandScopedChannelCoreAsync(command.DepartmentId, command.CallId, command.IncidentCommandId,
				ChatChannelType.IncidentLeads, await ResolveIncidentPrefixAsync(command.CallId, command.Name), cancellationToken);
		}

		public async Task<ChatChannel> EnsureDispatchChannelAsync(int departmentId, int callId, string incidentCommandId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (callId <= 0)
				return null;

			return await EnsureCommandScopedChannelCoreAsync(departmentId, callId, incidentCommandId,
				ChatChannelType.IncidentDispatch, await ResolveIncidentPrefixAsync(callId, null), cancellationToken);
		}

		/// <summary>
		/// Shared ensure for the three command-scoped singletons (Command/All Leads/Dispatch): an existing
		/// channel is rebound to the current command and renamed if the incident prefix drifted; a missing
		/// one is created under its "{prefix} {suffix}" name.
		/// </summary>
		private async Task<ChatChannel> EnsureCommandScopedChannelCoreAsync(int departmentId, int callId, string incidentCommandId,
			ChatChannelType channelType, string prefix, CancellationToken cancellationToken)
		{
			var desiredName = BuildCommandScopedChannelName(channelType, prefix);

			var existing = await _chatChannelRepository.GetByCallIdAndTypeAsync(callId, (int)channelType);
			if (existing != null)
			{
				var rebound = await RebindCommandScopedChannelAsync(existing, incidentCommandId, cancellationToken);
				return await ApplyProvisionedNameAsync(rebound, desiredName, cancellationToken);
			}

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)channelType,
				Name = desiredName,
				CallId = callId,
				IncidentCommandId = incidentCommandId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetByCallIdAndTypeAsync(callId, (int)channelType), cancellationToken);
		}

		/// <summary>
		/// A call can host sequential incident commands (close command, establish a new one later), but its
		/// command/leads/dispatch channels are singletons per call and get reused. A reused channel still
		/// carries the closed command's id and archived state, so without this the new command's channels
		/// stay frozen and its close/reopen archive sweeps match nothing. Targeted update — a full-row
		/// write would rewind the atomic LastMessageSeq allocator.
		/// </summary>
		private async Task<ChatChannel> RebindCommandScopedChannelAsync(ChatChannel channel, string incidentCommandId, CancellationToken cancellationToken)
		{
			if (channel == null || string.IsNullOrWhiteSpace(incidentCommandId))
				return channel;

			var commandChanged = !string.Equals(channel.IncidentCommandId, incidentCommandId, StringComparison.OrdinalIgnoreCase);
			if (!commandChanged && !channel.IsArchived)
				return channel;

			await _chatChannelRepository.RebindToIncidentCommandAsync(channel.ChatChannelId, incidentCommandId, DateTime.UtcNow, cancellationToken);

			channel.IncidentCommandId = incidentCommandId;
			channel.IsArchived = false;
			channel.ArchivedOn = null;
			channel.ModifiedOn = DateTime.UtcNow;

			// The archive flag gates posting per cached permission verdicts; clients also need to re-read it.
			await _chatPermissionService.InvalidateChannelCacheAsync(channel.ChatChannelId);
			PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return channel;
		}

		public async Task<ChatChannel> EnsureUnitDispatchChannelAsync(int departmentId, int unitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (unitId <= 0)
				return null;

			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != departmentId)
				return null;

			var dmKey = BuildUnitDispatchKey(unitId);
			var desiredName = BuildUnitDispatchChannelName(unit.Name);

			var existing = await _chatChannelRepository.GetByDmKeyAsync(departmentId, dmKey);
			if (existing != null)
				return await ApplyProvisionedNameAsync(existing, desiredName, cancellationToken);

			var channel = new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.UnitDispatch,
				Name = desiredName,
				CreatedOn = DateTime.UtcNow,
				DmKey = dmKey
			};

			// The unit rides an explicit member row (like a unit DM) so its operators surface the channel
			// through the unit-membership pass; the dispatch side stays an implicit audience.
			var members = new List<ChatChannelMember>
			{
				NewMemberRow(channel, ChatParticipantType.Unit, null, unitId, unit.Name, null)
			};

			ChatChannel saved;
			try
			{
				// Same atomic channel+members insert the DM path uses; the unique (DepartmentId, DmKey)
				// index backstops concurrent provisioning.
				saved = await _chatChannelRepository.CreateDirectMessageChannelAsync(channel, members, cancellationToken);
			}
			catch (Exception)
			{
				var winner = await _chatChannelRepository.GetByDmKeyAsync(departmentId, dmKey);
				if (winner != null)
					return winner;

				throw;
			}

			if (saved == null)
				saved = await _chatChannelRepository.GetByDmKeyAsync(departmentId, dmKey);

			if (saved != null && string.Equals(saved.ChatChannelId, channel.ChatChannelId, StringComparison.OrdinalIgnoreCase))
			{
				// Roll the list caches so the dispatch desk sees the unit's new line without waiting out
				// the 45s per-user list cache.
				await _chatPermissionService.InvalidateChannelCacheAsync(saved.ChatChannelId);
				PublishChannelEvent(saved, ChatEventKinds.ChannelProvisioned);
			}

			return saved;
		}

		public async Task EnsureIncidentChannelsAsync(IncidentCommand command, IEnumerable<CommandStructureNode> nodes, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (command == null || command.CallId <= 0)
				return;

			// A closed command's channels are a frozen record. Anything created now would be unarchived,
			// so the freeze would silently lift for a channel nobody ever posted in.
			if (command.Status != (int)IncidentCommandStatus.Active)
				return;

			var markerKey = $"chat:incidentbackfill:{command.IncidentCommandId}";

			try
			{
				if (!string.IsNullOrEmpty(await _cacheProvider.GetStringAsync(markerKey)))
					return;
			}
			catch (Exception ex)
			{
				// A cache outage must not stop the backfill — worst case it runs again on the next read.
				Logging.LogException(ex);
			}

			try
			{
				// One read of the call's channels covers every check below, instead of a lookup per Ensure*.
				var existing = (await _chatChannelRepository.GetByCallIdAsync(command.CallId))?.ToList() ?? new List<ChatChannel>();

				// Every incident-scoped channel is named "{incident name, or call name} {suffix}". This is
				// the one place the command is in hand, so names set before the command existed (or before
				// it was renamed by a re-establish) are refreshed here alongside the missing-channel fill.
				var prefix = await ResolveIncidentPrefixAsync(command.CallId, command.Name);

				var incidentChannel = existing.FirstOrDefault(c => c.ChannelType == (int)ChatChannelType.Incident);
				if (incidentChannel == null)
					await EnsureIncidentChannelAsync(command.DepartmentId, command.CallId, prefix, cancellationToken);
				else
					await ApplyProvisionedNameAsync(incidentChannel, prefix, cancellationToken);

				// Command-scoped channels are reused across sequential commands on the same call, so a
				// found channel still needs rebinding to this command (and unarchiving) — see the helper.
				foreach (var channelType in new[] { ChatChannelType.IncidentCommand, ChatChannelType.IncidentLeads, ChatChannelType.IncidentDispatch })
				{
					var channel = existing.FirstOrDefault(c => c.ChannelType == (int)channelType);
					if (channel == null)
					{
						await EnsureCommandScopedChannelCoreAsync(command.DepartmentId, command.CallId, command.IncidentCommandId, channelType, prefix, cancellationToken);
					}
					else
					{
						var rebound = await RebindCommandScopedChannelAsync(channel, command.IncidentCommandId, cancellationToken);
						await ApplyProvisionedNameAsync(rebound, BuildCommandScopedChannelName(channelType, prefix), cancellationToken);
					}
				}

				var lanesByNodeId = existing
					.Where(c => c.ChannelType == (int)ChatChannelType.IncidentLane && !string.IsNullOrWhiteSpace(c.CommandStructureNodeId))
					.GroupBy(c => c.CommandStructureNodeId, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

				var liveNodes = (nodes ?? Enumerable.Empty<CommandStructureNode>())
					.Where(n => n != null && !n.DeletedOn.HasValue)
					.ToList();

				// Serialized deliberately: these share the caller's unit-of-work connection, which is not
				// concurrency-safe. Bounded by the lane count on a once-per-incident path.
				foreach (var node in liveNodes)
				{
					if (lanesByNodeId.TryGetValue(node.CommandStructureNodeId, out var laneChannel))
						await ApplyProvisionedNameAsync(laneChannel, BuildLaneChannelName(prefix, node.Name), cancellationToken);
					else
						await EnsureLaneChannelCoreAsync(node, prefix, cancellationToken);
				}

				await _cacheProvider.SetStringAsync(markerKey, "1", IncidentBackfillCacheLength);
			}
			catch (Exception ex)
			{
				// Best-effort: chat provisioning must never cost the caller their board or incident view.
				Logging.LogException(ex);
			}
		}

		public async Task<ChatChannel> EnsureChatbotChannelAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var existing = await _chatChannelRepository.GetChatbotChannelAsync(departmentId, userId);
			if (existing != null)
				return existing;

			var channel = await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.Chatbot,
				Name = "Assistant",
				OwnerUserId = userId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetChatbotChannelAsync(departmentId, userId), cancellationToken);

			if (channel != null && channel.OwnerUserId == userId)
			{
				var ownerMember = await _chatChannelMemberRepository.GetUserMemberAsync(channel.ChatChannelId, userId);
				if (ownerMember == null)
				{
					await AddMemberRowAsync(channel, ChatParticipantType.User, userId, null, null, userId, cancellationToken);
					await _chatChannelMemberRepository.InsertAsync(new ChatChannelMember
					{
						ChatChannelMemberId = Guid.NewGuid().ToString(),
						ChatChannelId = channel.ChatChannelId,
						DepartmentId = departmentId,
						ParticipantType = (int)ChatParticipantType.Bot,
						DisplayNameOverride = "Resgrid Assistant",
						JoinedOn = DateTime.UtcNow
					}, cancellationToken);
				}
			}

			return channel;
		}

		public async Task<bool> SetIncidentChannelsArchivedAsync(int callId, bool archived, CancellationToken cancellationToken = default(CancellationToken))
		{
			var affected = await _chatChannelRepository.SetArchivedByCallIdAsync(callId, archived, archived ? DateTime.UtcNow : (DateTime?)null);
			return await PublishArchiveChangeAsync(affected);
		}

		public async Task<bool> SetCommandChannelsArchivedAsync(string incidentCommandId, bool archived, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (string.IsNullOrWhiteSpace(incidentCommandId))
				return false;

			var affected = await _chatChannelRepository.SetArchivedByIncidentCommandIdAsync(incidentCommandId, archived, archived ? DateTime.UtcNow : (DateTime?)null);
			return await PublishArchiveChangeAsync(affected);
		}

		/// <summary>
		/// Drops the cached permission evaluations for every channel whose archived flag just moved and
		/// tells connected clients to re-read it — a frozen channel has to stop accepting posts on every
		/// device immediately, not whenever the cache happens to expire.
		/// </summary>
		private async Task<bool> PublishArchiveChangeAsync(IEnumerable<string> affectedChannelIds)
		{
			var affectedList = affectedChannelIds?.ToList() ?? new List<string>();

			foreach (var channelId in affectedList)
				await _chatPermissionService.InvalidateChannelCacheAsync(channelId);

			if (affectedList.Count > 0)
			{
				var channels = await _chatChannelRepository.GetByIdsAsync(affectedList);
				if (channels != null)
					foreach (var channel in channels)
						PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);
			}

			return affectedList.Count > 0;
		}

		public async Task<ChatDepartmentSetting> GetDepartmentSettingsAsync(int departmentId)
		{
			var settings = await _chatDepartmentSettingRepository.GetByDepartmentIdAsync(departmentId);
			if (settings != null)
				return settings;

			// Config-driven defaults; not persisted until an admin saves.
			return new ChatDepartmentSetting
			{
				DepartmentId = departmentId,
				RetentionDays = ChatConfig.DefaultRetentionDays,
				AllowImages = true,
				AllowGifs = true,
				AllowLocationSharing = true,
				UrgentOverridesMute = true,
				MaxAttachmentSizeMb = ChatConfig.MaxAttachmentSizeMb,
				ChatbotEnabled = true,
				ChatbotFallbackEnabled = ChatConfig.ChatbotFallbackEnabled
			};
		}

		public async Task<ChatDepartmentSetting> SaveDepartmentSettingsAsync(ChatDepartmentSetting settings, CancellationToken cancellationToken = default(CancellationToken))
		{
			var existing = await _chatDepartmentSettingRepository.GetByDepartmentIdAsync(settings.DepartmentId);
			if (existing == null)
			{
				settings.ChatDepartmentSettingId = Guid.NewGuid().ToString();
				settings.ModifiedOn = DateTime.UtcNow;
				return await _chatDepartmentSettingRepository.InsertAsync(settings, cancellationToken);
			}

			existing.RetentionDays = settings.RetentionDays;
			existing.AllowImages = settings.AllowImages;
			existing.AllowGifs = settings.AllowGifs;
			existing.AllowLocationSharing = settings.AllowLocationSharing;
			existing.UrgentOverridesMute = settings.UrgentOverridesMute;
			existing.MaxAttachmentSizeMb = settings.MaxAttachmentSizeMb;
			existing.ChatbotEnabled = settings.ChatbotEnabled;
			existing.ChatbotFallbackEnabled = settings.ChatbotFallbackEnabled;
			existing.ModifiedOn = DateTime.UtcNow;

			return await _chatDepartmentSettingRepository.UpdateAsync(existing, cancellationToken);
		}

		private async Task<ChatChannel> InsertProvisionedChannelAsync(ChatChannel channel, Func<Task<ChatChannel>> reFetch, CancellationToken cancellationToken)
		{
			try
			{
				var saved = await _chatChannelRepository.InsertAsync(channel, cancellationToken);
				PublishChannelEvent(saved, ChatEventKinds.ChannelProvisioned);
				return saved;
			}
			catch (Exception)
			{
				// Unique provisioning indexes backstop concurrent Ensure* calls; adopt the winner.
				var winner = await reFetch();
				if (winner != null)
					return winner;

				throw;
			}
		}

		private async Task<ChatChannelMember> AddMemberRowAsync(ChatChannel channel, ChatParticipantType participantType, string userId, int? unitId, string displayNameOverride, string addedByUserId, CancellationToken cancellationToken, bool isModerator = false)
		{
			return await _chatChannelMemberRepository.InsertAsync(NewMemberRow(channel, participantType, userId, unitId, displayNameOverride, addedByUserId, isModerator), cancellationToken);
		}

		private static ChatChannelMember NewMemberRow(ChatChannel channel, ChatParticipantType participantType, string userId, int? unitId, string displayNameOverride, string addedByUserId, bool isModerator = false)
		{
			return new ChatChannelMember
			{
				ChatChannelMemberId = Guid.NewGuid().ToString(),
				ChatChannelId = channel.ChatChannelId,
				DepartmentId = channel.DepartmentId,
				ParticipantType = (int)participantType,
				UserId = userId,
				UnitId = unitId,
				DisplayNameOverride = displayNameOverride,
				IsModerator = isModerator,
				JoinedOn = DateTime.UtcNow,
				AddedByUserId = addedByUserId
			};
		}

		private void PublishChannelEvent(ChatChannel channel, string kind)
		{
			if (channel == null)
				return;

			_eventAggregator.SendMessage<ChatEventRaised>(new ChatEventRaised
			{
				DepartmentId = channel.DepartmentId,
				ChatChannelId = channel.ChatChannelId,
				Kind = kind,
				PayloadJson = JsonConvert.SerializeObject(new
				{
					channel.ChatChannelId,
					channel.DepartmentId,
					channel.ChannelType,
					channel.Name,
					channel.Topic,
					channel.CallId,
					channel.CommandStructureNodeId,
					channel.GroupId,
					channel.IsArchived,
					channel.IsLocked,
					channel.LastMessageSeq,
					channel.LastMessageOn
				})
			});
		}

		private static string BuildDmKey(string creatorUserId, string targetUserId, int? targetUnitId)
		{
			var parts = new List<string> { $"u:{creatorUserId?.ToLowerInvariant()}" };

			if (targetUnitId.HasValue)
				parts.Add($"unit:{targetUnitId.Value}");
			else
				parts.Add($"u:{targetUserId?.ToLowerInvariant()}");

			parts.Sort(StringComparer.Ordinal);

			return string.Join("|", parts);
		}

		/// <summary>
		/// UnitDispatch channels ride the (DepartmentId, DmKey) unique index for dedup — not a DM, but the
		/// same one-channel-per-identity constraint, and the prefix keeps the keyspaces disjoint.
		/// </summary>
		private static string BuildUnitDispatchKey(int unitId) => $"unitdispatch:{unitId}";

		/// <summary>
		/// The incident prefix every incident-scoped channel name starts with: the incident's own name when
		/// command gave it one, otherwise the call's name, otherwise the call id.
		/// </summary>
		private async Task<string> ResolveIncidentPrefixAsync(int callId, string incidentName)
		{
			if (!string.IsNullOrWhiteSpace(incidentName))
				return incidentName.Trim();

			try
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				if (!string.IsNullOrWhiteSpace(call?.Name))
					return call.Name.Trim();
			}
			catch (Exception ex)
			{
				// Naming is cosmetic next to provisioning — never let a call lookup break channel creation.
				Logging.LogException(ex);
			}

			return $"Call {callId}";
		}

		private static string BuildCommandScopedChannelName(ChatChannelType channelType, string prefix)
		{
			switch (channelType)
			{
				case ChatChannelType.IncidentCommand:
					// The "(private)" marker is part of the contract with the apps: it is how the command
					// channel reads as command-staff-only in a flat channel list.
					return $"{prefix} Command (private)";

				case ChatChannelType.IncidentLeads:
					return $"{prefix} All Leads";

				case ChatChannelType.IncidentDispatch:
					return $"{prefix} Dispatch";

				default:
					return prefix;
			}
		}

		private static string BuildLaneChannelName(string prefix, string laneName)
			=> string.IsNullOrWhiteSpace(laneName) ? prefix : $"{prefix} {laneName.Trim()}";

		private static string BuildUnitDispatchChannelName(string unitName)
			=> string.IsNullOrWhiteSpace(unitName) ? "Dispatch" : $"{unitName.Trim()} Dispatch";

		/// <summary>
		/// Applies the computed provisioning name to an existing channel when it drifted — the incident got
		/// its name after establish, a lane or unit was renamed. Targeted update (never a full-row write,
		/// which would rewind the atomic LastMessageSeq allocator), and clients are told to re-read.
		/// </summary>
		private async Task<ChatChannel> ApplyProvisionedNameAsync(ChatChannel channel, string desiredName, CancellationToken cancellationToken)
		{
			if (channel == null || string.IsNullOrWhiteSpace(desiredName) || string.Equals(channel.Name, desiredName, StringComparison.Ordinal))
				return channel;

			channel.Name = desiredName;
			channel.ModifiedOn = DateTime.UtcNow;

			await _chatChannelRepository.UpdateChannelInfoAsync(channel.ChatChannelId, channel.Name, channel.Topic, channel.ModifiedOn.Value, cancellationToken);

			await _chatPermissionService.InvalidateChannelCacheAsync(channel.ChatChannelId);
			PublishChannelEvent(channel, ChatEventKinds.ChannelUpdated);

			return channel;
		}
	}
}
