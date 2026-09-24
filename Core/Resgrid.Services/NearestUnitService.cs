using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="INearestUnitService" />
	public class NearestUnitService : INearestUnitService
	{
		/// <summary>Roads run roughly a third longer than the straight line between two points.</summary>
		public const double EstimatedRoadDistanceFactor = 1.3;

		/// <summary>A blended urban/suburban response speed (40 km/h) for straight-line ETA estimates.</summary>
		public const double EstimatedSpeedMetersPerSecond = 40000d / 3600d;

		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IUsersService _usersService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly ICustomStateService _customStateService;
		private readonly IPersonnelLocationResolver _personnelLocationResolver;
		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoService _geoService;
		private readonly IAuthorizationService _authorizationService;

		public NearestUnitService(IDispatchScopeService dispatchScopeService, IDepartmentGroupsService departmentGroupsService,
			IUnitsService unitsService, IUsersService usersService, IPersonnelRolesService personnelRolesService,
			IActionLogsService actionLogsService, IUserStateService userStateService, ICustomStateService customStateService,
			IPersonnelLocationResolver personnelLocationResolver, IShiftsService shiftsService,
			IDepartmentSettingsService departmentSettingsService, IGeoService geoService, IAuthorizationService authorizationService)
		{
			_dispatchScopeService = dispatchScopeService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_usersService = usersService;
			_personnelRolesService = personnelRolesService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_customStateService = customStateService;
			_personnelLocationResolver = personnelLocationResolver;
			_shiftsService = shiftsService;
			_departmentSettingsService = departmentSettingsService;
			_geoService = geoService;
			_authorizationService = authorizationService;
		}

		public async Task<NearestUnitBoard> GetBoardAsync(NearestUnitRequest request, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			var now = DateTime.UtcNow;
			var board = new NearestUnitBoard { Latitude = request.Latitude, Longitude = request.Longitude, GeneratedOn = now };

			if (!IsUsableCoordinate(request.Latitude, request.Longitude))
			{
				board.Notes.Add("The incident has no usable location, so nothing can be ranked by distance.");
				return board;
			}

			var departmentId = request.DepartmentId;
			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, request.UserId);
			board.IsDepartmentWide = scope.IsDepartmentWide;
			board.ScopeReason = scope.Reason;

			var config = await _departmentSettingsService.GetDispatchRecommendationConfigAsync(departmentId) ?? new DispatchRecommendationConfig();

			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(departmentId) ?? new List<DepartmentGroup>())
				.Where(g => g != null)
				.GroupBy(g => g.DepartmentGroupId)
				.Select(g => g.First())
				.ToList();
			var groupsById = groups.ToDictionary(g => g.DepartmentGroupId);

			var boundaryGroupIds = new HashSet<int>(groups
				.Where(g => GeoMath.IsPointInPolygon(request.Latitude, request.Longitude, GeoMath.ParseGeofence(g.Geofence)))
				.Select(g => g.DepartmentGroupId));

			// Every containing boundary is listed, in scope or not: a supervisor needs to see that an
			// incident sits in another area even though that area's units aren't on their board.
			board.ContainingBoundaries = boundaryGroupIds
				.Select(id => groupsById[id])
				.OrderByDescending(g => DepartmentGroupHierarchy.GetAncestorIds(groups, g.DepartmentGroupId).Count)
				.ThenBy(g => g.Name)
				.Select(g => new NearestBoundaryGroup { DepartmentGroupId = g.DepartmentGroupId, Name = g.Name, GroupType = g.Type })
				.ToList();

			var units = (await _unitsService.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>()).Where(u => u != null).ToList();
			var crewByUnit = BuildCrewByUnit(await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(departmentId));
			var people = await BuildPeopleAsync(departmentId, config, crewByUnit, units, now);

			var unitCandidates = await BuildUnitCandidatesAsync(request, scope, config, units, groupsById, now);
			var personnelCandidates = await BuildPersonnelCandidatesAsync(request, scope, people, groupsById);

			foreach (var candidate in unitCandidates.Cast<Candidate>().Concat(personnelCandidates).Where(c => c.Latitude.HasValue && c.Longitude.HasValue))
			{
				var distance = GeoMath.HaversineMeters(request.Latitude, request.Longitude, candidate.Latitude.Value, candidate.Longitude.Value);
				candidate.DistanceMeters = distance;
				candidate.EtaSeconds = EstimateEtaSeconds(distance);
				candidate.EtaSource = EtaSources.Estimated;
			}

			var useRoadEta = request.UseRoadEta ?? config.UseRoutedEta;
			if (useRoadEta)
				await ApplyRoadEtasAsync(request, config, unitCandidates, personnelCandidates, board, cancellationToken);
			else
				board.Notes.Add("ETAs are straight-line estimates; turn on routed ETAs in the dispatch settings for drive times.");

			foreach (var candidate in unitCandidates)
				await AttachCrewAsync(request, candidate, crewByUnit, people);

			board.Units = unitCandidates
				.Select(c => c.ToResult(groups, groupsById, boundaryGroupIds))
				.OrderBy(u => !u.IsAvailable)
				.ThenBy(u => u.PositionSource == UnitPositionSources.None)
				.ThenBy(u => u.PositionIsStale)
				.ThenBy(u => u.EtaSeconds ?? double.MaxValue)
				.ThenBy(u => u.DistanceMeters ?? double.MaxValue)
				.ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			board.Personnel = personnelCandidates
				.Select(c => c.ToResult())
				.OrderBy(p => !p.IsAvailable)
				.ThenBy(p => !p.DistanceMeters.HasValue)
				.ThenBy(p => p.LocationIsStale)
				.ThenBy(p => p.EtaSeconds ?? double.MaxValue)
				.ThenBy(p => p.DistanceMeters ?? double.MaxValue)
				.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return board;
		}

		#region Candidates

		/// <summary>A ranked row plus the raw coordinates it was ranked on (withheld from the output when hidden).</summary>
		private abstract class Candidate
		{
			public double? Latitude { get; set; }

			public double? Longitude { get; set; }

			public bool LocationHidden { get; set; }

			public bool IsAvailable { get; set; }

			public bool LocationIsStale { get; set; }

			public double? DistanceMeters { get; set; }

			public double? EtaSeconds { get; set; }

			public EtaSources EtaSource { get; set; }
		}

		private sealed class UnitCandidate : Candidate
		{
			public Unit Unit { get; set; }

			public string StatusText { get; set; }

			public DateTime? PositionTimestamp { get; set; }

			public UnitPositionSources PositionSource { get; set; }

			public UnitCrewSources CrewSource { get; set; }

			public List<string> CrewNames { get; } = new List<string>();

			public List<Person> Crew { get; } = new List<Person>();

			public NearestUnitResult ToResult(List<DepartmentGroup> groups, Dictionary<int, DepartmentGroup> groupsById, HashSet<int> boundaryGroupIds)
			{
				DepartmentGroup station = null;
				DepartmentGroup parent = null;

				if (Unit.StationGroupId.HasValue && groupsById.TryGetValue(Unit.StationGroupId.Value, out station) && station.ParentDepartmentGroupId.HasValue)
					groupsById.TryGetValue(station.ParentDepartmentGroupId.Value, out parent);

				var ancestors = station == null ? new List<int>() : DepartmentGroupHierarchy.GetAncestorIds(groups, station.DepartmentGroupId);

				return new NearestUnitResult
				{
					UnitId = Unit.UnitId,
					Name = Unit.Name,
					UnitType = Unit.Type,
					DepartmentGroupId = Unit.StationGroupId,
					GroupName = station?.Name,
					ParentGroupId = parent?.DepartmentGroupId,
					ParentGroupName = parent?.Name,
					IncidentInGroupBoundary = station != null && boundaryGroupIds.Contains(station.DepartmentGroupId),
					IncidentInParentBoundary = ancestors.Any(boundaryGroupIds.Contains),
					StatusText = StatusText,
					IsAvailable = IsAvailable,
					// A position the viewer may not see is withheld here, after it has fed the ranking.
					Latitude = LocationHidden ? null : Latitude,
					Longitude = LocationHidden ? null : Longitude,
					PositionTimestamp = PositionTimestamp,
					PositionIsStale = LocationIsStale,
					PositionSource = PositionSource,
					LocationHidden = LocationHidden,
					DistanceMeters = DistanceMeters,
					EtaSeconds = EtaSeconds,
					EtaSource = EtaSource,
					CrewSource = CrewSource,
					Crew = CrewNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
					CrewCount = Crew.Count,
					CrewAvailableCount = Crew.Count(p => p.IsAvailable),
					OnShiftCount = Crew.Count(p => p.IsOnShift),
					ShiftNames = Crew.Where(p => p.IsOnShift)
						.SelectMany(p => p.ShiftNames)
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
						.ToList(),
					RoleMix = Crew
						.SelectMany(p => p.Roles)
						.GroupBy(r => r.PersonnelRoleId)
						.Select(g => new RoleCount { PersonnelRoleId = g.Key, Name = g.First().Name, Count = g.Count() })
						.OrderByDescending(r => r.Count)
						.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
						.ToList()
				};
			}
		}

		private sealed class PersonnelCandidate : Candidate
		{
			public Person Person { get; set; }

			public int? CoveringGroupId { get; set; }

			public string GroupName { get; set; }

			public DateTime? LocationTimestamp { get; set; }

			public NearestPersonnelResult ToResult()
			{
				return new NearestPersonnelResult
				{
					UserId = Person.UserId,
					Name = Person.Name,
					DepartmentGroupId = CoveringGroupId,
					GroupName = GroupName,
					UnitId = Person.UnitId,
					UnitName = Person.UnitName,
					StatusText = Person.StatusText,
					StaffingText = Person.StaffingText,
					IsAvailable = IsAvailable,
					IsOnShift = Person.IsOnShift,
					Roles = Person.Roles.Where(r => !string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Name)
						.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
					Latitude = LocationHidden ? null : Latitude,
					Longitude = LocationHidden ? null : Longitude,
					LocationTimestamp = LocationTimestamp,
					LocationIsStale = LocationIsStale,
					LocationHidden = LocationHidden,
					DistanceMeters = DistanceMeters,
					EtaSeconds = EtaSeconds,
					EtaSource = EtaSource
				};
			}
		}

		/// <summary>Everything the board knows about one member, department-wide (before scope and visibility).</summary>
		private sealed class Person
		{
			public string UserId { get; set; }

			public string Name { get; set; }

			public int? HomeGroupId { get; set; }

			public bool IsOnShift { get; set; }

			/// <summary>The group a running shift has the person covering; null when the shift doesn't say.</summary>
			public int? ShiftGroupId { get; set; }

			public SortedSet<string> ShiftNames { get; } = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

			public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();

			public string StatusText { get; set; }

			public string StaffingText { get; set; }

			public bool IsAvailable { get; set; }

			public ResolvedPersonnelLocation Location { get; set; }

			public int? UnitId { get; set; }

			public string UnitName { get; set; }

			/// <summary>Someone covering a running shift for another group counts toward that group.</summary>
			public int? CoveringGroupId => ShiftGroupId ?? HomeGroupId;
		}

		private static Dictionary<int, List<string>> BuildCrewByUnit(List<UnitActiveRole> activeRoles)
		{
			return (activeRoles ?? new List<UnitActiveRole>())
				.Where(r => r != null && !string.IsNullOrWhiteSpace(r.UserId))
				.GroupBy(r => r.UnitId)
				.ToDictionary(g => g.Key, g => g.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
		}

		private async Task<Dictionary<string, Person>> BuildPeopleAsync(int departmentId, DispatchRecommendationConfig config,
			Dictionary<int, List<string>> crewByUnit, List<Unit> units, DateTime now)
		{
			var rows = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(departmentId, false, false, false) ?? new List<UserGroupRole>();
			var rolesByUser = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(departmentId) ?? new List<ActionLog>();
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(departmentId) ?? new List<UserState>();
			var personnelDetails = BuildCustomDetailMap(await _customStateService.GetActivePersonnelStateForDepartmentAsync(departmentId));
			var staffingDetails = BuildCustomDetailMap(await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(departmentId));
			var locations = await _personnelLocationResolver.GetLatestLocationsAsync(departmentId, config.PersonnelMaxLocationAgeSeconds, now)
				?? new Dictionary<string, ResolvedPersonnelLocation>();
			var onShift = await _shiftsService.GetOnShiftPersonnelAsync(departmentId, now) ?? new List<OnShiftAssignment>();

			var logByUser = actionLogs.Where(l => !string.IsNullOrWhiteSpace(l?.UserId))
				.GroupBy(l => l.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First(), StringComparer.OrdinalIgnoreCase);
			var stateByUser = userStates.Where(s => !string.IsNullOrWhiteSpace(s?.UserId))
				.GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First(), StringComparer.OrdinalIgnoreCase);
			var unitNames = units.ToDictionary(u => u.UnitId, u => u.Name);
			var unitByUser = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var crew in crewByUnit)
				foreach (var userId in crew.Value)
					unitByUser.TryAdd(userId, crew.Key);

			var people = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);

			foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r?.UserId)))
			{
				if (people.ContainsKey(row.UserId))
					continue;

				logByUser.TryGetValue(row.UserId, out var lastLog);
				stateByUser.TryGetValue(row.UserId, out var lastState);
				locations.TryGetValue(row.UserId, out var location);

				var person = new Person
				{
					UserId = row.UserId,
					Name = row.Name?.Trim(),
					HomeGroupId = row.DepartmentGroupId,
					Roles = rolesByUser.TryGetValue(row.UserId, out var roles) ? roles.Where(r => r != null).ToList() : new List<PersonnelRole>(),
					StatusText = GetPersonnelStatusText(lastLog, personnelDetails),
					StaffingText = GetStaffingText(lastState, staffingDetails),
					IsAvailable = IsPersonnelStatusAvailable(lastLog, personnelDetails) && IsPersonnelStaffingAvailable(lastState, staffingDetails),
					Location = location != null && IsUsableCoordinate(location.Latitude, location.Longitude) ? location : null
				};

				if (unitByUser.TryGetValue(row.UserId, out var unitId))
				{
					person.UnitId = unitId;
					person.UnitName = unitNames.TryGetValue(unitId, out var unitName) ? unitName : null;
				}

				people[row.UserId] = person;
			}

			foreach (var assignment in onShift.Where(a => !string.IsNullOrWhiteSpace(a?.UserId)))
			{
				if (!people.TryGetValue(assignment.UserId, out var person))
					continue;

				// A shift that names the group wins over one that doesn't.
				if (!person.IsOnShift || (!person.ShiftGroupId.HasValue && assignment.DepartmentGroupId.HasValue))
					person.ShiftGroupId = assignment.DepartmentGroupId;

				person.IsOnShift = true;

				if (!string.IsNullOrWhiteSpace(assignment.ShiftName))
					person.ShiftNames.Add(assignment.ShiftName);
			}

			return people;
		}

		private async Task<List<UnitCandidate>> BuildUnitCandidatesAsync(NearestUnitRequest request, DispatchScope scope,
			DispatchRecommendationConfig config, List<Unit> units, Dictionary<int, DepartmentGroup> groupsById, DateTime now)
		{
			var departmentId = request.DepartmentId;
			var candidates = new List<UnitCandidate>();

			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitState>();
			var customDetails = BuildCustomDetailMap(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(departmentId));
			var locations = await _unitsService.GetLatestUnitLocationsAsync(departmentId) ?? new List<UnitsLocation>();

			var stateByUnit = states.Where(s => s != null).GroupBy(s => s.UnitId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First());
			var locationByUnit = locations
				.Where(l => l != null && l.IsValidFix != false && !(l.Latitude == 0 && l.Longitude == 0))
				.GroupBy(l => l.UnitId)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First());
			var stationPoints = new Dictionary<int, GeoMath.GeoPoint?>();

			foreach (var unit in units)
			{
				if (!scope.IncludesGroup(unit.StationGroupId))
					continue;

				if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, request.UserId, departmentId))
					continue;

				var stateId = stateByUnit.TryGetValue(unit.UnitId, out var state) ? state.State : (int)UnitStateTypes.Available;
				var isCustom = customDetails.ContainsKey(stateId);

				var candidate = new UnitCandidate
				{
					Unit = unit,
					StatusText = GetUnitStatusText(stateId, isCustom, customDetails),
					IsAvailable = IsUnitAvailable(stateId, isCustom, customDetails)
				};

				if (locationByUnit.TryGetValue(unit.UnitId, out var location))
				{
					candidate.Latitude = (double)location.Latitude;
					candidate.Longitude = (double)location.Longitude;
					candidate.PositionTimestamp = location.Timestamp;
					candidate.PositionSource = UnitPositionSources.Live;
					candidate.LocationIsStale = config.MaxLocationAgeSeconds > 0 && (now - location.Timestamp).TotalSeconds > config.MaxLocationAgeSeconds;
					candidate.LocationHidden = !await _authorizationService.CanUserViewUnitLocationViaMatrixAsync(unit.UnitId, request.UserId, departmentId);
				}
				else if (unit.StationGroupId.HasValue && groupsById.TryGetValue(unit.StationGroupId.Value, out var station))
				{
					// No GPS on the unit: it is taken to be at its station, which is public knowledge, not a tracked position.
					if (!stationPoints.TryGetValue(station.DepartmentGroupId, out var point))
						stationPoints[station.DepartmentGroupId] = point = await _geoService.GetStationCoordinatesAsync(station);

					if (point.HasValue)
					{
						candidate.Latitude = point.Value.Latitude;
						candidate.Longitude = point.Value.Longitude;
						candidate.PositionSource = UnitPositionSources.Station;
					}
				}

				candidates.Add(candidate);
			}

			return candidates;
		}

		private async Task<List<PersonnelCandidate>> BuildPersonnelCandidatesAsync(NearestUnitRequest request, DispatchScope scope,
			Dictionary<string, Person> people, Dictionary<int, DepartmentGroup> groupsById)
		{
			var candidates = new List<PersonnelCandidate>();

			foreach (var person in people.Values)
			{
				if (!scope.IncludesGroup(person.CoveringGroupId))
					continue;

				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(person.UserId, request.UserId, request.DepartmentId))
					continue;

				var candidate = new PersonnelCandidate
				{
					Person = person,
					CoveringGroupId = person.CoveringGroupId,
					GroupName = person.CoveringGroupId.HasValue && groupsById.TryGetValue(person.CoveringGroupId.Value, out var group) ? group.Name : null,
					IsAvailable = person.IsAvailable
				};

				if (person.Location != null)
				{
					candidate.Latitude = person.Location.Latitude;
					candidate.Longitude = person.Location.Longitude;
					candidate.LocationTimestamp = person.Location.Timestamp;
					candidate.LocationIsStale = person.Location.IsStale;
					candidate.LocationHidden = !await _authorizationService.CanUserViewPersonLocationViaMatrixAsync(person.UserId, request.UserId, request.DepartmentId);
				}

				candidates.Add(candidate);
			}

			return candidates;
		}

		#endregion

		#region Crew and ETAs

		private async Task AttachCrewAsync(NearestUnitRequest request, UnitCandidate candidate, Dictionary<int, List<string>> crewByUnit,
			Dictionary<string, Person> people)
		{
			List<Person> crew;

			if (crewByUnit.TryGetValue(candidate.Unit.UnitId, out var assigned) && assigned.Any())
			{
				crew = assigned.Where(people.ContainsKey).Select(id => people[id]).ToList();
				candidate.CrewSource = UnitCrewSources.Assigned;
			}
			else
			{
				// Nobody in the unit's seats: whoever is on a running shift at its station is its crew.
				crew = candidate.Unit.StationGroupId.HasValue
					? people.Values.Where(p => p.IsOnShift && p.CoveringGroupId == candidate.Unit.StationGroupId).ToList()
					: new List<Person>();
				candidate.CrewSource = crew.Any() ? UnitCrewSources.StationShift : UnitCrewSources.None;
			}

			candidate.Crew.AddRange(crew);

			// Counts and roles describe the unit; names are only shown for people the viewer may see.
			foreach (var person in crew)
			{
				if (await _authorizationService.CanUserViewPersonViaMatrixAsync(person.UserId, request.UserId, request.DepartmentId))
					candidate.CrewNames.Add(person.Name);
			}
		}

		private async Task ApplyRoadEtasAsync(NearestUnitRequest request, DispatchRecommendationConfig config, List<UnitCandidate> units,
			List<PersonnelCandidate> personnel, NearestUnitBoard board, CancellationToken cancellationToken)
		{
			// Each lookup is a mapping-provider call made while the dispatcher waits, so the count is
			// bounded by the department's ETA shortlist size (itself capped).
			var budget = config.EtaShortlistSize > 0
				? Math.Min(config.EtaShortlistSize, DispatchRecommendationConfig.MaximumEtaShortlistSize)
				: DispatchRecommendationConfig.DefaultEtaShortlistSize;

			// Units are what gets dispatched, so they take the budget first; individual responders get what's left.
			var shortlist = units
				.Where(c => c.IsAvailable && c.DistanceMeters.HasValue && !c.LocationIsStale)
				.OrderBy(c => c.DistanceMeters.Value)
				.Cast<Candidate>()
				.Concat(personnel
					.Where(c => c.IsAvailable && c.DistanceMeters.HasValue && !c.LocationIsStale)
					.OrderBy(c => c.DistanceMeters.Value))
				.Take(budget)
				.ToList();

			var destination = FormatPoint(request.Latitude, request.Longitude);

			foreach (var candidate in shortlist)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var eta = await _geoService.GetEtaInSecondsAsync(FormatPoint(candidate.Latitude.Value, candidate.Longitude.Value), destination);

				if (eta >= 0)
				{
					candidate.EtaSeconds = eta;
					candidate.EtaSource = EtaSources.Road;
				}
			}

			if (shortlist.Any())
				board.Notes.Add($"Drive times were looked up for the {shortlist.Count} closest available units and responders; the rest are straight-line estimates.");
		}

		public static double EstimateEtaSeconds(double distanceMeters)
		{
			return Math.Round(distanceMeters * EstimatedRoadDistanceFactor / EstimatedSpeedMetersPerSecond);
		}

		private static string FormatPoint(double latitude, double longitude)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0},{1}", latitude, longitude);
		}

		private static bool IsUsableCoordinate(double latitude, double longitude)
		{
			return !double.IsNaN(latitude) && !double.IsNaN(longitude)
				&& latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180
				&& !(latitude == 0 && longitude == 0);
		}

		#endregion

		#region Availability
		// The run card engine's default path (no run card status selections): the shared availability
		// matrix decides, and Delayed still counts as available.

		private static Dictionary<int, CustomStateDetail> BuildCustomDetailMap(IEnumerable<CustomState> states)
		{
			var map = new Dictionary<int, CustomStateDetail>();

			foreach (var state in states ?? Enumerable.Empty<CustomState>())
			{
				if (state == null)
					continue;

				foreach (var detail in state.GetActiveDetails() ?? new List<CustomStateDetail>())
					map[detail.CustomStateDetailId] = detail;
			}

			return map;
		}

		private static Dictionary<int, CustomStateDetail> BuildCustomDetailMap(CustomState state)
		{
			return BuildCustomDetailMap(state == null ? null : new[] { state });
		}

		private static bool IsAvailableClass(AvailabilityClass availability)
		{
			return availability == AvailabilityClass.Available || availability == AvailabilityClass.Delayed;
		}

		private static bool IsUnitAvailable(int stateId, bool isCustom, Dictionary<int, CustomStateDetail> customDetails)
		{
			return IsAvailableClass(isCustom
				? AvailabilityMatrix.ForCustomBaseType((int)customDetails[stateId].BaseType)
				: AvailabilityMatrix.ForUnitStateType(stateId));
		}

		private static bool IsPersonnelStatusAvailable(ActionLog lastLog, Dictionary<int, CustomStateDetail> customDetails)
		{
			// No status on file: the member never set one and is treated as standing by.
			if (lastLog == null)
				return true;

			return IsAvailableClass(customDetails.TryGetValue(lastLog.ActionTypeId, out var detail)
				? AvailabilityMatrix.ForCustomBaseType((int)detail.BaseType)
				: AvailabilityMatrix.ForBuiltInPersonnelActionType(lastLog.ActionTypeId));
		}

		private static bool IsPersonnelStaffingAvailable(UserState lastState, Dictionary<int, CustomStateDetail> customDetails)
		{
			// No staffing row defaults to Available (UserStateService semantics).
			if (lastState == null)
				return true;

			if (customDetails.TryGetValue(lastState.State, out var detail))
				return IsAvailableClass(AvailabilityMatrix.ForCustomBaseType((int)detail.BaseType));

			return lastState.State == (int)UserStateTypes.Available
				|| lastState.State == (int)UserStateTypes.OnShift
				|| lastState.State == (int)UserStateTypes.Delayed;
		}

		private static string GetUnitStatusText(int stateId, bool isCustom, Dictionary<int, CustomStateDetail> customDetails)
		{
			if (isCustom && customDetails.TryGetValue(stateId, out var detail))
				return detail.ButtonText;

			return Enum.IsDefined(typeof(UnitStateTypes), stateId) ? ((UnitStateTypes)stateId).ToString() : stateId.ToString(CultureInfo.InvariantCulture);
		}

		private static string GetPersonnelStatusText(ActionLog lastLog, Dictionary<int, CustomStateDetail> customDetails)
		{
			if (lastLog == null)
				return ActionTypes.StandingBy.ToString();

			if (customDetails.TryGetValue(lastLog.ActionTypeId, out var detail))
				return detail.ButtonText;

			return Enum.IsDefined(typeof(ActionTypes), lastLog.ActionTypeId) ? ((ActionTypes)lastLog.ActionTypeId).ToString() : lastLog.ActionTypeId.ToString(CultureInfo.InvariantCulture);
		}

		private static string GetStaffingText(UserState lastState, Dictionary<int, CustomStateDetail> customDetails)
		{
			if (lastState == null)
				return UserStateTypes.Available.ToString();

			if (customDetails.TryGetValue(lastState.State, out var detail))
				return detail.ButtonText;

			return Enum.IsDefined(typeof(UserStateTypes), lastState.State) ? ((UserStateTypes)lastState.State).ToString() : lastState.State.ToString(CultureInfo.InvariantCulture);
		}

		#endregion
	}
}
