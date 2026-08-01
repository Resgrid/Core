using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
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

		private readonly IChatChannelRepository _chatChannelRepository;
		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IChatChannelAccessRuleRepository _chatChannelAccessRuleRepository;
		private readonly IChatDepartmentSettingRepository _chatDepartmentSettingRepository;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;
		private readonly IUnitOfWork _unitOfWork;

		public ChatChannelService(IChatChannelRepository chatChannelRepository, IChatChannelMemberRepository chatChannelMemberRepository,
			IChatChannelAccessRuleRepository chatChannelAccessRuleRepository, IChatDepartmentSettingRepository chatDepartmentSettingRepository,
			IChatPermissionService chatPermissionService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IUnitsService unitsService, IUserProfileService userProfileService, IEventAggregator eventAggregator,
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
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
			_unitOfWork = unitOfWork;
		}

		public async Task<ChatChannel> GetChannelByIdAsync(string chatChannelId)
		{
			return await _chatChannelRepository.GetByIdAsync(chatChannelId);
		}

		public async Task<List<ChatChannel>> GetChannelsForUserAsync(int departmentId, string userId, int? activeUnitId, bool includeArchived = false)
		{
			async Task<List<ChatChannel>> getChannels()
			{
				var results = new Dictionary<string, ChatChannel>(StringComparer.OrdinalIgnoreCase);

				var departmentChannel = await EnsureDepartmentChannelAsync(departmentId);
				if (departmentChannel != null)
					results[departmentChannel.ChatChannelId] = departmentChannel;

				var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
				if (group != null)
				{
					var groupChannel = await EnsureGroupChannelAsync(group);
					if (groupChannel != null)
						results[groupChannel.ChatChannelId] = groupChannel;
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

				// Implicit-audience channels (custom rule-based + active incident channels): evaluate access
				// per channel; evaluations are cached by the permission service.
				var allChannels = await _chatChannelRepository.GetAllByDepartmentIdAsync(departmentId, includeArchived);
				if (allChannels != null)
				{
					foreach (var channel in allChannels)
					{
						if (results.ContainsKey(channel.ChatChannelId))
							continue;

						var type = (ChatChannelType)channel.ChannelType;
						if (type != ChatChannelType.CustomLocked && type != ChatChannelType.Incident &&
							type != ChatChannelType.IncidentLane && type != ChatChannelType.IncidentCommand)
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
				PublishChannelEvent(saved, ChatEventKinds.ChannelProvisioned);

			return saved;
		}

		public async Task<ChatChannel> CreateAdHocGroupChannelAsync(int departmentId, string creatorUserId, string name, List<string> memberUserIds, CancellationToken cancellationToken = default(CancellationToken))
		{
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

			if (memberUserIds != null)
			{
				foreach (var memberId in memberUserIds.Where(m => !string.IsNullOrWhiteSpace(m) && !string.Equals(m, creatorUserId, StringComparison.OrdinalIgnoreCase)).Distinct())
				{
					if (!await _departmentsService.IsUserInDepartmentAsync(departmentId, memberId))
						throw new UnauthorizedAccessException("Every member must belong to this department.");

					await AddMemberRowAsync(channel, ChatParticipantType.User, memberId, null, null, creatorUserId, cancellationToken);
				}
			}

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

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChannelType = (int)ChatChannelType.Incident,
				Name = string.IsNullOrWhiteSpace(callName) ? $"Call {callId}" : callName,
				CallId = callId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetByCallIdAndTypeAsync(callId, (int)ChatChannelType.Incident), cancellationToken);
		}

		public async Task<ChatChannel> EnsureLaneChannelAsync(CommandStructureNode node, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (node == null)
				return null;

			var existing = await _chatChannelRepository.GetByCommandStructureNodeIdAsync(node.CommandStructureNodeId);
			if (existing != null)
				return existing;

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = node.DepartmentId,
				ChannelType = (int)ChatChannelType.IncidentLane,
				Name = node.Name,
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

			foreach (var node in nodeList)
			{
				if (provisionedNodeIds.Contains(node.CommandStructureNodeId))
					continue;

				// Provisioning inserts are serialized deliberately: they share the caller's unit-of-work
				// connection (single DbConnection is not concurrency-safe). N is the template lane count
				// (single digits) on a cold, once-per-incident path, so this is not a hot loop.
				await EnsureLaneChannelAsync(node, cancellationToken);
				provisionedNodeIds.Add(node.CommandStructureNodeId);
			}
		}

		public async Task<ChatChannel> EnsureCommandChannelAsync(IncidentCommand command, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (command == null)
				return null;

			var existing = await _chatChannelRepository.GetByCallIdAndTypeAsync(command.CallId, (int)ChatChannelType.IncidentCommand);
			if (existing != null)
				return existing;

			return await InsertProvisionedChannelAsync(new ChatChannel
			{
				ChatChannelId = Guid.NewGuid().ToString(),
				DepartmentId = command.DepartmentId,
				ChannelType = (int)ChatChannelType.IncidentCommand,
				Name = "Command",
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				CreatedOn = DateTime.UtcNow
			}, () => _chatChannelRepository.GetByCallIdAndTypeAsync(command.CallId, (int)ChatChannelType.IncidentCommand), cancellationToken);
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
			var affectedList = affected?.ToList() ?? new List<string>();

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
	}
}
