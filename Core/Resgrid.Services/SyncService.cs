using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Aggregates the offline shift-start REFERENCE data set (department configuration + a SAFE personnel roster) into
	/// one payload. Read-only. Personnel is projected to <see cref="ReferencePersonnel"/> so no credentials, security
	/// fields, or contact-verification secrets are exposed. The live per-incident state is delivered separately by the
	/// incident-command bundle (/Sync/Bundle) and delta (/Sync/Changes) endpoints.
	/// </summary>
	public class SyncService : ISyncService
	{
		private readonly ICallsService _callsService;
		private readonly ICommandsService _commandsService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IMappingService _mappingService;
		private readonly IProtocolsService _protocolsService;
		private readonly ICheckInTimerService _checkInTimerService;
		private readonly ICustomStateService _customStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly ICacheProvider _cacheProvider;

		private static readonly string CacheKey = "SyncReferenceData_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromMinutes(5);

		public SyncService(
			ICallsService callsService,
			ICommandsService commandsService,
			IUnitsService unitsService,
			IDepartmentGroupsService departmentGroupsService,
			IMappingService mappingService,
			IProtocolsService protocolsService,
			ICheckInTimerService checkInTimerService,
			ICustomStateService customStateService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IFeatureToggleService featureToggleService,
			ICacheProvider cacheProvider)
		{
			_callsService = callsService;
			_commandsService = commandsService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
			_mappingService = mappingService;
			_protocolsService = protocolsService;
			_checkInTimerService = checkInTimerService;
			_customStateService = customStateService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_featureToggleService = featureToggleService;
			_cacheProvider = cacheProvider;
		}

		public async Task<SyncReferenceData> GetReferenceDataAsync(int departmentId, bool bypassCache = false)
		{
			async Task<SyncReferenceData> build()
			{
				var data = new SyncReferenceData { ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

				// Configuration / reference entities returned as-is (audited: no secret scalar fields, no IdentityUser navs).
				data.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>();
				data.CallPriorities = await _callsService.GetCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
				data.CommandTemplates = await _commandsService.GetAllCommandsForDepartmentAsync(departmentId) ?? new List<CommandDefinition>();
				data.Units = await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>();
				data.UnitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>();
				data.Pois = await _mappingService.GetPOIsForDepartmentAsync(departmentId) ?? new List<Poi>();
				data.PoiTypes = await _mappingService.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>();
				data.Protocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>();
				data.CheckInTimerConfigs = await _checkInTimerService.GetTimerConfigsForDepartmentAsync(departmentId) ?? new List<CheckInTimerConfig>();
				data.PersonnelStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId) ?? new List<CustomState>();
				data.UnitStates = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(departmentId) ?? new List<CustomState>();
				data.Features = await _featureToggleService.EvaluateAllForDepartmentAsync(departmentId) ?? new List<FeatureFlagEvaluation>();

				// Groups: project to a safe shape. The raw DepartmentGroup.Members carry IdentityUser navs we must not leak,
				// and mutating the (possibly cached) entities to strip them would be unsafe.
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>();
				data.Groups = groups.Select(g => new ReferenceGroup
				{
					GroupId = g.DepartmentGroupId,
					Name = g.Name,
					Type = g.Type,
					ParentGroupId = g.ParentDepartmentGroupId
				}).ToList();

				data.Personnel = await BuildPersonnelAsync(departmentId, groups);

				return data;
			}

			if (bypassCache || !Config.SystemBehaviorConfig.CacheEnabled)
				return await build();

			// Cache-aside, department-scoped. The cache provider serializes via protobuf-net and SyncReferenceData's
			// contained entities are mostly not [ProtoContract], so the payload is cached as a JSON snapshot inside a
			// protobuf-safe envelope rather than ProtoContract-tagging ~8 shared entities (see ReferenceCacheEnvelope).
			var envelope = await _cacheProvider.RetrieveAsync<ReferenceCacheEnvelope>(
				string.Format(CacheKey, departmentId),
				async () => new ReferenceCacheEnvelope { Json = JsonConvert.SerializeObject(await build()) },
				CacheLength);

			return !string.IsNullOrEmpty(envelope?.Json)
				? JsonConvert.DeserializeObject<SyncReferenceData>(envelope.Json)
				: await build();
		}

		/// <summary>
		/// Builds the SAFE personnel roster (name + mobile, primary group, current state) projected from UserProfile +
		/// UserState — never exposing the IdentityUser nav, password/security fields, or the UserProfile contact-
		/// verification codes / CalendarSyncToken. Mirrors the field exposure of the existing v4 PersonnelInfoResultData.
		/// </summary>
		private async Task<List<ReferencePersonnel>> BuildPersonnelAsync(int departmentId, List<DepartmentGroup> groups)
		{
			var personnel = new List<ReferencePersonnel>();

			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);
			if (profiles == null || profiles.Count == 0)
				return personnel;

			// First group membership wins as the member's "primary" group.
			var userGroup = new Dictionary<string, ReferenceGroup>();
			foreach (var g in groups)
			{
				if (g.Members == null)
					continue;

				foreach (var m in g.Members)
				{
					if (!string.IsNullOrWhiteSpace(m.UserId) && !userGroup.ContainsKey(m.UserId))
						userGroup[m.UserId] = new ReferenceGroup { GroupId = g.DepartmentGroupId, Name = g.Name };
				}
			}

			var states = await _userStateService.GetStatesForDepartmentAsync(departmentId) ?? new List<UserState>();
			var stateByUser = states
				.Where(s => !string.IsNullOrWhiteSpace(s.UserId))
				.GroupBy(s => s.UserId)
				.ToDictionary(grp => grp.Key, grp => grp.OrderByDescending(s => s.Timestamp).First());

			foreach (var profile in profiles.Values)
			{
				if (profile == null || string.IsNullOrWhiteSpace(profile.UserId))
					continue;

				var person = new ReferencePersonnel
				{
					UserId = profile.UserId,
					FirstName = profile.FirstName,
					LastName = profile.LastName,
					MobilePhone = profile.MobileNumber
				};

				if (userGroup.TryGetValue(profile.UserId, out var grp))
				{
					person.GroupId = grp.GroupId;
					person.GroupName = grp.Name;
				}

				if (stateByUser.TryGetValue(profile.UserId, out var state))
				{
					person.StateId = state.State;
					person.StateTimestamp = state.Timestamp;
				}

				personnel.Add(person);
			}

			return personnel;
		}
	}
}
