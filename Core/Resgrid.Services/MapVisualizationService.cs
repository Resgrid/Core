using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Map visualizations: saved definitions (MapVisualizations, M0288) and running a definition into map layers. A run loads
	/// only what the definition uses — a call heat map reads one thin call query; unit times, dispatches, statuses and location
	/// history are read only when a layer, filter or metric needs them — then hands the facts to
	/// <see cref="MapVisualizationBuilder"/>.
	/// </summary>
	public class MapVisualizationService : IMapVisualizationService
	{
		/// <summary>Longest window read for status-location layers (status history is per unit, so a five-year read is expensive).</summary>
		public const int MaxStatusWindowDays = 366;

		/// <summary>Calls handed to the attribution walk at once: it is linear per chunk, so long windows stay linear overall.</summary>
		private const int AttributionChunkCalls = 2500;
		private const int AttributionChunkDays = 366;

		private static readonly HashSet<ActionBaseTypes> EngagedPersonnelBaseTypes = new HashSet<ActionBaseTypes>
		{
			ActionBaseTypes.Responding, ActionBaseTypes.OnScene, ActionBaseTypes.MadeContact, ActionBaseTypes.Investigating,
			ActionBaseTypes.Enroute, ActionBaseTypes.Transporting, ActionBaseTypes.Delivering, ActionBaseTypes.AtPatient,
			ActionBaseTypes.AtHospital, ActionBaseTypes.Searching, ActionBaseTypes.Staging, ActionBaseTypes.Loading
		};

		private static readonly HashSet<ActionBaseTypes> RespondingBaseTypes = new HashSet<ActionBaseTypes>
		{
			ActionBaseTypes.Responding, ActionBaseTypes.Enroute
		};

		private readonly IMapVisualizationsRepository _mapVisualizationsRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly ICallGroupAssignmentService _callGroupAssignmentService;
		private readonly ICallStatusAttributionService _callStatusAttributionService;
		private readonly IUnitsService _unitsService;
		private readonly ICallDispatchesRepository _callDispatchesRepository;
		private readonly ICallDispatchGroupRepository _callDispatchGroupRepository;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IUnitLocationsDocRepository _unitLocationsDocRepository;
		private readonly IPersonnelLocationsDocRepository _personnelLocationsDocRepository;
		private readonly Lazy<IMongoRepository<UnitsLocation>> _unitLocationsMongoRepository;
		private readonly Lazy<IMongoRepository<PersonnelLocation>> _personnelLocationsMongoRepository;

		public MapVisualizationService(IMapVisualizationsRepository mapVisualizationsRepository, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, ICallsService callsService, IDispatchScopeService dispatchScopeService,
			ICallGroupAssignmentService callGroupAssignmentService, ICallStatusAttributionService callStatusAttributionService,
			IUnitsService unitsService, ICallDispatchesRepository callDispatchesRepository, ICallDispatchGroupRepository callDispatchGroupRepository,
			ICallDispatchUnitRepository callDispatchUnitRepository, ICustomStateService customStateService, IDepartmentGroupsService departmentGroupsService, IUserProfileService userProfileService,
			IAuthorizationService authorizationService, IGeoLocationProvider geoLocationProvider,
			IUnitLocationsDocRepository unitLocationsDocRepository, IPersonnelLocationsDocRepository personnelLocationsDocRepository,
			Lazy<IMongoRepository<UnitsLocation>> unitLocationsMongoRepository, Lazy<IMongoRepository<PersonnelLocation>> personnelLocationsMongoRepository)
		{
			_mapVisualizationsRepository = mapVisualizationsRepository;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
			_callGroupAssignmentService = callGroupAssignmentService;
			_callStatusAttributionService = callStatusAttributionService;
			_unitsService = unitsService;
			_callDispatchesRepository = callDispatchesRepository;
			_callDispatchGroupRepository = callDispatchGroupRepository;
			_callDispatchUnitRepository = callDispatchUnitRepository;
			_customStateService = customStateService;
			_departmentGroupsService = departmentGroupsService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
			_geoLocationProvider = geoLocationProvider;
			_unitLocationsDocRepository = unitLocationsDocRepository;
			_personnelLocationsDocRepository = personnelLocationsDocRepository;
			_unitLocationsMongoRepository = unitLocationsMongoRepository;
			_personnelLocationsMongoRepository = personnelLocationsMongoRepository;
		}

		#region Saved visualizations
		public async Task<List<MapVisualization>> GetVisibleForUserAsync(int departmentId, string userId)
		{
			var all = await _mapVisualizationsRepository.GetForDepartmentAsync(departmentId) ?? new List<MapVisualization>();

			return all.Where(v => v.DepartmentId == departmentId && v.IsVisibleTo(userId))
				.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
		}

		public async Task<MapVisualization> GetVisibleByIdAsync(int departmentId, string userId, string mapVisualizationId)
		{
			if (string.IsNullOrWhiteSpace(mapVisualizationId))
				return null;

			var visualization = await _mapVisualizationsRepository.GetByIdForDepartmentAsync(mapVisualizationId.Trim(), departmentId);

			return visualization != null && visualization.DepartmentId == departmentId && visualization.IsVisibleTo(userId) ? visualization : null;
		}

		public async Task<MapVisualizationSaveResult> SaveAsync(int departmentId, string userId, bool isDepartmentAdmin, MapVisualizationSaveRequest request,
			CancellationToken cancellationToken = default)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Name))
				return new MapVisualizationSaveResult { Error = MapVisualizationSaveErrors.NameRequired };

			// Sharing makes it department configuration, which is a department admin's call.
			if (request.IsShared && !isDepartmentAdmin)
				return new MapVisualizationSaveResult { Error = MapVisualizationSaveErrors.NotAllowed };

			var definition = MapVisualizationDefinitionValidator.Normalize(request.Definition);
			var name = Truncate(request.Name.Trim(), MapVisualizationLimits.MaxNameLength);
			var description = string.IsNullOrWhiteSpace(request.Description) ? null : Truncate(request.Description.Trim(), MapVisualizationLimits.MaxDescriptionLength);
			var templateKey = MapVisualizationLibrary.Find(request.TemplateKey)?.Key;
			var now = DateTime.UtcNow;

			MapVisualization visualization;
			var isNew = string.IsNullOrWhiteSpace(request.MapVisualizationId);
			if (isNew)
			{
				var existing = await _mapVisualizationsRepository.GetForDepartmentAsync(departmentId) ?? new List<MapVisualization>();

				// A member's own list has its own cap, so one member cannot use up the department's room for shared ones.
				if (existing.Count >= MapVisualizationLimits.MaxSavedPerDepartment ||
					(!request.IsShared && existing.Count(v => !v.IsShared && v.IsOwnedBy(userId)) >= MapVisualizationLimits.MaxPersonalPerUser))
					return new MapVisualizationSaveResult { Error = MapVisualizationSaveErrors.LimitReached };

				visualization = new MapVisualization
				{
					MapVisualizationId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					CreatedByUserId = userId,
					CreatedOn = now
				};
			}
			else
			{
				visualization = await _mapVisualizationsRepository.GetByIdForDepartmentAsync(request.MapVisualizationId.Trim(), departmentId);

				if (visualization == null || visualization.DepartmentId != departmentId || !visualization.IsVisibleTo(userId))
					return new MapVisualizationSaveResult { Error = MapVisualizationSaveErrors.NotFound };

				if (!visualization.CanBeEditedBy(userId, isDepartmentAdmin))
					return new MapVisualizationSaveResult { Error = MapVisualizationSaveErrors.NotAllowed };
			}

			visualization.Name = name;
			visualization.Description = description;
			visualization.IsShared = request.IsShared;
			visualization.TemplateKey = templateKey ?? visualization.TemplateKey;
			visualization.Definition = definition;
			visualization.UpdatedByUserId = userId;
			visualization.UpdatedOn = now;

			if (visualization.DefinitionJson.Length > MapVisualizationLimits.MaxDefinitionLength)
				return new MapVisualizationSaveResult { Error = MapVisualizationSaveErrors.DefinitionTooLarge };

			if (isNew)
				await _mapVisualizationsRepository.InsertAsync(visualization, cancellationToken, true);
			else
				await _mapVisualizationsRepository.UpdateAsync(visualization, cancellationToken, true);

			return new MapVisualizationSaveResult { Saved = visualization };
		}

		public async Task<bool> DeleteAsync(int departmentId, string userId, bool isDepartmentAdmin, string mapVisualizationId, CancellationToken cancellationToken = default)
		{
			var visualization = await GetVisibleByIdAsync(departmentId, userId, mapVisualizationId);

			if (visualization == null || !visualization.CanBeEditedBy(userId, isDepartmentAdmin))
				return false;

			return await _mapVisualizationsRepository.DeleteAsync(visualization, cancellationToken);
		}
		#endregion Saved visualizations

		public async Task<List<Call>> SearchCallsAsync(int departmentId, string userId, string term, int max, CancellationToken cancellationToken = default)
		{
			var endUtc = DateTime.UtcNow.AddMinutes(1);
			var needle = term?.Trim() ?? string.Empty;
			if (needle.Length < 2)
				return new List<Call>();

			var calls = await _mapVisualizationsRepository.GetCallsForMapAsync(departmentId, endUtc.AddDays(-365), endUtc, 5000, cancellationToken: cancellationToken)
				?? new List<Call>();

			calls = calls.Where(c => Contains(c.Number, needle) || Contains(c.Name, needle) || Contains(c.Type, needle) || Contains(c.Address, needle) ||
				string.Equals(c.CallId.ToString(CultureInfo.InvariantCulture), needle, StringComparison.Ordinal))
				.Take(200)
				.ToList();

			calls = await ScopeCallsAsync(departmentId, userId, calls);

			return calls.Take(Math.Max(1, max)).ToList();
		}

		#region Run
		public async Task<MapVisualizationResult> RunAsync(int departmentId, string userId, MapVisualizationDefinition definition, CancellationToken cancellationToken = default)
		{
			definition = MapVisualizationDefinitionValidator.Normalize(definition);

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var nowUtc = DateTime.UtcNow;
			var (startUtc, endUtc) = MapVisualizationDefinitionValidator.ResolveWindow(definition.Filters, nowUtc, local => department != null ? local.DepartmentLocalToUtc(department) : local);
			DateTime ToLocal(DateTime utc) => department != null ? utc.TimeConverter(department) : utc;

			var context = new MapVisualizationContext
			{
				Definition = definition,
				StartUtc = startUtc,
				EndUtc = endUtc,
				StartLocal = ToLocal(startUtc),
				EndLocal = ToLocal(endUtc.AddSeconds(-1))
			};
			var layers = definition.Layers;
			var filters = definition.Filters;
			var options = definition.Options;
			bool Uses(string source) => layers.Any(l => l.Source == source);
			bool AnyColorBy(string key) => layers.Any(l => l.ColorBy == key);
			bool AnyMetric(string key) => layers.Any(l => l.Metric == key);

			var needUnitTimes = layers.Any(MapVisualizationBuilder.NeedsUnitTimes);
			var needUnitDispatches = needUnitTimes || Uses(MapVisualizationKeys.SourceUnitResponses) || AnyMetric(MapVisualizationKeys.MetricUnitsDispatched) || filters.UnitIds.Count > 0;
			var needResponders = Uses(MapVisualizationKeys.SourcePersonnelResponses) || AnyMetric(MapVisualizationKeys.MetricPersonnelResponded);
			var needGroups = filters.GroupIds.Count > 0 || AnyColorBy(MapVisualizationKeys.ColorByGroup);
			var needDistance = AnyMetric(MapVisualizationKeys.MetricStationDistance) || AnyColorBy(MapVisualizationKeys.ColorByDistanceBand);
			var needStations = options.ShowStations || options.StationRings.Count > 0 || needDistance;
			var metersToUnit = options.DistanceUnit == MapVisualizationKeys.DistanceMiles ? 1 / 1609.344 : 1 / 1000d;

			// Lookups for labels and visibility.
			var units = await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(departmentId) ?? new List<Unit>();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(departmentId) ?? new List<DepartmentGroup>();
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(departmentId) ?? new Dictionary<string, UserProfile>();
			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId) ?? new List<CustomState>();

			FillLookups(context.Lookups, units, groups, priorities, profiles, customStates);

			var unitBaseTypes = CallStatusLinkage.BuildUnitBaseTypeMap(customStates);
			var personnelBaseTypes = CallStatusLinkage.BuildBaseTypeMap(customStates, CustomStateTypes.Personnel);

			var visibility = new VisibilityCache(_authorizationService, departmentId, userId);

			// A unit the viewer may not see cannot be used to pick calls either.
			if (filters.UnitIds.Count > 0)
			{
				var visibleUnitIds = new List<int>();
				foreach (var unitId in filters.UnitIds)
					if (await visibility.CanViewUnitAsync(unitId))
						visibleUnitIds.Add(unitId);
				filters.UnitIds = visibleUnitIds;
			}

			// Stations.
			var stations = needStations ? await LoadStationsAsync(groups) : new List<MapVisualizationStation>();
			if (options.ShowStations || options.StationRings.Count > 0)
				context.Stations = stations;

			if (needDistance && stations.Count == 0)
				foreach (var source in MapVisualizationKeys.Sources)
					context.AddNotice(source, MapVisualizationNotices.NoStationLocations);

			double? NearestStation(double latitude, double longitude) => stations.Count == 0
				? (double?)null
				: Math.Round(stations.Min(s => GeoMath.HaversineMeters(latitude, longitude, s.Latitude, s.Longitude)) * metersToUnit, 2);

			var stationsById = stations.ToDictionary(s => s.GroupId);

			await SetCenterAsync(context, departmentId);

			// Calls.
			Call focusCall = null;
			List<Call> calls;
			if (filters.CallId.HasValue)
			{
				focusCall = await _callsService.GetCallByIdAsync(filters.CallId.Value);
				if (focusCall == null || focusCall.DepartmentId != departmentId || focusCall.IsDeleted ||
					!await _dispatchScopeService.CanUserAccessCallAsync(departmentId, userId, focusCall))
				{
					focusCall = null;
					calls = new List<Call>();
					foreach (var source in MapVisualizationKeys.Sources)
						context.AddNotice(source, MapVisualizationNotices.CallNotFound);
				}
				else
				{
					calls = new List<Call> { focusCall };
					context.Call = ToFocus(focusCall);
				}
			}
			else
			{
				var read = await _mapVisualizationsRepository.GetCallsForMapAsync(departmentId, startUtc, endUtc, MapVisualizationLimits.MaxCalls + 1,
					filters.CallTypes, filters.Priorities, filters.Outcomes, cancellationToken) ?? new List<Call>();

				if (read.Count > MapVisualizationLimits.MaxCalls)
				{
					context.CallsTruncated = true;
					read = read.Take(MapVisualizationLimits.MaxCalls).ToList();
				}

				calls = ApplyCallFilters(read, filters, ToLocal);
				calls = await ScopeCallsAsync(departmentId, userId, calls);
			}

			cancellationToken.ThrowIfCancellationRequested();

			// Home groups.
			var homeGroups = new Dictionary<int, int?>();
			if (needGroups && calls.Count > 0)
			{
				var assignments = await _callGroupAssignmentService.AssignCallsToGroupsAsync(departmentId, calls) ?? new Dictionary<int, CallGroupAssignment>();
				foreach (var call in calls)
					homeGroups[call.CallId] = assignments.TryGetValue(call.CallId, out var assignment) ? assignment?.DepartmentGroupId : null;

				if (filters.GroupIds.Count > 0)
					calls = calls.Where(c => homeGroups.TryGetValue(c.CallId, out var groupId) && groupId.HasValue && filters.GroupIds.Contains(groupId.Value)).ToList();
			}

			// Units on calls.
			var dispatchesByCall = needUnitDispatches && calls.Count > 0
				? await _callStatusAttributionService.GetUnitDispatchesForCallsAsync(departmentId, calls) ?? new Dictionary<int, List<CallDispatchUnit>>()
				: new Dictionary<int, List<CallDispatchUnit>>();

			var needUnitStates = needUnitTimes || (focusCall != null && (Uses(MapVisualizationKeys.SourceUnitStatusLocations) || Uses(MapVisualizationKeys.SourceUnitTracks)));
			var statesByCall = needUnitStates && calls.Count > 0
				? await ChunkedAsync(calls, chunk => _callStatusAttributionService.GetUnitStatesForCallsAsync(departmentId, chunk))
				: new Dictionary<int, List<UnitState>>();
			context.UnitTimesLoaded = needUnitTimes;

			if (filters.UnitIds.Count > 0 && focusCall == null)
				calls = calls.Where(c =>
					(dispatchesByCall.TryGetValue(c.CallId, out var dispatches) && dispatches.Any(d => filters.UnitIds.Contains(d.UnitId))) ||
					(statesByCall.TryGetValue(c.CallId, out var states) && states.Any(s => filters.UnitIds.Contains(s.UnitId)))).ToList();

			// People on calls.
			var needLogs = needResponders || (focusCall != null && (Uses(MapVisualizationKeys.SourceResponderOrigins) || Uses(MapVisualizationKeys.SourcePersonnelTracks)));
			var logsByCall = needLogs && calls.Count > 0
				? await ChunkedAsync(calls, chunk => _callStatusAttributionService.GetActionLogsForCallsAsync(departmentId, chunk))
				: new Dictionary<int, List<ActionLog>>();

			cancellationToken.ThrowIfCancellationRequested();

			// Call facts.
			var facts = new List<MapVisualizationCallFact>(calls.Count);
			foreach (var call in calls)
			{
				var point = CallLocation(call);
				// A scheduled call's clock starts when it was dispatched, not when it was entered.
				var clockStart = call.DispatchOn.HasValue && call.DispatchOn.Value > call.LoggedOn ? call.DispatchOn.Value : call.LoggedOn;
				var fact = new MapVisualizationCallFact
				{
					CallId = call.CallId,
					Type = call.Type,
					Priority = call.Priority,
					State = call.State,
					Latitude = point?.Latitude,
					Longitude = point?.Longitude,
					LoggedOnUtc = call.LoggedOn,
					LoggedOnLocal = ToLocal(call.LoggedOn),
					HomeGroupId = homeGroups.TryGetValue(call.CallId, out var homeGroupId) ? homeGroupId : null,
					DurationMinutes = call.ClosedOn.HasValue && call.State != (int)CallStates.Active ? Minutes(call.LoggedOn, call.ClosedOn.Value, 14 * 1440) : null,
					StationDistance = point.HasValue ? NearestStation(point.Value.Latitude, point.Value.Longitude) : null
				};

				dispatchesByCall.TryGetValue(call.CallId, out var dispatches);
				statesByCall.TryGetValue(call.CallId, out var states);
				dispatches ??= new List<CallDispatchUnit>();
				fact.UnitsDispatched = dispatches.Select(d => d.UnitId).Distinct().Count();

				var unitTimes = needUnitStates
					? CallUnitTimesCalculator.Compute(dispatches, states ?? new List<UnitState>(), unitBaseTypes)
					: dispatches.Select(d => d.UnitId).Distinct().Select(id => new CallUnitTimesRow { UnitId = id }).ToList();

				foreach (var row in unitTimes)
				{
					if (!await visibility.CanViewUnitAsync(row.UnitId))
						continue;

					var unitResponse = new MapVisualizationUnitResponseFact
					{
						UnitId = row.UnitId,
						ResponseMinutes = row.OnSceneOn.HasValue ? Minutes(clockStart, row.OnSceneOn.Value, 1440) : null,
						TurnoutMinutes = row.EnrouteOn.HasValue ? Minutes(clockStart, row.EnrouteOn.Value, 1440) : null
					};

					if (point.HasValue)
					{
						var stationId = context.Lookups.Units.TryGetValue(row.UnitId, out var unit) ? unit.StationGroupId : null;
						unitResponse.StationDistance = stationId.HasValue && stationsById.TryGetValue(stationId.Value, out var station)
							? Math.Round(GeoMath.HaversineMeters(point.Value.Latitude, point.Value.Longitude, station.Latitude, station.Longitude) * metersToUnit, 2)
							: fact.StationDistance;
					}

					fact.Units.Add(unitResponse);
				}

				// The call's times use every unit, seen or not: hiding a unit from the viewer must not change the call's response time.
				var onScene = unitTimes.Where(r => r.OnSceneOn.HasValue).Select(r => Minutes(clockStart, r.OnSceneOn.Value, 1440)).Where(m => m.HasValue).ToList();
				var enroute = unitTimes.Where(r => r.EnrouteOn.HasValue).Select(r => Minutes(clockStart, r.EnrouteOn.Value, 1440)).Where(m => m.HasValue).ToList();
				fact.ResponseMinutes = onScene.Count > 0 ? onScene.Min() : null;
				fact.TurnoutMinutes = enroute.Count > 0 ? enroute.Min() : null;

				if (logsByCall.TryGetValue(call.CallId, out var logs))
				{
					var responders = logs
						.Where(l => !string.IsNullOrWhiteSpace(l.UserId) && EngagedPersonnelBaseTypes.Contains(PersonnelBaseType(l.ActionTypeId, personnelBaseTypes)))
						.Select(l => l.UserId)
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToList();

					fact.PersonnelResponded = responders.Count;
					foreach (var responder in responders)
						if (await visibility.CanViewPersonAsync(responder))
							fact.Responders.Add(responder);
				}

				facts.Add(fact);
			}

			// Repeat locations: count calls per ~11 m cell over the filtered set, then apply the minimum.
			var atLocation = facts.Where(f => f.Latitude.HasValue)
				.GroupBy(f => LocationKey(f.Latitude.Value, f.Longitude.Value))
				.ToDictionary(g => g.Key, g => g.Count());
			foreach (var fact in facts.Where(f => f.Latitude.HasValue))
				fact.CallsAtLocation = atLocation[LocationKey(fact.Latitude.Value, fact.Longitude.Value)];

			if (filters.MinCallsAtLocation > 1 && focusCall == null)
				facts = facts.Where(f => f.Latitude.HasValue && f.CallsAtLocation >= filters.MinCallsAtLocation).ToList();

			context.Calls = facts;
			context.SourceWithoutLocation[MapVisualizationKeys.SourceCalls] = facts.Count(f => !f.Latitude.HasValue);

			// Status locations.
			// Status layers follow the same calls: a status linked to a call shows only when that call passed the filters and the
			// viewer's dispatch scope; one linked to no call shows only when no call-level filter is set.
			var callIds = new HashSet<int>(facts.Select(f => f.CallId));
			var keepUnlinked = focusCall == null && filters.CallTypes.Count == 0 && filters.Priorities.Count == 0 && filters.Outcomes.Count == 0 &&
				filters.GroupIds.Count == 0 && filters.MinCallsAtLocation <= 1;
			bool KeepStatus(int? destinationId, int? destinationType)
			{
				var linked = CallStatusLinkage.LinkedCallId(destinationId, destinationType);
				return linked.HasValue ? callIds.Contains(linked.Value) : keepUnlinked;
			}

			if (Uses(MapVisualizationKeys.SourceResponderOrigins))
				await LoadResponderOriginsAsync(context, departmentId, focusCall, logsByCall, personnelBaseTypes, visibility, filters, startUtc, endUtc, ToLocal,
					NearestStation, KeepStatus, cancellationToken);

			if (Uses(MapVisualizationKeys.SourceUnitStatusLocations))
				await LoadUnitStatusLocationsAsync(context, departmentId, focusCall, statesByCall, visibility, filters, startUtc, endUtc, ToLocal, NearestStation,
					KeepStatus, cancellationToken);

			// Tracks.
			if (Uses(MapVisualizationKeys.SourceUnitTracks) || Uses(MapVisualizationKeys.SourcePersonnelTracks))
			{
				var (trackStart, trackEnd, shortened) = TrackWindow(focusCall, startUtc, endUtc, nowUtc);

				if (Uses(MapVisualizationKeys.SourceUnitTracks))
				{
					if (shortened)
						context.AddNotice(MapVisualizationKeys.SourceUnitTracks, MapVisualizationNotices.TrackWindowShortened);

					var unitIds = focusCall != null
						? facts.SelectMany(f => f.Units.Select(u => u.UnitId)).Concat(statesByCall.Values.SelectMany(s => s.Select(x => x.UnitId))).Distinct().ToList()
						: filters.UnitIds.Count > 0 ? filters.UnitIds : null;

					context.UnitTracks = await LoadUnitTracksAsync(context, departmentId, unitIds, trackStart, trackEnd, visibility, cancellationToken);
				}

				if (Uses(MapVisualizationKeys.SourcePersonnelTracks))
				{
					if (shortened)
						context.AddNotice(MapVisualizationKeys.SourcePersonnelTracks, MapVisualizationNotices.TrackWindowShortened);

					var userIds = focusCall != null
						? logsByCall.Values.SelectMany(l => l.Select(x => x.UserId)).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
						: null;

					context.PersonnelTracks = focusCall != null && userIds.Count == 0
						? new List<MapVisualizationTrackFact>()
						: await LoadPersonnelTracksAsync(context, departmentId, userIds, trackStart, trackEnd, visibility, cancellationToken);
				}
			}

			return MapVisualizationBuilder.Build(context);
		}

		private static List<Call> ApplyCallFilters(List<Call> calls, MapVisualizationFilters filters, Func<DateTime, DateTime> toLocal)
		{
			IEnumerable<Call> query = calls;

			query = filters.Outcomes.Count > 0
				? query.Where(c => filters.Outcomes.Contains(c.State))
				: query.Where(c => c.State != (int)CallStates.Pending);

			if (filters.CallTypes.Count > 0)
			{
				var types = new HashSet<string>(filters.CallTypes, StringComparer.OrdinalIgnoreCase);
				query = query.Where(c => !string.IsNullOrWhiteSpace(c.Type) && types.Contains(c.Type.Trim()));
			}

			if (filters.Priorities.Count > 0)
				query = query.Where(c => filters.Priorities.Contains(c.Priority));

			if (filters.Hours.Count > 0 || filters.DaysOfWeek.Count > 0)
				query = query.Where(c =>
				{
					var local = toLocal(c.LoggedOn);
					return (filters.Hours.Count == 0 || filters.Hours.Contains(local.Hour)) &&
						(filters.DaysOfWeek.Count == 0 || filters.DaysOfWeek.Contains((int)local.DayOfWeek));
				});

			return query.ToList();
		}

		private static bool PassesTimeFilters(MapVisualizationFilters filters, DateTime local) =>
			(filters.Hours.Count == 0 || filters.Hours.Contains(local.Hour)) &&
			(filters.DaysOfWeek.Count == 0 || filters.DaysOfWeek.Contains((int)local.DayOfWeek));

		private async Task LoadResponderOriginsAsync(MapVisualizationContext context, int departmentId, Call focusCall,
			Dictionary<int, List<ActionLog>> logsByCall, Dictionary<int, int> personnelBaseTypes, VisibilityCache visibility,
			MapVisualizationFilters filters, DateTime startUtc, DateTime endUtc, Func<DateTime, DateTime> toLocal, Func<double, double, double?> nearestStation,
			Func<int?, int?, bool> keepStatus, CancellationToken cancellationToken)
		{
			List<ActionLog> logs;
			if (focusCall != null)
			{
				logs = logsByCall.TryGetValue(focusCall.CallId, out var callLogs) ? callLogs : new List<ActionLog>();
			}
			else
			{
				var readStart = StatusWindowStart(startUtc, endUtc, context, MapVisualizationKeys.SourceResponderOrigins);
				logs = await _mapVisualizationsRepository.GetActionLogsWithLocationForMapAsync(departmentId, readStart, endUtc, MapVisualizationLimits.MaxPointsPerLayer + 1,
					cancellationToken) ?? new List<ActionLog>();

				if (logs.Count > MapVisualizationLimits.MaxPointsPerLayer)
					context.AddNotice(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationNotices.LayerTruncated);
			}

			var hidden = false;
			var withoutLocation = 0;
			foreach (var log in logs.OrderByDescending(l => l.Timestamp))
			{
				if (string.IsNullOrWhiteSpace(log.UserId) || !RespondingBaseTypes.Contains(PersonnelBaseType(log.ActionTypeId, personnelBaseTypes)))
					continue;

				var local = toLocal(log.Timestamp);
				if (focusCall == null && (!PassesTimeFilters(filters, local) || !keepStatus(log.DestinationId, log.DestinationType)))
					continue;

				var point = ProtectedDataEnvelope.HasEnvelopePrefix(log.GeoLocationData) ? null : GeoMath.ParseLatLonString(log.GeoLocationData);
				if (!point.HasValue)
				{
					withoutLocation++;
					continue;
				}

				if (!await visibility.CanViewPersonLocationAsync(log.UserId))
				{
					hidden = true;
					continue;
				}

				context.ResponderOrigins.Add(new MapVisualizationStatusFact
				{
					UserId = log.UserId,
					Status = log.ActionTypeId,
					TimestampLocal = local,
					Latitude = point.Value.Latitude,
					Longitude = point.Value.Longitude,
					CallId = CallStatusLinkage.LinkedCallId(log.DestinationId, log.DestinationType) ?? 0,
					StationDistance = nearestStation(point.Value.Latitude, point.Value.Longitude)
				});
			}

			if (hidden)
				context.AddNotice(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationNotices.PersonnelLocationsHidden);

			context.SourceWithoutLocation[MapVisualizationKeys.SourceResponderOrigins] = withoutLocation;
		}

		private async Task LoadUnitStatusLocationsAsync(MapVisualizationContext context, int departmentId, Call focusCall, Dictionary<int, List<UnitState>> statesByCall,
			VisibilityCache visibility, MapVisualizationFilters filters, DateTime startUtc, DateTime endUtc, Func<DateTime, DateTime> toLocal,
			Func<double, double, double?> nearestStation, Func<int?, int?, bool> keepStatus, CancellationToken cancellationToken)
		{
			List<UnitState> states;
			if (focusCall != null)
			{
				states = statesByCall.TryGetValue(focusCall.CallId, out var callStates) ? callStates : new List<UnitState>();
			}
			else
			{
				var readStart = StatusWindowStart(startUtc, endUtc, context, MapVisualizationKeys.SourceUnitStatusLocations);
				states = await _mapVisualizationsRepository.GetUnitStatesWithLocationForMapAsync(departmentId, readStart, endUtc,
					filters.UnitIds.Count > 0 ? filters.UnitIds : null, MapVisualizationLimits.MaxPointsPerLayer + 1, cancellationToken) ?? new List<UnitState>();

				if (states.Count > MapVisualizationLimits.MaxPointsPerLayer)
					context.AddNotice(MapVisualizationKeys.SourceUnitStatusLocations, MapVisualizationNotices.LayerTruncated);
			}

			var hidden = false;
			var withoutLocation = 0;
			foreach (var state in states.Where(s => s != null).OrderByDescending(s => s.Timestamp))
			{
				var local = toLocal(state.Timestamp);
				if (focusCall == null && (!PassesTimeFilters(filters, local) || !keepStatus(state.DestinationId, state.DestinationType)))
					continue;

				var point = UnitStateLocation(state);
				if (!point.HasValue)
				{
					withoutLocation++;
					continue;
				}

				if (!await visibility.CanViewUnitLocationAsync(state.UnitId))
				{
					hidden = true;
					continue;
				}

				context.UnitStatusLocations.Add(new MapVisualizationStatusFact
				{
					UnitId = state.UnitId,
					IsUnit = true,
					Status = state.State,
					TimestampLocal = local,
					Latitude = point.Value.Latitude,
					Longitude = point.Value.Longitude,
					CallId = CallStatusLinkage.LinkedCallId(state.DestinationId, state.DestinationType) ?? 0,
					StationDistance = nearestStation(point.Value.Latitude, point.Value.Longitude)
				});
			}

			if (hidden)
				context.AddNotice(MapVisualizationKeys.SourceUnitStatusLocations, MapVisualizationNotices.UnitLocationsHidden);

			context.SourceWithoutLocation[MapVisualizationKeys.SourceUnitStatusLocations] = withoutLocation;
		}

		/// <summary>
		/// The calls the viewer's dispatch scope allows. A department-wide scope keeps every call. Otherwise each call's
		/// dispatches are filled from three range reads first, so the scope check never falls back to per-call queries.
		/// </summary>
		private async Task<List<Call>> ScopeCallsAsync(int departmentId, string userId, List<Call> calls)
		{
			if (calls == null || calls.Count == 0)
				return new List<Call>();

			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, userId);
			if (scope == null)
				return new List<Call>();

			if (scope.IsDepartmentWide)
				return calls;

			var from = calls.Min(c => c.LoggedOn);
			var to = calls.Max(c => c.LoggedOn);
			var ids = new HashSet<int>(calls.Select(c => c.CallId));

			var dispatches = ((await _callDispatchesRepository.GetCallDispatchesForCallsInRangeAsync(departmentId, from, to)) ?? Enumerable.Empty<CallDispatch>())
				.Where(d => d != null && ids.Contains(d.CallId)).ToLookup(d => d.CallId);
			var groupDispatches = ((await _callDispatchGroupRepository.GetCallDispatchGroupsForCallsInRangeAsync(departmentId, from, to)) ?? Enumerable.Empty<CallDispatchGroup>())
				.Where(d => d != null && ids.Contains(d.CallId)).ToLookup(d => d.CallId);
			var unitDispatches = ((await _callDispatchUnitRepository.GetCallUnitDispatchesForCallsInRangeAsync(departmentId, from, to)) ?? Enumerable.Empty<CallDispatchUnit>())
				.Where(d => d != null && ids.Contains(d.CallId)).ToLookup(d => d.CallId);

			foreach (var call in calls)
			{
				call.Dispatches ??= dispatches[call.CallId].ToList();
				call.GroupDispatches ??= groupDispatches[call.CallId].ToList();
				call.UnitDispatches ??= unitDispatches[call.CallId].ToList();
			}

			return await _dispatchScopeService.FilterCallsAsync(scope, calls) ?? new List<Call>();
		}

		private static DateTime StatusWindowStart(DateTime startUtc, DateTime endUtc, MapVisualizationContext context, string source)
		{
			var earliest = endUtc.AddDays(-MaxStatusWindowDays);
			if (startUtc >= earliest)
				return startUtc;

			context.AddNotice(source, MapVisualizationNotices.TrackWindowShortened);
			return earliest;
		}

		/// <summary>
		/// The span a track layer reads: the focused call's own span (from 15 minutes before it was logged to 15 minutes after it
		/// closed, or now while it is open), else the end of the analysis window; either way the latest
		/// <see cref="MapVisualizationLimits.MaxTrackWindowDays"/> days at most.
		/// </summary>
		public static (DateTime Start, DateTime End, bool Shortened) TrackWindow(Call focusCall, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
		{
			var maxSpan = TimeSpan.FromDays(MapVisualizationLimits.MaxTrackWindowDays);

			if (focusCall != null)
			{
				var start = focusCall.LoggedOn.AddMinutes(-15);
				var end = (focusCall.ClosedOn ?? nowUtc).AddMinutes(15);
				if (end > nowUtc)
					end = nowUtc;

				// A long-running incident (a multi-day search) shows its latest days: where everyone went most recently.
				return end - start > maxSpan ? (end - maxSpan, end, true) : (start, end, false);
			}

			return endUtc - startUtc > maxSpan ? (endUtc - maxSpan, endUtc, true) : (startUtc, endUtc, false);
		}

		private async Task<List<MapVisualizationTrackFact>> LoadUnitTracksAsync(MapVisualizationContext context, int departmentId, IReadOnlyCollection<int> unitIds,
			DateTime startUtc, DateTime endUtc, VisibilityCache visibility, CancellationToken cancellationToken)
		{
			var tracks = new List<MapVisualizationTrackFact>();
			if (unitIds != null && unitIds.Count == 0)
				return tracks;

			List<UnitsLocation> locations;
			try
			{
				locations = await ReadUnitLocationsAsync(departmentId, startUtc, endUtc, unitIds, MapVisualizationLimits.MaxTrackPoints + 1, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "Map visualization: unit location history unavailable");
				return tracks;
			}

			if (locations.Count > MapVisualizationLimits.MaxTrackPoints)
				context.AddNotice(MapVisualizationKeys.SourceUnitTracks, MapVisualizationNotices.LayerTruncated);

			var hidden = false;
			foreach (var group in locations.Where(l => l != null && l.DepartmentId == departmentId).GroupBy(l => l.UnitId))
			{
				if (!await visibility.CanViewUnitLocationAsync(group.Key))
				{
					hidden = true;
					continue;
				}

				var track = new MapVisualizationTrackFact { UnitId = group.Key };
				track.Points.AddRange(group
					.Where(l => IsUsable((double)l.Latitude, (double)l.Longitude))
					.OrderBy(l => l.Timestamp)
					.Select(l => ((double)l.Latitude, (double)l.Longitude, l.Timestamp)));

				if (track.Points.Count > 0)
					tracks.Add(track);
			}

			if (hidden)
				context.AddNotice(MapVisualizationKeys.SourceUnitTracks, MapVisualizationNotices.UnitLocationsHidden);

			return tracks;
		}

		private async Task<List<MapVisualizationTrackFact>> LoadPersonnelTracksAsync(MapVisualizationContext context, int departmentId, IReadOnlyCollection<string> userIds,
			DateTime startUtc, DateTime endUtc, VisibilityCache visibility, CancellationToken cancellationToken)
		{
			var tracks = new List<MapVisualizationTrackFact>();

			List<PersonnelLocation> locations;
			try
			{
				locations = await ReadPersonnelLocationsAsync(departmentId, startUtc, endUtc, userIds, MapVisualizationLimits.MaxTrackPoints + 1, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "Map visualization: personnel location history unavailable");
				return tracks;
			}

			if (locations.Count > MapVisualizationLimits.MaxTrackPoints)
				context.AddNotice(MapVisualizationKeys.SourcePersonnelTracks, MapVisualizationNotices.LayerTruncated);

			var hidden = false;
			foreach (var group in locations.Where(l => l != null && l.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(l.UserId))
				.GroupBy(l => l.UserId, StringComparer.OrdinalIgnoreCase))
			{
				if (!await visibility.CanViewPersonLocationAsync(group.Key))
				{
					hidden = true;
					continue;
				}

				var track = new MapVisualizationTrackFact { UserId = group.Key };
				track.Points.AddRange(group
					.Where(l => IsUsable((double)l.Latitude, (double)l.Longitude))
					.OrderBy(l => l.Timestamp)
					.Select(l => ((double)l.Latitude, (double)l.Longitude, l.Timestamp)));

				if (track.Points.Count > 0)
					tracks.Add(track);
			}

			if (hidden)
				context.AddNotice(MapVisualizationKeys.SourcePersonnelTracks, MapVisualizationNotices.PersonnelLocationsHidden);

			return tracks;
		}

		private async Task<List<UnitsLocation>> ReadUnitLocationsAsync(int departmentId, DateTime startUtc, DateTime endUtc, IReadOnlyCollection<int> unitIds, int max,
			CancellationToken cancellationToken)
		{
			if (Config.DataConfig.DocDatabaseType == Config.DatabaseTypes.Postgres)
				return await _unitLocationsDocRepository.GetLocationsForDepartmentInRangeAsync(departmentId, startUtc, endUtc, unitIds, max, cancellationToken)
					?? new List<UnitsLocation>();

			var ids = unitIds?.ToArray();
			var from = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
			var to = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
			var query = _unitLocationsMongoRepository.Value.AsQueryable()
				.Where(x => x.DepartmentId == departmentId && x.Timestamp >= from && x.Timestamp < to);

			if (ids != null && ids.Length > 0)
				query = query.Where(x => ids.Contains(x.UnitId));

			return query.OrderByDescending(x => x.Timestamp).Take(max).ToList();
		}

		private async Task<List<PersonnelLocation>> ReadPersonnelLocationsAsync(int departmentId, DateTime startUtc, DateTime endUtc, IReadOnlyCollection<string> userIds, int max,
			CancellationToken cancellationToken)
		{
			if (Config.DataConfig.DocDatabaseType == Config.DatabaseTypes.Postgres)
				return await _personnelLocationsDocRepository.GetLocationsForDepartmentInRangeAsync(departmentId, startUtc, endUtc, userIds, max, cancellationToken)
					?? new List<PersonnelLocation>();

			var ids = userIds?.ToArray();
			var from = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
			var to = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
			var query = _personnelLocationsMongoRepository.Value.AsQueryable()
				.Where(x => x.DepartmentId == departmentId && x.Timestamp >= from && x.Timestamp < to);

			if (ids != null && ids.Length > 0)
				query = query.Where(x => ids.Contains(x.UserId));

			return query.OrderByDescending(x => x.Timestamp).Take(max).ToList();
		}

		/// <summary>
		/// Runs an attribution read over calls in slices (by time and count). The attribution walk compares each call's
		/// dispatches with the window's statuses, so one slice at a time keeps a multi-year run linear instead of quadratic.
		/// </summary>
		private static async Task<Dictionary<int, List<T>>> ChunkedAsync<T>(List<Call> calls, Func<IReadOnlyCollection<Call>, Task<Dictionary<int, List<T>>>> read)
		{
			var result = new Dictionary<int, List<T>>();
			foreach (var chunk in Chunk(calls))
			{
				var part = await read(chunk);
				if (part == null)
					continue;

				foreach (var pair in part)
					result[pair.Key] = pair.Value ?? new List<T>();
			}

			return result;
		}

		public static IEnumerable<List<Call>> Chunk(IEnumerable<Call> calls)
		{
			var current = new List<Call>();
			DateTime? chunkStart = null;

			foreach (var call in calls.OrderBy(c => c.LoggedOn))
			{
				if (current.Count >= AttributionChunkCalls || (chunkStart.HasValue && (call.LoggedOn - chunkStart.Value).TotalDays > AttributionChunkDays))
				{
					yield return current;
					current = new List<Call>();
					chunkStart = null;
				}

				chunkStart ??= call.LoggedOn;
				current.Add(call);
			}

			if (current.Count > 0)
				yield return current;
		}

		private async Task<List<MapVisualizationStation>> LoadStationsAsync(List<DepartmentGroup> groups)
		{
			var stations = new List<MapVisualizationStation>();

			foreach (var group in groups.Where(g => g != null && g.Type == (int)DepartmentGroupTypes.Station))
			{
				var point = GeoMath.ParseCoordinatePair(group.Latitude, group.Longitude);

				if (!point.HasValue && group.Address != null && !string.IsNullOrWhiteSpace(group.Address.Address1))
				{
					try
					{
						var geocoded = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", group.Address.Address1,
							group.Address.City, group.Address.State, group.Address.PostalCode, group.Address.Country));
						point = GeoMath.ParseLatLonString(geocoded);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, "Map visualization: station address could not be geocoded");
					}
				}

				if (point.HasValue)
					stations.Add(new MapVisualizationStation
					{
						GroupId = group.DepartmentGroupId,
						Name = group.Name,
						Latitude = point.Value.Latitude,
						Longitude = point.Value.Longitude
					});
			}

			return stations;
		}

		private async Task SetCenterAsync(MapVisualizationContext context, int departmentId)
		{
			try
			{
				var center = GeoMath.ParseLatLonString(await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(departmentId));
				if (center.HasValue)
				{
					context.CenterLatitude = center.Value.Latitude;
					context.CenterLongitude = center.Value.Longitude;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Map visualization: department map center unavailable");
			}
		}

		private static void FillLookups(MapVisualizationLookups lookups, List<Unit> units, List<DepartmentGroup> groups, List<DepartmentCallPriority> priorities,
			Dictionary<string, UserProfile> profiles, List<CustomState> customStates)
		{
			foreach (var priority in priorities.Where(p => p != null))
				lookups.Priorities[priority.DepartmentCallPriorityId] = (priority.Name, priority.Color);

			foreach (var group in groups.Where(g => g != null))
				lookups.Groups[group.DepartmentGroupId] = (group.Name, group.GeofenceColor);

			foreach (var unit in units.Where(u => u != null))
				lookups.Units[unit.UnitId] = (unit.Name, unit.Type, unit.StationGroupId);

			foreach (var profile in profiles)
				if (profile.Value != null)
					lookups.People[profile.Key] = profile.Value.FullName.AsFirstNameLastName;

			foreach (var detail in customStates.Where(s => s?.Details != null).SelectMany(s => s.Details).Where(d => d != null && d.CustomStateDetailId > 0))
				lookups.CustomStatuses[detail.CustomStateDetailId] = (detail.ButtonText, detail.ButtonColor);
		}

		private static ActionBaseTypes PersonnelBaseType(int actionTypeId, IReadOnlyDictionary<int, int> customBaseTypes) =>
			customBaseTypes != null && customBaseTypes.TryGetValue(actionTypeId, out var baseType)
				? (ActionBaseTypes)baseType
				: BuiltInStatusBaseTypes.ForPersonnelStatus(actionTypeId);

		/// <summary>The call's map location, or null when it has none or it is a sealed protected-data envelope.</summary>
		public static GeoMath.GeoPoint? CallLocation(Call call)
		{
			if (call == null || ProtectedDataEnvelope.HasEnvelopePrefix(call.GeoLocationData))
				return null;

			return GeoMath.ParseLatLonString(call.GeoLocationData);
		}

		public static GeoMath.GeoPoint? UnitStateLocation(UnitState state)
		{
			if (state == null || state.IsProtected)
				return null;

			if (state.Latitude.HasValue && state.Longitude.HasValue && IsUsable((double)state.Latitude.Value, (double)state.Longitude.Value))
				return new GeoMath.GeoPoint((double)state.Latitude.Value, (double)state.Longitude.Value);

			return ProtectedDataEnvelope.HasEnvelopePrefix(state.GeoLocationData) ? null : GeoMath.ParseLatLonString(state.GeoLocationData);
		}

		private static bool IsUsable(double latitude, double longitude) =>
			!(latitude == 0 && longitude == 0) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;

		/// <summary>Minutes from <paramref name="from"/> to <paramref name="to"/>, or null when negative or longer than <paramref name="maxMinutes"/> (bad data).</summary>
		public static double? Minutes(DateTime from, DateTime to, double maxMinutes)
		{
			var minutes = (to - from).TotalMinutes;
			return minutes < 0 || minutes > maxMinutes ? (double?)null : Math.Round(minutes, 2);
		}

		/// <summary>Repeat-location cell: coordinates rounded to four decimals (about 11 m).</summary>
		public static string LocationKey(double latitude, double longitude) =>
			Math.Round(latitude, 4).ToString("0.0000", CultureInfo.InvariantCulture) + "," + Math.Round(longitude, 4).ToString("0.0000", CultureInfo.InvariantCulture);

		private static MapVisualizationCallFocus ToFocus(Call call)
		{
			var point = CallLocation(call);
			return new MapVisualizationCallFocus
			{
				CallId = call.CallId,
				Number = call.Number,
				Name = ProtectedDataEnvelope.HasEnvelopePrefix(call.Name) ? null : call.Name,
				Type = call.Type,
				Address = ProtectedDataEnvelope.HasEnvelopePrefix(call.Address) ? null : call.Address,
				LoggedOnUtc = call.LoggedOn,
				ClosedOnUtc = call.ClosedOn,
				Latitude = point?.Latitude,
				Longitude = point?.Longitude
			};
		}

		private static bool Contains(string value, string needle) =>
			!string.IsNullOrWhiteSpace(value) && !ProtectedDataEnvelope.HasEnvelopePrefix(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

		private static string Truncate(string value, int length) => value.Length <= length ? value : value.Substring(0, length);
		#endregion Run

		/// <summary>Per-run memo of the viewer's matrix checks: each unit or member is asked about once.</summary>
		private sealed class VisibilityCache
		{
			private readonly IAuthorizationService _authorizationService;
			private readonly int _departmentId;
			private readonly string _userId;
			private readonly Dictionary<int, bool> _units = new Dictionary<int, bool>();
			private readonly Dictionary<int, bool> _unitLocations = new Dictionary<int, bool>();
			private readonly Dictionary<string, bool> _people = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<string, bool> _personLocations = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

			public VisibilityCache(IAuthorizationService authorizationService, int departmentId, string userId)
			{
				_authorizationService = authorizationService;
				_departmentId = departmentId;
				_userId = userId;
			}

			public async Task<bool> CanViewUnitAsync(int unitId)
			{
				if (!_units.TryGetValue(unitId, out var allowed))
					_units[unitId] = allowed = await _authorizationService.CanUserViewUnitViaMatrixAsync(unitId, _userId, _departmentId);
				return allowed;
			}

			public async Task<bool> CanViewUnitLocationAsync(int unitId)
			{
				if (!_unitLocations.TryGetValue(unitId, out var allowed))
					_unitLocations[unitId] = allowed = await CanViewUnitAsync(unitId) &&
						await _authorizationService.CanUserViewUnitLocationViaMatrixAsync(unitId, _userId, _departmentId);
				return allowed;
			}

			public async Task<bool> CanViewPersonAsync(string userId)
			{
				if (!_people.TryGetValue(userId, out var allowed))
					_people[userId] = allowed = await _authorizationService.CanUserViewPersonViaMatrixAsync(userId, _userId, _departmentId);
				return allowed;
			}

			public async Task<bool> CanViewPersonLocationAsync(string userId)
			{
				if (!_personLocations.TryGetValue(userId, out var allowed))
					_personLocations[userId] = allowed = await CanViewPersonAsync(userId) &&
						await _authorizationService.CanUserViewPersonLocationViaMatrixAsync(userId, _userId, _departmentId);
				return allowed;
			}
		}
	}
}
