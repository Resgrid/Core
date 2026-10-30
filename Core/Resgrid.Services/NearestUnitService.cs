using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="INearestUnitService" />
	public class NearestUnitService : INearestUnitService
	{
		/// <summary>Roads run roughly a third longer than the straight line between two points.</summary>
		public const double EstimatedRoadDistanceFactor = UnitResponseOrigin.EstimatedRoadDistanceFactor;

		/// <summary>A blended urban/suburban response speed (40 km/h) for straight-line ETA estimates.</summary>
		public const double EstimatedSpeedMetersPerSecond = UnitResponseOrigin.EstimatedSpeedMetersPerSecond;

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
		private readonly IDepartmentsService _departmentsService;

		public NearestUnitService(IDispatchScopeService dispatchScopeService, IDepartmentGroupsService departmentGroupsService,
			IUnitsService unitsService, IUsersService usersService, IPersonnelRolesService personnelRolesService,
			IActionLogsService actionLogsService, IUserStateService userStateService, ICustomStateService customStateService,
			IPersonnelLocationResolver personnelLocationResolver, IShiftsService shiftsService,
			IDepartmentSettingsService departmentSettingsService, IGeoService geoService, IAuthorizationService authorizationService,
			IDepartmentsService departmentsService)
		{
			_departmentsService = departmentsService;
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

			// A distance or ETA from a position the viewer may not see would let them work the position out, so a hidden
			// position gets neither (and so no drive-time lookup): it ranks after the rows that have one.
			foreach (var candidate in unitCandidates.Cast<Candidate>().Concat(personnelCandidates).Where(c => !c.LocationHidden && c.Latitude.HasValue && c.Longitude.HasValue))
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

			var byDistance = config.RanksByDistance;

			board.Units = RankUnits(unitCandidates, byDistance)
				.Select(c => c.ToResult(groups, groupsById, boundaryGroupIds))
				.ToList();

			// A responder whose drive time was looked up comes before one left on a straight-line estimate, as a run card's
			// shortlist does: the estimate is the cheaper guess, not a comparable time.
			board.Personnel = personnelCandidates
				.OrderBy(p => !p.IsAvailable)
				.ThenBy(p => !p.DistanceMeters.HasValue)
				.ThenBy(p => p.LocationIsStale)
				.ThenBy(p => byDistance || p.DriveTimeLookedUp ? 0 : 1)
				.ThenBy(p => byDistance ? p.DistanceMeters ?? double.MaxValue : p.EtaSeconds ?? double.MaxValue)
				.ThenBy(p => p.DistanceMeters ?? double.MaxValue)
				.ThenBy(p => p.Person.Name, StringComparer.OrdinalIgnoreCase)
				.Select(c => c.ToResult())
				.ToList();

			return board;
		}

		/// <summary>
		/// The board's unit order. Available units with a distance come first, each unit type in the order a closest-unit
		/// run card ranks that type: fastest response (drive time first for the type's shortlist when routed ETAs are on,
		/// then the straight-line estimates), or nearest when the department ranks by distance. The types are then merged
		/// by taking whichever type's next unit is fastest (or nearest), so a type's own order is never broken: the unit a
		/// run card picks for a type is always that type's first row. A stale fix is flagged on the row rather than ranked
		/// down: sorting it below every fresh unit put a unit two minutes out in the middle of the list. Available units with
		/// no distance (no position, or one hidden from the viewer) follow, then the units that aren't available.
		/// </summary>
		private static List<UnitCandidate> RankUnits(List<UnitCandidate> units, bool byDistance)
		{
			double Key(UnitCandidate c) => byDistance ? c.DistanceMeters ?? double.MaxValue : c.ResponseSeconds ?? double.MaxValue;

			var queues = units
				.Where(c => c.IsAvailable && c.DistanceMeters.HasValue)
				.GroupBy(UnitTypeKey, StringComparer.OrdinalIgnoreCase)
				.Select(g => new Queue<UnitCandidate>(g
					.OrderBy(c => byDistance || c.DriveTimeLookedUp ? 0 : 1)
					.ThenBy(Key)
					.ThenBy(c => c.DistanceMeters.Value)
					.ThenBy(c => c.Unit.Name, StringComparer.OrdinalIgnoreCase)))
				.ToList();

			var ranked = new List<UnitCandidate>();
			while (true)
			{
				Queue<UnitCandidate> next = null;

				foreach (var queue in queues.Where(q => q.Count > 0))
				{
					if (next == null || CompareHeads(queue.Peek(), next.Peek(), Key) < 0)
						next = queue;
				}

				if (next == null)
					break;

				ranked.Add(next.Dequeue());
			}

			ranked.AddRange(units
				.Where(c => !(c.IsAvailable && c.DistanceMeters.HasValue))
				.OrderBy(c => !c.IsAvailable)
				.ThenBy(c => c.PositionSource == UnitPositionSources.None)
				.ThenBy(Key)
				.ThenBy(c => c.DistanceMeters ?? double.MaxValue)
				.ThenBy(c => c.Unit.Name, StringComparer.OrdinalIgnoreCase));

			return ranked;
		}

		private static int CompareHeads(UnitCandidate left, UnitCandidate right, Func<UnitCandidate, double> key)
		{
			var result = key(left).CompareTo(key(right));
			if (result == 0)
				result = (left.DistanceMeters ?? double.MaxValue).CompareTo(right.DistanceMeters ?? double.MaxValue);
			if (result == 0)
				result = StringComparer.OrdinalIgnoreCase.Compare(left.Unit.Name, right.Unit.Name);

			return result;
		}

		/// <summary>The unit type a run card requirement would match the unit on (types are matched by name on the board).</summary>
		private static string UnitTypeKey(UnitCandidate candidate)
		{
			return (candidate.Unit.Type ?? string.Empty).Trim();
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

			/// <summary>A drive time was looked up for this position (it may have failed and left the estimate).</summary>
			public bool DriveTimeLookedUp { get; set; }

			/// <summary>The position as the mapping provider is asked for it; set only once the row is measured.</summary>
			public string Origin => Latitude.HasValue && Longitude.HasValue ? FormatPoint(Latitude.Value, Longitude.Value) : null;
		}

		private sealed class UnitCandidate : Candidate
		{
			public Unit Unit { get; set; }

			public string StatusText { get; set; }

			public string StatusColor { get; set; }

			public string StatusTextColor { get; set; }

			public DateTime? PositionTimestamp { get; set; }

			public UnitPositionSources PositionSource { get; set; }

			public int TurnoutSeconds { get; set; }

			/// <summary>Turnout plus travel; null until the unit has an ETA.</summary>
			public double? ResponseSeconds => EtaSeconds.HasValue ? TurnoutSeconds + EtaSeconds.Value : (double?)null;

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
					StatusColor = StatusColor,
					StatusTextColor = StatusTextColor,
					IsAvailable = IsAvailable,
					// A position the viewer may not see is withheld, and was never measured (see GetBoardAsync).
					Latitude = LocationHidden ? null : Latitude,
					Longitude = LocationHidden ? null : Longitude,
					PositionTimestamp = PositionTimestamp,
					PositionIsStale = LocationIsStale,
					PositionSource = PositionSource,
					LocationHidden = LocationHidden,
					DistanceMeters = DistanceMeters,
					EtaSeconds = EtaSeconds,
					EtaSource = EtaSource,
					TurnoutSeconds = TurnoutSeconds,
					ResponseSeconds = ResponseSeconds,
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
					StatusColor = Person.StatusColor,
					StaffingText = Person.StaffingText,
					StaffingColor = Person.StaffingColor,
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

			public string StatusColor { get; set; }

			public string StaffingText { get; set; }

			public string StaffingColor { get; set; }

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
			// Only the members the dispatch lists offer (GetAllUsersForDepartmentAsync, capped at the plan's personnel limit), so
			// the board never ranks, maps or crews someone the dispatcher has no box to tick. The same cached call picks the
			// same members when the department is over its limit.
			var members = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId) ?? new List<IdentityUser>();
			var memberIds = new HashSet<string>(members.Where(u => !string.IsNullOrWhiteSpace(u?.UserId)).Select(u => u.UserId), StringComparer.OrdinalIgnoreCase);
			var rolesByUser = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(departmentId) ?? new List<ActionLog>();
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(departmentId) ?? new List<UserState>();
			var personnelDetails = BuildCustomDetailMap(await _customStateService.GetActivePersonnelStateForDepartmentAsync(departmentId));
			var staffingDetails = BuildCustomDetailMap(await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(departmentId));
			// No status inside the auto-available hour reads as the department's Available status, as on every other list.
			var defaultStatus = await _customStateService.GetDefaultPersonnelStatusAsync(departmentId);
			var locations = await _personnelLocationResolver.GetLatestLocationsAsync(departmentId, config.PersonnelMaxLocationAgeSeconds, now)
				?? new Dictionary<string, ResolvedPersonnelLocation>();
			var onShift = await _shiftsService.GetOnShiftPersonnelAsync(departmentId, now) ?? new List<OnShiftAssignment>();

			var logByUser = actionLogs.Where(l => !string.IsNullOrWhiteSpace(l?.UserId))
				.GroupBy(l => l.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First(), StringComparer.OrdinalIgnoreCase);
			var stateByUser = userStates.Where(s => !string.IsNullOrWhiteSpace(s?.UserId))
				.GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First(), StringComparer.OrdinalIgnoreCase);
			// Crew put On Unit by their unit's status are as available as that unit (PersonnelOnUnit).
			Dictionary<int, AvailabilityClass> latestUnitAvailability = null;
			if (logByUser.Values.Any(l => PersonnelOnUnit.IsLinkedOnUnit(l, personnelDetails.ContainsKey(l.ActionTypeId))))
			{
				latestUnitAvailability = PersonnelOnUnit.LatestUnitAvailability(
					await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId),
					CallStatusLinkage.BuildUnitBaseTypeMap(await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId)));
			}

			var unitNames = units.ToDictionary(u => u.UnitId, u => u.Name);
			var unitByUser = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var crew in crewByUnit)
				foreach (var userId in crew.Value)
					unitByUser.TryAdd(userId, crew.Key);

			var people = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);

			foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r?.UserId)))
			{
				if (people.ContainsKey(row.UserId) || !memberIds.Contains(row.UserId))
					continue;

				logByUser.TryGetValue(row.UserId, out var lastLog);
				stateByUser.TryGetValue(row.UserId, out var lastState);
				locations.TryGetValue(row.UserId, out var location);

				var status = GetPersonnelStatus(lastLog, personnelDetails, defaultStatus);
				var staffing = GetStaffing(lastState, staffingDetails);

				var person = new Person
				{
					UserId = row.UserId,
					Name = row.Name?.Trim(),
					HomeGroupId = row.DepartmentGroupId,
					Roles = rolesByUser.TryGetValue(row.UserId, out var roles) ? roles.Where(r => r != null).ToList() : new List<PersonnelRole>(),
					StatusText = status.Text,
					StatusColor = status.Color,
					StaffingText = staffing.Text,
					StaffingColor = staffing.Color,
					IsAvailable = IsPersonnelStatusAvailable(lastLog, personnelDetails, latestUnitAvailability) && IsPersonnelStaffingAvailable(lastState, staffingDetails),
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
			var defaultDetails = UnitStatusColors.BuildDefaultMap(_customStateService.GetDefaultUnitStatuses());
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
				var statusColors = UnitStatusColors.Resolve(stateId, customDetails, defaultDetails);

				var candidate = new UnitCandidate
				{
					Unit = unit,
					StatusText = GetUnitStatusText(stateId, isCustom, customDetails),
					StatusColor = statusColors.Color,
					StatusTextColor = statusColors.TextColor,
					IsAvailable = IsUnitAvailable(stateId, isCustom, customDetails)
				};

				var inQuarters = UnitResponseOrigin.IsInQuarters(stateId, customDetails);
				locationByUnit.TryGetValue(unit.UnitId, out var location);
				var liveFix = location != null ? new GeoMath.GeoPoint((double)location.Latitude, (double)location.Longitude) : (GeoMath.GeoPoint?)null;

				// In quarters, or no GPS on the unit: it is taken to be at its station, which is public knowledge, not a
				// tracked position. Looked up only then, since a station without stored coordinates is geocoded.
				GeoMath.GeoPoint? stationPoint = null;
				if ((inQuarters || liveFix == null) && unit.StationGroupId.HasValue && groupsById.TryGetValue(unit.StationGroupId.Value, out var station))
				{
					if (!stationPoints.TryGetValue(station.DepartmentGroupId, out stationPoint))
						stationPoints[station.DepartmentGroupId] = stationPoint = await _geoService.GetStationCoordinatesAsync(station);
				}

				var origin = UnitResponseOrigin.Resolve(inQuarters, liveFix, stationPoint, config);

				if (origin.Point.HasValue)
				{
					candidate.Latitude = origin.Point.Value.Latitude;
					candidate.Longitude = origin.Point.Value.Longitude;
					candidate.PositionSource = origin.Source;
					candidate.TurnoutSeconds = origin.TurnoutSeconds;
				}

				if (origin.Source == UnitPositionSources.Live)
				{
					candidate.PositionTimestamp = location.Timestamp;
					candidate.LocationIsStale = config.MaxLocationAgeSeconds > 0 && (now - location.Timestamp).TotalSeconds > config.MaxLocationAgeSeconds;
					candidate.LocationHidden = !await _authorizationService.CanUserViewUnitLocationViaMatrixAsync(unit.UnitId, request.UserId, departmentId);
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
			var perGroup = config.EtaShortlistSize > 0
				? Math.Min(config.EtaShortlistSize, DispatchRecommendationConfig.MaximumEtaShortlistSize)
				: DispatchRecommendationConfig.DefaultEtaShortlistSize;

			bool Routable(Candidate c) => c.IsAvailable && c.DistanceMeters.HasValue && !c.LocationIsStale;

			// Each unit type is shortlisted the way a run card requirement shortlists its type (on estimated response, or
			// on distance when the department ranks by distance), so the drive times on the board are the ones the run card
			// ranked on. One shortlist shared by every unit gave the drive times to the nearest few units overall and left the
			// rest of a type on straight-line estimates, sorted against drive times they can't be compared with: a unit that
			// a run card rightly picked could sit far down the board behind estimates. Responders are one more group.
			var groups = units.Where(Routable)
				.GroupBy(UnitTypeKey, StringComparer.OrdinalIgnoreCase)
				.Select(g => g
					.OrderBy(c => config.RanksByDistance ? c.DistanceMeters.Value : c.ResponseSeconds ?? double.MaxValue)
					.ThenBy(c => c.DistanceMeters.Value)
					.Take(perGroup)
					.Cast<Candidate>()
					.ToList())
				.OrderBy(g => g[0].DistanceMeters.Value)
				.ToList();
			groups.Add(personnel.Where(Routable).OrderBy(c => c.DistanceMeters.Value).Take(perGroup).Cast<Candidate>().ToList());

			// One lookup per position, a rank at a time across the groups (every type's best first) and units before
			// responders, up to the cap on what one board may ask of the mapping provider while the dispatcher waits. Units
			// parked at the same station share its drive time.
			var origins = new List<string>();
			var depth = groups.Max(g => g.Count);
			for (var rank = 0; rank < depth; rank++)
			{
				foreach (var group in groups.Where(g => rank < g.Count))
				{
					var origin = group[rank].Origin;
					if (origins.Count < DispatchRecommendationConfig.MaximumEtaShortlistSize && !origins.Contains(origin))
						origins.Add(origin);
				}
			}

			if (origins.Count == 0)
				return;

			cancellationToken.ThrowIfCancellationRequested();

			var destination = FormatPoint(request.Latitude, request.Longitude);
			var lookups = await Task.WhenAll(origins.Select(async origin => (Origin: origin, Eta: await _geoService.GetEtaInSecondsAsync(origin, destination))));
			var etaByOrigin = lookups.ToDictionary(x => x.Origin, x => x.Eta, StringComparer.Ordinal);

			foreach (var candidate in units.Cast<Candidate>().Concat(personnel).Where(Routable))
			{
				if (!etaByOrigin.TryGetValue(candidate.Origin, out var eta))
					continue;

				// A failed lookup keeps the estimate but still counts as looked up, as it does in a run card's shortlist.
				candidate.DriveTimeLookedUp = true;

				if (eta >= 0)
				{
					candidate.EtaSeconds = eta;
					candidate.EtaSource = EtaSources.Road;
				}
			}

			board.Notes.Add($"Drive times were looked up for {origins.Count} position(s): the {perGroup} {(config.RanksByDistance ? "closest" : "fastest")} " +
				"available units of each unit type and the closest responders (units at the same position share one). " +
				(config.RanksByDistance
					? "The rest are straight-line estimates. Units are ranked by distance, so drive times don't change the order."
					: "The rest are straight-line estimates, listed after the looked-up units of their type."));
		}

		public static double EstimateEtaSeconds(double distanceMeters)
		{
			return UnitResponseOrigin.EstimateTravelSeconds(distanceMeters);
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

				// Deleted options too: a unit or person still on one is on the department's option (StatusOptionIds).
				foreach (var detail in (state.Details ?? new List<CustomStateDetail>()).Where(x => x != null && x.CustomStateDetailId > 0))
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

		private static bool IsPersonnelStatusAvailable(ActionLog lastLog, Dictionary<int, CustomStateDetail> customDetails,
			IReadOnlyDictionary<int, AvailabilityClass> latestUnitAvailability = null)
		{
			// No status on file: the member never set one and is treated as standing by.
			if (lastLog == null)
				return true;

			return IsAvailableClass(customDetails.TryGetValue(lastLog.ActionTypeId, out var detail)
				? AvailabilityMatrix.ForCustomBaseType((int)detail.BaseType)
				: PersonnelOnUnit.FollowUnit(lastLog, false, latestUnitAvailability) ?? AvailabilityMatrix.ForBuiltInPersonnelActionType(lastLog.ActionTypeId));
		}

		private static bool IsPersonnelStaffingAvailable(UserState lastState, Dictionary<int, CustomStateDetail> customDetails)
		{
			// No staffing row defaults to Available (UserStateService semantics).
			if (lastState == null)
				return true;

			if (customDetails.TryGetValue(lastState.State, out var detail))
				return IsAvailableClass(AvailabilityMatrix.ForStaffingBaseType(detail.BaseType));

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

		/// <summary>
		/// The person's status as the lists show it: the department's own option, else the built-in status's name ("On Scene",
		/// not "OnScene"); with no status inside the auto-available hour, the department's default (its Available option).
		/// The colour is hex or null.
		/// </summary>
		private static (string Text, string Color) GetPersonnelStatus(ActionLog lastLog, Dictionary<int, CustomStateDetail> customDetails, CustomStateDetail defaultStatus)
		{
			if (lastLog == null)
			{
				return defaultStatus != null
					? (defaultStatus.ButtonText, UnitStatusColors.ToHexColor(defaultStatus.ButtonClassToColor()))
					: ("Standing By", "#777");
			}

			if (customDetails.TryGetValue(lastLog.ActionTypeId, out var detail))
				return (detail.ButtonText, UnitStatusColors.ToHexColor(detail.ButtonClassToColor()));

			if (!Enum.IsDefined(typeof(ActionTypes), lastLog.ActionTypeId))
				return (lastLog.ActionTypeId.ToString(CultureInfo.InvariantCulture), null);

			return (lastLog.GetActionText(), UnitStatusColors.ToHexColor(new CustomStateDetail { ButtonColor = lastLog.GetActionCss() }.ButtonClassToColor()));
		}

		/// <summary>The person's staffing level: the department's own level, else the built-in level's name. The colour is hex or null.</summary>
		private static (string Text, string Color) GetStaffing(UserState lastState, Dictionary<int, CustomStateDetail> customDetails)
		{
			var state = lastState ?? new UserState { State = (int)UserStateTypes.Available };

			if (customDetails.TryGetValue(state.State, out var detail))
				return (detail.ButtonText, UnitStatusColors.ToHexColor(detail.ButtonClassToColor()));

			if (!Enum.IsDefined(typeof(UserStateTypes), state.State))
				return (state.State.ToString(CultureInfo.InvariantCulture), null);

			return (state.GetStaffingText(), UnitStatusColors.ToHexColor(new CustomStateDetail { ButtonColor = state.GetStaffingCss() }.ButtonClassToColor()));
		}

		#endregion
	}
}
