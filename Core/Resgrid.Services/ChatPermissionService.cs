using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProtoBuf;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Single authority for chat access decisions. Evaluations for a channel are cached briefly under a
	/// per-channel version so membership/role changes can invalidate every user's cached result at once
	/// (bump the version instead of enumerating per-user keys).
	/// </summary>
	public class ChatPermissionService : IChatPermissionService
	{
		private static readonly TimeSpan CacheLength = TimeSpan.FromSeconds(60);
		private static readonly TimeSpan VersionCacheLength = TimeSpan.FromDays(1);

		/// <summary>Shared version key rolled into every per-user channel-list cache key; bumped by InvalidateChannelCacheAsync.</summary>
		internal const string ChannelListVersionCacheKey = "chatchannellistver";

		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IChatChannelAccessRuleRepository _chatChannelAccessRuleRepository;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly IDispatchAccessService _dispatchAccessService;
		private readonly ICacheProvider _cacheProvider;

		public ChatPermissionService(IChatChannelMemberRepository chatChannelMemberRepository, IChatChannelAccessRuleRepository chatChannelAccessRuleRepository,
			IAuthorizationService authorizationService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService, IUnitsService unitsService, ICallsService callsService,
			IIncidentCommandService incidentCommandService, IDispatchAccessService dispatchAccessService, ICacheProvider cacheProvider)
		{
			_chatChannelMemberRepository = chatChannelMemberRepository;
			_chatChannelAccessRuleRepository = chatChannelAccessRuleRepository;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_unitsService = unitsService;
			_callsService = callsService;
			_incidentCommandService = incidentCommandService;
			_dispatchAccessService = dispatchAccessService;
			_cacheProvider = cacheProvider;
		}

		public async Task<bool> CanAccessChannelAsync(ChatChannel channel, string userId, int? activeUnitId)
		{
			if (channel == null || string.IsNullOrWhiteSpace(userId))
				return false;

			var cacheKey = await GetPermCacheKeyAsync(channel.ChatChannelId, "access", userId, activeUnitId);
			var cached = await _cacheProvider.GetStringAsync(cacheKey);
			if (cached == "1")
				return true;
			if (cached == "0")
				return false;

			var result = await EvaluateAccessAsync(channel, userId, activeUnitId);

			await _cacheProvider.SetStringAsync(cacheKey, result ? "1" : "0", CacheLength);

			return result;
		}

		public async Task<bool> CanPostAsync(ChatChannel channel, string userId, int? asUnitId)
		{
			if (channel == null || string.IsNullOrWhiteSpace(userId))
				return false;

			if (channel.IsArchived)
				return false;

			if (asUnitId.HasValue && !await CanSendAsUnitAsync(userId, asUnitId.Value, channel.DepartmentId))
				return false;

			if (!await CanAccessChannelAsync(channel, userId, asUnitId))
				return false;

			// Mute/ban state lives on the participant's member row (lazy rows may not exist yet = clean state).
			var member = asUnitId.HasValue
				? await _chatChannelMemberRepository.GetUnitMemberAsync(channel.ChatChannelId, asUnitId.Value)
				: await _chatChannelMemberRepository.GetUserMemberAsync(channel.ChatChannelId, userId);

			if (member != null)
			{
				if (member.IsBanned)
					return false;

				if (member.MutedUntil.HasValue && member.MutedUntil.Value > DateTime.UtcNow)
					return false;
			}

			// Also check the human's own row when posting as a unit — a banned user can't hide behind the unit identity.
			if (asUnitId.HasValue)
			{
				var userMember = await _chatChannelMemberRepository.GetUserMemberAsync(channel.ChatChannelId, userId);
				if (userMember != null && (userMember.IsBanned || (userMember.MutedUntil.HasValue && userMember.MutedUntil.Value > DateTime.UtcNow)))
					return false;
			}

			if (channel.IsLocked && !await CanModerateChannelAsync(channel, userId))
				return false;

			return true;
		}

		public async Task<bool> CanModerateChannelAsync(ChatChannel channel, string userId)
		{
			if (channel == null || string.IsNullOrWhiteSpace(userId))
				return false;

			var cacheKey = await GetPermCacheKeyAsync(channel.ChatChannelId, "mod", userId, null);
			var cached = await _cacheProvider.GetStringAsync(cacheKey);
			if (cached == "1")
				return true;
			if (cached == "0")
				return false;

			var result = await EvaluateModerateAsync(channel, userId);

			await _cacheProvider.SetStringAsync(cacheKey, result ? "1" : "0", CacheLength);

			return result;
		}

		public async Task<bool> CanSendAsUnitAsync(string userId, int unitId, int departmentId)
		{
			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != departmentId)
				return false;

			// The user must actively crew the unit — mere department membership is not enough to speak as it.
			var activeRoles = await _unitsService.GetActiveRolesForUnitAsync(unitId);
			return activeRoles != null && activeRoles.Any(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));
		}

		public async Task<bool> CanSendAsIcAsync(string userId, int callId, int departmentId)
		{
			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command == null)
				return false;

			if (command.CurrentCommanderUserId == userId || command.EstablishedByUserId == userId)
				return true;

			var roles = await _incidentCommandService.GetIncidentRolesAsync(departmentId, callId);
			return roles != null && roles.Any(r => r.UserId == userId && !r.RemovedOn.HasValue);
		}

		public async Task<List<string>> ResolveChannelAudienceUserIdsAsync(ChatChannel channel)
		{
			var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			if (channel == null)
				return userIds.ToList();

			switch ((ChatChannelType)channel.ChannelType)
			{
				case ChatChannelType.Chatbot:
					AddIfSet(userIds, channel.OwnerUserId);
					break;

				case ChatChannelType.DepartmentDefault:
					var deptMembers = await _departmentsService.GetAllMembersForDepartmentAsync(channel.DepartmentId);
					if (deptMembers != null)
						foreach (var m in deptMembers.Where(x => !x.IsDisabled.GetValueOrDefault() && !x.IsDeleted))
							AddIfSet(userIds, m.UserId);
					break;

				case ChatChannelType.GroupDefault:
					if (channel.GroupId.HasValue)
					{
						var groupMembers = await _departmentGroupsService.GetAllMembersForGroupAsync(channel.GroupId.Value);
						if (groupMembers != null)
							foreach (var m in groupMembers)
								AddIfSet(userIds, m.UserId);
					}
					break;

				case ChatChannelType.CustomLocked:
					await AddCustomChannelAudienceAsync(channel, userIds);
					await AddExplicitMemberAudienceAsync(channel, userIds);
					break;

				case ChatChannelType.Incident:
					await AddIncidentAudienceAsync(channel, userIds);
					// The desk follows the incident's shared conversation, not just its own dispatch line.
					foreach (var dispatcherId in await _dispatchAccessService.GetDispatchUserIdsAsync(channel.DepartmentId))
						AddIfSet(userIds, dispatcherId);
					break;

				case ChatChannelType.IncidentLane:
					await AddLaneAudienceAsync(channel, userIds);
					break;

				case ChatChannelType.IncidentCommand:
					await AddCommandStaffAsync(channel.DepartmentId, channel.CallId.GetValueOrDefault(), userIds);
					break;

				case ChatChannelType.IncidentLeads:
					await AddLaneLeadsAsync(channel.DepartmentId, channel.CallId.GetValueOrDefault(), userIds);
					break;

				case ChatChannelType.IncidentDispatch:
					await AddIncidentAudienceAsync(channel, userIds);
					foreach (var dispatcherId in await _dispatchAccessService.GetDispatchUserIdsAsync(channel.DepartmentId))
						AddIfSet(userIds, dispatcherId);
					break;

				case ChatChannelType.UnitDispatch:
					// The unit's member row resolves to its active crew; the desk side is every dispatcher.
					await AddExplicitMemberAudienceAsync(channel, userIds);
					foreach (var dispatcherId in await _dispatchAccessService.GetDispatchUserIdsAsync(channel.DepartmentId))
						AddIfSet(userIds, dispatcherId);
					break;

				case ChatChannelType.IncidentCommanderLine:
					// Requester side is an explicit member row; the commander side is resolved live from the
					// call so a command transfer moves the conversation rather than copying it. Only the
					// CURRENT commander — deliberately not EstablishedByUserId or the wider command staff,
					// which is what separates this from the IncidentCommand channel.
					await AddExplicitMemberAudienceAsync(channel, userIds);
					AddIfSet(userIds, await GetCurrentCommanderUserIdAsync(channel.DepartmentId, channel.CallId.GetValueOrDefault()));
					break;

				default: // DirectMessage, AdHocGroup
					await AddExplicitMemberAudienceAsync(channel, userIds);
					break;
			}

			return userIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
		}

		public async Task InvalidateChannelCacheAsync(string chatChannelId)
		{
			if (string.IsNullOrWhiteSpace(chatChannelId))
				return;

			await _cacheProvider.IncrementAsync(GetVersionKey(chatChannelId), VersionCacheLength);

			// Roll every per-user channel-list cache key forward too (channel set/visibility changed).
			await _cacheProvider.IncrementAsync(ChannelListVersionCacheKey, VersionCacheLength);
		}

		private async Task<bool> EvaluateAccessAsync(ChatChannel channel, string userId, int? activeUnitId)
		{
			switch ((ChatChannelType)channel.ChannelType)
			{
				case ChatChannelType.Chatbot:
					return string.Equals(channel.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase);

				case ChatChannelType.DirectMessage:
				case ChatChannelType.AdHocGroup:
					if (await HasActiveMembershipAsync(channel.ChatChannelId, userId, null))
						return true;

					// The unit's member row only grants access when the caller actually crews the claimed
					// unit — a caller-supplied activeUnitId alone must not open another unit's channels.
					return activeUnitId.HasValue
						&& await CanSendAsUnitAsync(userId, activeUnitId.Value, channel.DepartmentId)
						&& await HasActiveMembershipAsync(channel.ChatChannelId, userId, activeUnitId);

				case ChatChannelType.DepartmentDefault:
					return await _departmentsService.IsUserInDepartmentAsync(channel.DepartmentId, userId);

				case ChatChannelType.GroupDefault:
					if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
						return true;

					if (!channel.GroupId.HasValue)
						return false;

					var groupMembers = await _departmentGroupsService.GetAllMembersForGroupAsync(channel.GroupId.Value);
					return groupMembers != null && groupMembers.Any(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));

				case ChatChannelType.CustomLocked:
					if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
						return true;

					if (await HasActiveMembershipAsync(channel.ChatChannelId, userId, null))
						return true;

					return await MatchesAccessRulesAsync(channel, userId);

				case ChatChannelType.Incident:
					if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
						return true;

					// Authorized dispatchers see every call's shared incident conversation — that is the
					// desk's job. The private command channel stays closed to them.
					if (await _dispatchAccessService.CanUseDispatchAsync(channel.DepartmentId, userId))
						return true;

					return await IsInIncidentAudienceAsync(channel, userId, activeUnitId);

				case ChatChannelType.IncidentLane:
					if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
						return true;

					return await IsInLaneAudienceAsync(channel, userId, activeUnitId);

				case ChatChannelType.IncidentCommand:
					if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
						return true;

					// Command staff ONLY — deliberately not widened to dispatch. Dispatch reaches command
					// through the incident's dispatch channel; this one stays internal to the people running
					// the incident so command can talk candidly.
					return await IsCommandStaffAsync(channel.DepartmentId, channel.CallId.GetValueOrDefault(), userId);

				case ChatChannelType.IncidentLeads:
					if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
						return true;

					return await IsLaneLeadOrCommanderAsync(channel.DepartmentId, channel.CallId.GetValueOrDefault(), userId);

				case ChatChannelType.IncidentDispatch:
					// Deliberately NOT widened to department admins the way the other incident channels are.
					// The whole point of the DispatchAppLogin permission is that an admin the department has
					// not authorized for dispatch stays out of dispatch traffic; they still get in if they
					// are actually working the incident.
					if (await _dispatchAccessService.CanUseDispatchAsync(channel.DepartmentId, userId))
						return true;

					return await IsInIncidentAudienceAsync(channel, userId, activeUnitId);

				case ChatChannelType.UnitDispatch:
				{
					// Same stance as IncidentDispatch: dispatch authorization, not admin standing, opens
					// dispatch traffic.
					if (await _dispatchAccessService.CanUseDispatchAsync(channel.DepartmentId, userId))
						return true;

					// The unit side is proven against the channel's OWN unit, never the caller-supplied
					// activeUnitId or a leftover user member row (lazy read-pointer rows outlive access) —
					// crewing some other unit must not open this unit's dispatch line.
					var owningUnitId = await GetUnitDispatchChannelUnitIdAsync(channel);
					return owningUnitId.HasValue
						&& await CanSendAsUnitAsync(userId, owningUnitId.Value, channel.DepartmentId);
				}

				case ChatChannelType.IncidentCommanderLine:
				{
					// Whoever currently holds command, by virtue of holding it. An outgoing commander loses
					// the line here on their next check — the history stays on the channel for the incoming
					// one. Deliberately NOT widened to department admins or dispatch: this is a private line.
					if (string.Equals(await GetCurrentCommanderUserIdAsync(channel.DepartmentId, channel.CallId.GetValueOrDefault()), userId, StringComparison.OrdinalIgnoreCase))
						return true;

					// Requester side, proven the same way DMs are — a unit's row only counts when the caller
					// actually crews that unit.
					if (await HasActiveMembershipAsync(channel.ChatChannelId, userId, null))
						return true;

					return activeUnitId.HasValue
						&& await CanSendAsUnitAsync(userId, activeUnitId.Value, channel.DepartmentId)
						&& await HasActiveMembershipAsync(channel.ChatChannelId, userId, activeUnitId);
				}

				default:
					return false;
			}
		}

		/// <summary>
		/// The user currently running the incident, or null when no command is established. Single source
		/// for every IncidentCommanderLine decision so the audience and the access check can never disagree
		/// about who "the IC" is mid-transfer.
		/// </summary>
		private async Task<string> GetCurrentCommanderUserIdAsync(int departmentId, int callId)
		{
			if (callId <= 0)
				return null;

			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);

			return command?.CurrentCommanderUserId;
		}

		private async Task<bool> EvaluateModerateAsync(ChatChannel channel, string userId)
		{
			// Department admins moderate every channel type (including DMs, for flagged-content handling).
			if (await IsDepartmentAdminAsync(channel.DepartmentId, userId))
				return true;

			var member = await _chatChannelMemberRepository.GetUserMemberAsync(channel.ChatChannelId, userId);
			if (member != null && member.IsModerator && !member.RemovedOn.HasValue)
				return true;

			switch ((ChatChannelType)channel.ChannelType)
			{
				case ChatChannelType.GroupDefault:
					if (!channel.GroupId.HasValue)
						return false;

					var groupMembers = await _departmentGroupsService.GetAllMembersForGroupAsync(channel.GroupId.Value);
					return groupMembers != null && groupMembers.Any(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase) && m.IsAdmin.GetValueOrDefault());

				case ChatChannelType.Incident:
				case ChatChannelType.IncidentLane:
				case ChatChannelType.IncidentCommand:
				case ChatChannelType.IncidentLeads:
				case ChatChannelType.IncidentDispatch:
				case ChatChannelType.IncidentCommanderLine:
					if (!channel.CallId.HasValue)
						return false;

					var command = await _incidentCommandService.GetCommandForCallAsync(channel.DepartmentId, channel.CallId.Value);
					if (command == null)
						return false;

					return string.Equals(command.CurrentCommanderUserId, userId, StringComparison.OrdinalIgnoreCase) ||
						   string.Equals(command.EstablishedByUserId, userId, StringComparison.OrdinalIgnoreCase);

				default:
					return false;
			}
		}

		/// <summary>
		/// The unit a UnitDispatch channel belongs to: parsed from its "unitdispatch:{unitId}" DmKey,
		/// falling back to the channel's unit member row.
		/// </summary>
		private async Task<int?> GetUnitDispatchChannelUnitIdAsync(ChatChannel channel)
		{
			const string keyPrefix = "unitdispatch:";
			if (!string.IsNullOrWhiteSpace(channel.DmKey)
				&& channel.DmKey.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(channel.DmKey.Substring(keyPrefix.Length), out var unitId))
				return unitId;

			var members = await _chatChannelMemberRepository.GetByChannelIdAsync(channel.ChatChannelId);
			return members?.FirstOrDefault(m => m.ParticipantType == (int)ChatParticipantType.Unit && !m.RemovedOn.HasValue && m.UnitId.HasValue)?.UnitId;
		}

		private async Task<bool> HasActiveMembershipAsync(string chatChannelId, string userId, int? activeUnitId)
		{
			var member = await _chatChannelMemberRepository.GetUserMemberAsync(chatChannelId, userId);
			if (member != null && !member.RemovedOn.HasValue && !member.IsBanned)
				return true;

			if (activeUnitId.HasValue)
			{
				var unitMember = await _chatChannelMemberRepository.GetUnitMemberAsync(chatChannelId, activeUnitId.Value);
				if (unitMember != null && !unitMember.RemovedOn.HasValue && !unitMember.IsBanned)
					return true;
			}

			return false;
		}

		private async Task<bool> MatchesAccessRulesAsync(ChatChannel channel, string userId)
		{
			var rules = await _chatChannelAccessRuleRepository.GetByChannelIdAsync(channel.ChatChannelId);
			if (rules == null)
				return false;

			var ruleList = rules.ToList();
			if (ruleList.Count == 0)
				return false;

			if (ruleList.Any(r => r.RuleType == (int)ChatAccessRuleType.User && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)))
				return true;

			var roleRules = ruleList.Where(r => r.RuleType == (int)ChatAccessRuleType.Role && r.PersonnelRoleId.HasValue).ToList();
			if (roleRules.Count > 0)
			{
				var userRoles = await _personnelRolesService.GetRolesForUserAsync(userId, channel.DepartmentId);
				if (userRoles != null && userRoles.Any(ur => roleRules.Any(rr => rr.PersonnelRoleId.Value == ur.PersonnelRoleId)))
					return true;
			}

			foreach (var groupRule in ruleList.Where(r => r.RuleType == (int)ChatAccessRuleType.GroupMembership && r.GroupId.HasValue))
			{
				var memberUserIds = await GetGroupRosterUserIdsAsync(groupRule.GroupId.Value);
				if (memberUserIds != null && memberUserIds.Any(id => string.Equals(id, userId, StringComparison.OrdinalIgnoreCase)))
					return true;
			}

			return false;
		}

		/// <summary>Group roster lookup cached briefly: access-rule evaluation walks every group rule per user, which would otherwise N+1 the group-membership table.</summary>
		private async Task<List<string>> GetGroupRosterUserIdsAsync(int groupId)
		{
			async Task<GroupRosterCache> getRoster()
			{
				var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);
				return new GroupRosterCache
				{
					UserIds = members?.Where(m => !string.IsNullOrWhiteSpace(m.UserId)).Select(m => m.UserId).ToList() ?? new List<string>()
				};
			}

			if (SystemBehaviorConfig.CacheEnabled)
			{
				var cached = await _cacheProvider.RetrieveAsync($"chatperm:grouproster:{groupId}", getRoster, CacheLength);
				return cached?.UserIds;
			}

			return (await getRoster()).UserIds;
		}

		[ProtoContract]
		public class GroupRosterCache
		{
			[ProtoMember(1)]
			public List<string> UserIds { get; set; } = new List<string>();
		}

		private async Task<bool> IsInIncidentAudienceAsync(ChatChannel channel, string userId, int? activeUnitId)
		{
			if (!channel.CallId.HasValue)
				return false;

			var callId = channel.CallId.Value;

			if (await IsCommandStaffAsync(channel.DepartmentId, callId, userId))
				return true;

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call != null)
			{
				if (call.Dispatches != null && call.Dispatches.Any(d => string.Equals(d.UserId, userId, StringComparison.OrdinalIgnoreCase)))
					return true;

				// A caller-supplied activeUnitId only grants access when the user actually crews that unit.
				if (activeUnitId.HasValue && call.UnitDispatches != null && call.UnitDispatches.Any(d => d.UnitId == activeUnitId.Value)
					&& await CanSendAsUnitAsync(userId, activeUnitId.Value, channel.DepartmentId))
					return true;

				if (call.GroupDispatches != null && call.GroupDispatches.Any())
				{
					foreach (var groupDispatch in call.GroupDispatches)
					{
						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupDispatch.DepartmentGroupId);
						if (members != null && members.Any(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase)))
							return true;
					}
				}

				if (call.RoleDispatches != null && call.RoleDispatches.Any())
				{
					var userRoles = await _personnelRolesService.GetRolesForUserAsync(userId, channel.DepartmentId);
					if (userRoles != null && call.RoleDispatches.Any(rd => userRoles.Any(ur => ur.PersonnelRoleId == rd.RoleId)))
						return true;
				}
			}

			// Resources placed on the command board are part of the incident even if not dispatched.
			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(channel.DepartmentId, callId);
			if (assignments != null)
			{
				foreach (var assignment in assignments.Where(a => !a.ReleasedOn.HasValue))
				{
					if (await MatchesResourceForIncidentAccessAsync(assignment, userId, activeUnitId, channel.DepartmentId))
						return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Incident-channel resource matching: personnel matches grant directly; unit matches only when the
		/// user actively crews the matched unit (a caller-supplied activeUnitId alone is not proof).
		/// </summary>
		private async Task<bool> MatchesResourceForIncidentAccessAsync(ResourceAssignment assignment, string userId, int? activeUnitId, int departmentId)
		{
			if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel || assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel)
				return string.Equals(assignment.ResourceId, userId, StringComparison.OrdinalIgnoreCase);

			if (activeUnitId.HasValue && (assignment.ResourceKind == (int)ResourceAssignmentKind.RealUnit || assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit)
				&& assignment.ResourceId == activeUnitId.Value.ToString())
				return await CanSendAsUnitAsync(userId, activeUnitId.Value, departmentId);

			return false;
		}

		private async Task<bool> IsInLaneAudienceAsync(ChatChannel channel, string userId, int? activeUnitId)
		{
			if (!channel.CallId.HasValue || string.IsNullOrWhiteSpace(channel.CommandStructureNodeId))
				return false;

			var callId = channel.CallId.Value;

			if (await IsCommandStaffAsync(channel.DepartmentId, callId, userId))
				return true;

			var nodes = await _incidentCommandService.GetNodesForCallAsync(channel.DepartmentId, callId);
			var node = nodes?.FirstOrDefault(n => n.CommandStructureNodeId == channel.CommandStructureNodeId);
			if (node != null)
			{
				if (string.Equals(node.SupervisorUserId, userId, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(node.PrimaryLeadUserId, userId, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(node.SecondaryLeadUserId, userId, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(channel.DepartmentId, callId);
			if (assignments != null)
			{
				foreach (var assignment in assignments.Where(a => !a.ReleasedOn.HasValue && a.CommandStructureNodeId == channel.CommandStructureNodeId))
				{
					if (MatchesResource(assignment, userId, activeUnitId))
						return true;
				}
			}

			return false;
		}

		/// <summary>
		/// "All Leads" audience: the Incident Commander plus every lane's primary and secondary lead.
		/// Deliberately derived from the lanes on each check rather than stored as membership — a lead who
		/// is replaced on the board loses the channel without anyone having to remember to remove them.
		/// </summary>
		private async Task<bool> IsLaneLeadOrCommanderAsync(int departmentId, int callId, string userId)
		{
			if (callId <= 0)
				return false;

			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command != null &&
				(string.Equals(command.CurrentCommanderUserId, userId, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(command.EstablishedByUserId, userId, StringComparison.OrdinalIgnoreCase)))
				return true;

			var nodes = await _incidentCommandService.GetNodesForCallAsync(departmentId, callId);
			if (nodes == null)
				return false;

			return nodes.Any(n => !n.DeletedOn.HasValue &&
				(string.Equals(n.PrimaryLeadUserId, userId, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(n.SecondaryLeadUserId, userId, StringComparison.OrdinalIgnoreCase)));
		}

		private async Task AddLaneLeadsAsync(int departmentId, int callId, HashSet<string> userIds)
		{
			if (callId <= 0)
				return;

			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command != null)
			{
				AddIfSet(userIds, command.CurrentCommanderUserId);
				AddIfSet(userIds, command.EstablishedByUserId);
			}

			var nodes = await _incidentCommandService.GetNodesForCallAsync(departmentId, callId);
			if (nodes == null)
				return;

			foreach (var node in nodes.Where(n => !n.DeletedOn.HasValue))
			{
				AddIfSet(userIds, node.PrimaryLeadUserId);
				AddIfSet(userIds, node.SecondaryLeadUserId);
			}
		}

		private async Task<bool> IsCommandStaffAsync(int departmentId, int callId, string userId)
		{
			if (callId <= 0)
				return false;

			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command != null &&
				(string.Equals(command.CurrentCommanderUserId, userId, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(command.EstablishedByUserId, userId, StringComparison.OrdinalIgnoreCase)))
				return true;

			var roles = await _incidentCommandService.GetIncidentRolesAsync(departmentId, callId);
			return roles != null && roles.Any(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase) && !r.RemovedOn.HasValue);
		}

		private static bool MatchesResource(ResourceAssignment assignment, string userId, int? activeUnitId)
		{
			if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel || assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel)
				return string.Equals(assignment.ResourceId, userId, StringComparison.OrdinalIgnoreCase);

			if (activeUnitId.HasValue && (assignment.ResourceKind == (int)ResourceAssignmentKind.RealUnit || assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit))
				return assignment.ResourceId == activeUnitId.Value.ToString();

			return false;
		}

		private async Task AddExplicitMemberAudienceAsync(ChatChannel channel, HashSet<string> userIds)
		{
			var members = await _chatChannelMemberRepository.GetByChannelIdAsync(channel.ChatChannelId);
			if (members == null)
				return;

			foreach (var member in members.Where(m => !m.RemovedOn.HasValue && !m.IsBanned))
			{
				if (member.ParticipantType == (int)ChatParticipantType.User)
					AddIfSet(userIds, member.UserId);
				else if (member.ParticipantType == (int)ChatParticipantType.Unit && member.UnitId.HasValue)
					await AddUnitCrewAsync(member.UnitId.Value, userIds);
			}
		}

		private async Task AddCustomChannelAudienceAsync(ChatChannel channel, HashSet<string> userIds)
		{
			var rules = await _chatChannelAccessRuleRepository.GetByChannelIdAsync(channel.ChatChannelId);
			if (rules == null)
				return;

			foreach (var rule in rules)
			{
				switch ((ChatAccessRuleType)rule.RuleType)
				{
					case ChatAccessRuleType.User:
						AddIfSet(userIds, rule.UserId);
						break;

					case ChatAccessRuleType.GroupMembership:
						if (rule.GroupId.HasValue)
						{
							var members = await _departmentGroupsService.GetAllMembersForGroupAsync(rule.GroupId.Value);
							if (members != null)
								foreach (var m in members)
									AddIfSet(userIds, m.UserId);
						}
						break;

					case ChatAccessRuleType.Role:
						if (rule.PersonnelRoleId.HasValue)
						{
							var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(rule.PersonnelRoleId.Value);
							if (roleMembers != null)
								foreach (var m in roleMembers)
									AddIfSet(userIds, m.UserId);
						}
						break;
				}
			}
		}

		private async Task AddIncidentAudienceAsync(ChatChannel channel, HashSet<string> userIds)
		{
			if (!channel.CallId.HasValue)
				return;

			var callId = channel.CallId.Value;

			await AddCommandStaffAsync(channel.DepartmentId, callId, userIds);

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call != null)
			{
				if (call.Dispatches != null)
					foreach (var dispatch in call.Dispatches)
						AddIfSet(userIds, dispatch.UserId);

				if (call.GroupDispatches != null)
				{
					foreach (var groupDispatch in call.GroupDispatches)
					{
						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupDispatch.DepartmentGroupId);
						if (members != null)
							foreach (var m in members)
								AddIfSet(userIds, m.UserId);
					}
				}

				if (call.RoleDispatches != null)
				{
					foreach (var roleDispatch in call.RoleDispatches)
					{
						var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(roleDispatch.RoleId);
						if (roleMembers != null)
							foreach (var m in roleMembers)
								AddIfSet(userIds, m.UserId);
					}
				}

				if (call.UnitDispatches != null)
					foreach (var unitDispatch in call.UnitDispatches)
						await AddUnitCrewAsync(unitDispatch.UnitId, userIds);
			}

			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(channel.DepartmentId, callId);
			if (assignments != null)
				foreach (var assignment in assignments.Where(a => !a.ReleasedOn.HasValue))
					await AddResourceAsync(assignment, userIds);
		}

		private async Task AddLaneAudienceAsync(ChatChannel channel, HashSet<string> userIds)
		{
			if (!channel.CallId.HasValue || string.IsNullOrWhiteSpace(channel.CommandStructureNodeId))
				return;

			var callId = channel.CallId.Value;

			await AddCommandStaffAsync(channel.DepartmentId, callId, userIds);

			var nodes = await _incidentCommandService.GetNodesForCallAsync(channel.DepartmentId, callId);
			var node = nodes?.FirstOrDefault(n => n.CommandStructureNodeId == channel.CommandStructureNodeId);
			if (node != null)
			{
				AddIfSet(userIds, node.SupervisorUserId);
				AddIfSet(userIds, node.PrimaryLeadUserId);
				AddIfSet(userIds, node.SecondaryLeadUserId);
			}

			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(channel.DepartmentId, callId);
			if (assignments != null)
				foreach (var assignment in assignments.Where(a => !a.ReleasedOn.HasValue && a.CommandStructureNodeId == channel.CommandStructureNodeId))
					await AddResourceAsync(assignment, userIds);
		}

		private async Task AddCommandStaffAsync(int departmentId, int callId, HashSet<string> userIds)
		{
			if (callId <= 0)
				return;

			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command != null)
			{
				AddIfSet(userIds, command.CurrentCommanderUserId);
				AddIfSet(userIds, command.EstablishedByUserId);
			}

			var roles = await _incidentCommandService.GetIncidentRolesAsync(departmentId, callId);
			if (roles != null)
				foreach (var role in roles.Where(r => !r.RemovedOn.HasValue))
					AddIfSet(userIds, role.UserId);
		}

		private async Task AddResourceAsync(ResourceAssignment assignment, HashSet<string> userIds)
		{
			if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel || assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel)
			{
				AddIfSet(userIds, assignment.ResourceId);
			}
			else if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealUnit || assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit)
			{
				if (int.TryParse(assignment.ResourceId, out var unitId))
					await AddUnitCrewAsync(unitId, userIds);
			}
		}

		private async Task AddUnitCrewAsync(int unitId, HashSet<string> userIds)
		{
			var activeRoles = await _unitsService.GetActiveRolesForUnitAsync(unitId);
			if (activeRoles != null)
				foreach (var role in activeRoles)
					AddIfSet(userIds, role.UserId);
		}

		public async Task<bool> IsDepartmentAdminAsync(int departmentId, string userId)
		{
			return await _authorizationService.CanUserModifyDepartmentAsync(userId, departmentId);
		}

		private static void AddIfSet(HashSet<string> set, string userId)
		{
			if (!string.IsNullOrWhiteSpace(userId))
				set.Add(userId);
		}

		private async Task<string> GetPermCacheKeyAsync(string channelId, string kind, string userId, int? unitId)
		{
			var version = await _cacheProvider.GetStringAsync(GetVersionKey(channelId));
			return $"chatperm:{channelId}:{version ?? "0"}:{kind}:{userId}:{unitId.GetValueOrDefault()}";
		}

		private static string GetVersionKey(string channelId)
		{
			return $"chatpermver:{channelId}";
		}
	}
}
