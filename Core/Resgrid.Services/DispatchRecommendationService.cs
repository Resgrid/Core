using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DispatchRecommendationService : IDispatchRecommendationService
	{
		private readonly IRunCardsService _runCardsService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoService _geoService;
		private readonly IPersonnelLocationResolver _personnelLocationResolver;
		private readonly IShiftsService _shiftsService;
		private readonly IRunCardActivationsRepository _runCardActivationsRepository;
		private readonly IEventAggregator _eventAggregator;

		public DispatchRecommendationService(IRunCardsService runCardsService, IUnitsService unitsService,
			IActionLogsService actionLogsService, IUserStateService userStateService, IPersonnelRolesService personnelRolesService,
			ICustomStateService customStateService, IDepartmentGroupsService departmentGroupsService,
			IDepartmentSettingsService departmentSettingsService, IGeoService geoService,
			IPersonnelLocationResolver personnelLocationResolver, IShiftsService shiftsService,
			IRunCardActivationsRepository runCardActivationsRepository, IEventAggregator eventAggregator)
		{
			_runCardsService = runCardsService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_personnelRolesService = personnelRolesService;
			_customStateService = customStateService;
			_departmentGroupsService = departmentGroupsService;
			_departmentSettingsService = departmentSettingsService;
			_geoService = geoService;
			_personnelLocationResolver = personnelLocationResolver;
			_shiftsService = shiftsService;
			_runCardActivationsRepository = runCardActivationsRepository;
			_eventAggregator = eventAggregator;
		}

		public async Task<DispatchRecommendationResult> GetRecommendationAsync(DispatchRecommendationRequest request, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			var result = new DispatchRecommendationResult { AlarmLevel = request.TargetAlarmLevel };

			var card = await _runCardsService.GetMatchingRunCardAsync(request.DepartmentId, request.Priority, request.CallTypeName);

			if (card == null)
			{
				result.Notes.Add("No run card matches this call's priority and type; manual dispatch flow applies.");
				return result;
			}

			result.MatchedRunCardId = card.RunCardId;
			result.MatchedRunCardName = card.Name;

			var departmentMode = await _departmentSettingsService.GetDispatchRecommendationModeAsync(request.DepartmentId);
			var mode = request.ModeOverride
				?? (card.DispatchModeOverride.HasValue ? (DispatchRecommendationModes)card.DispatchModeOverride.Value : departmentMode);
			result.ModeUsed = mode;

			var departmentAuto = await _departmentSettingsService.GetDispatchRecommendationAutoDispatchAsync(request.DepartmentId);
			result.AutoDispatch = card.AutoDispatchOverride.HasValue ? card.AutoDispatchOverride.Value == 1 : departmentAuto;

			var level = card.AlarmLevels?.FirstOrDefault(l => l.AlarmLevel == request.TargetAlarmLevel);

			if (level == null)
			{
				result.Notes.Add($"Run card '{card.Name}' has no alarm level {request.TargetAlarmLevel}; nothing to add.");
				return result;
			}

			if (mode == DispatchRecommendationModes.Off)
			{
				result.Notes.Add($"Run card '{card.Name}' matched but automatic resource selection is off; use its requirements as a manual checklist.");
				return result;
			}

			var config = await _departmentSettingsService.GetDispatchRecommendationConfigAsync(request.DepartmentId);
			var staffingGate = card.MinimumStaffingLevelOverride ?? config.UnitMinimumStaffingLevel;
			var now = DateTime.UtcNow;

			var context = new RecommendationContext
			{
				Request = request,
				Card = card,
				Level = level,
				Config = config,
				StaffingGate = staffingGate,
				Now = now,
				Result = result,
				CancellationToken = cancellationToken
			};

			await BuildUnitCandidatesAsync(context);
			await BuildPersonnelCandidatesAsync(context);

			if (config.RestPeriodMinutes > 0)
			{
				context.UnitLastDispatched = await _runCardsService.GetLastUnitDispatchTimesAsync(request.DepartmentId);
				context.UserLastDispatched = await _runCardsService.GetLastUserDispatchTimesAsync(request.DepartmentId);
			}

			context.CallLocation = ResolveCallLocation(request);

			if (mode == DispatchRecommendationModes.StationBased)
				await FillStationBasedAsync(context);
			else if (mode == DispatchRecommendationModes.ClosestUnit)
				await FillClosestUnitAsync(context);

			if (config.MoveUpRecommendationsEnabled)
				await RunMoveUpPassAsync(context);

			return result;
		}

		public async Task<DispatchRecommendationResult> EnrichCallForDispatchAsync(Call call, int targetAlarmLevel, bool onlyWhenAutoDispatch = false, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (call == null)
				throw new ArgumentNullException(nameof(call));

			var location = GeoMath.ParseLatLonString(call.GeoLocationData);

			var request = new DispatchRecommendationRequest
			{
				DepartmentId = call.DepartmentId,
				Priority = call.Priority,
				CallTypeName = call.Type,
				Latitude = location?.Latitude,
				Longitude = location?.Longitude,
				TargetAlarmLevel = targetAlarmLevel,
				AlreadyDispatchedUnitIds = call.UnitDispatches?.Select(d => d.UnitId).ToList() ?? new List<int>(),
				AlreadyDispatchedUserIds = call.Dispatches?.Select(d => d.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>()
			};

			var result = await GetRecommendationAsync(request, cancellationToken);

			if (!result.MatchedRunCardId.HasValue)
				return result;

			call.ActiveRunCardId = result.MatchedRunCardId;

			if (targetAlarmLevel > call.AlarmLevel || call.AlarmLevel <= 0)
				call.AlarmLevel = Math.Max(1, targetAlarmLevel);

			if (onlyWhenAutoDispatch && !result.AutoDispatch)
			{
				result.Notes.Add("Auto-dispatch is off; recommendations were computed but not applied to the call.");
				return result;
			}

			if (result.Units.Any())
			{
				if (call.UnitDispatches == null)
					call.UnitDispatches = new Collection<CallDispatchUnit>();

				foreach (var unit in result.Units)
				{
					if (call.UnitDispatches.Any(d => d.UnitId == unit.UnitId))
						continue;

					call.UnitDispatches.Add(new CallDispatchUnit
					{
						CallId = call.CallId,
						UnitId = unit.UnitId
					});
				}
			}

			if (result.Personnel.Any())
			{
				if (call.Dispatches == null)
					call.Dispatches = new Collection<CallDispatch>();

				foreach (var person in result.Personnel)
				{
					if (call.Dispatches.Any(d => d.UserId == person.UserId))
						continue;

					call.Dispatches.Add(new CallDispatch
					{
						CallId = call.CallId,
						UserId = person.UserId
					});
				}
			}

			return result;
		}

		public async Task RecordActivationAsync(Call call, DispatchRecommendationResult result, string createdByUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (call == null || result == null || !result.MatchedRunCardId.HasValue)
				return;

			var activation = new RunCardActivation
			{
				DepartmentId = call.DepartmentId,
				CallId = call.CallId,
				RunCardId = result.MatchedRunCardId.Value,
				AlarmLevel = result.AlarmLevel,
				ModeUsed = (int)result.ModeUsed,
				WasAutoDispatched = result.AutoDispatch,
				ResultJson = JsonConvert.SerializeObject(result),
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = createdByUserId
			};

			await _runCardActivationsRepository.SaveOrUpdateAsync(activation, cancellationToken, true);

			_eventAggregator.SendMessage<RunCardActivatedEvent>(new RunCardActivatedEvent
			{
				DepartmentId = call.DepartmentId,
				CallId = call.CallId,
				RunCardId = result.MatchedRunCardId.Value,
				RunCardName = result.MatchedRunCardName,
				AlarmLevel = result.AlarmLevel,
				ModeUsed = (int)result.ModeUsed,
				WasAutoDispatched = result.AutoDispatch,
				UnitIds = result.Units.Select(u => u.UnitId).ToList(),
				UserIds = result.Personnel.Select(p => p.UserId).ToList()
			});

			if (result.HasShortfalls)
			{
				_eventAggregator.SendMessage<DispatchShortfallEvent>(new DispatchShortfallEvent
				{
					DepartmentId = call.DepartmentId,
					CallId = call.CallId,
					RunCardId = result.MatchedRunCardId.Value,
					AlarmLevel = result.AlarmLevel,
					Shortfalls = result.Shortfalls
				});
			}

			if (result.MoveUps.Any())
			{
				_eventAggregator.SendMessage<StationCoverageGapEvent>(new StationCoverageGapEvent
				{
					DepartmentId = call.DepartmentId,
					CallId = call.CallId,
					MoveUps = result.MoveUps
				});
			}
		}

		#region Candidate pools

		private sealed class UnitCandidate
		{
			public Unit Unit { get; set; }
			public int UnitTypeId { get; set; }
			public string UnitTypeName { get; set; }
			public string StatusText { get; set; }
			public int? StaffingLevel { get; set; }
			public bool InRestPeriod { get; set; }
			public double? Latitude { get; set; }
			public double? Longitude { get; set; }
			public DateTime? LocationTimestamp { get; set; }
			public bool LocationIsStale { get; set; }
		}

		private sealed class PersonnelCandidate
		{
			public string UserId { get; set; }
			public List<int> RoleIds { get; set; } = new List<int>();
			public string StatusText { get; set; }
			public int? StationGroupId { get; set; }
			public string StationGroupName { get; set; }
			public bool InRestPeriod { get; set; }
			public double? Latitude { get; set; }
			public double? Longitude { get; set; }
			public DateTime? LocationTimestamp { get; set; }
			public bool LocationIsStale { get; set; }
		}

		private sealed class RecommendationContext
		{
			public DispatchRecommendationRequest Request { get; set; }
			public RunCard Card { get; set; }
			public RunCardAlarmLevel Level { get; set; }
			public DispatchRecommendationConfig Config { get; set; }
			public int StaffingGate { get; set; }
			public DateTime Now { get; set; }
			public DispatchRecommendationResult Result { get; set; }
			/// <summary>
			/// Carried on the context rather than threaded through every private fill
			/// method. Checked at the boundaries that repeat slow external I/O (routed
			/// ETA lookups, per-station geocoding) so an abandoned request stops calling
			/// the mapping provider.
			/// </summary>
			public CancellationToken CancellationToken { get; set; }
			public List<UnitCandidate> UnitCandidates { get; set; } = new List<UnitCandidate>();
			public List<PersonnelCandidate> PersonnelCandidates { get; set; } = new List<PersonnelCandidate>();
			public Dictionary<int, DateTime> UnitLastDispatched { get; set; } = new Dictionary<int, DateTime>();
			public Dictionary<string, DateTime> UserLastDispatched { get; set; } = new Dictionary<string, DateTime>();
			public GeoMath.GeoPoint? CallLocation { get; set; }
			public HashSet<int> UnitTypesWithStaffingExclusions { get; set; } = new HashSet<int>();
		}

		private async Task BuildUnitCandidatesAsync(RecommendationContext context)
		{
			var departmentId = context.Request.DepartmentId;

			var units = await _unitsService.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>();
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitState>();
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>();
			var customStates = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(departmentId) ?? new List<CustomState>();

			var stateByUnit = states.GroupBy(s => s.UnitId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First());
			var typeByName = BuildUnitTypeLookup(unitTypes);
			var customDetails = BuildCustomDetailMap(customStates);

			var selections = (context.Card.AvailabilitySelections ?? new List<RunCardAvailabilitySelection>())
				.Where(s => s.SelectionType == (int)RunCardSelectionTypes.UnitStatus)
				.ToList();

			Dictionary<int, UnitRoleStaffingResult> staffing = null;
			if (context.StaffingGate > 0)
				staffing = await _unitsService.GetUnitStaffingForDepartmentAsync(departmentId) ?? new Dictionary<int, UnitRoleStaffingResult>();

			var alreadyDispatched = new HashSet<int>(context.Request.AlreadyDispatchedUnitIds ?? new List<int>());

			foreach (var unit in units)
			{
				if (alreadyDispatched.Contains(unit.UnitId))
					continue;

				if (string.IsNullOrWhiteSpace(unit.Type) || !typeByName.TryGetValue(unit.Type.Trim(), out var unitType))
					continue;

				var stateId = stateByUnit.TryGetValue(unit.UnitId, out var unitState) ? unitState.State : (int)UnitStateTypes.Available;
				var isCustom = customDetails.ContainsKey(stateId);

				if (!IsUnitStateDispatchable(stateId, isCustom, unitType.UnitTypeId, selections, customDetails))
					continue;

				int? staffingLevel = null;
				if (staffing != null)
				{
					staffing.TryGetValue(unit.UnitId, out var staffingResult);
					var level = staffingResult?.Level ?? UnitStaffingLevel.Unknown;
					staffingLevel = (int)level;

					// Units with no defined seats (Unknown) always pass — departments not
					// using unit roles shouldn't be locked out by the staffing gate.
					if (level != UnitStaffingLevel.Unknown && (int)level < context.StaffingGate)
					{
						context.UnitTypesWithStaffingExclusions.Add(unitType.UnitTypeId);
						context.Result.Notes.Add($"Unit '{unit.Name}' excluded: staffing {level} is below the required minimum.");
						continue;
					}
				}

				context.UnitCandidates.Add(new UnitCandidate
				{
					Unit = unit,
					UnitTypeId = unitType.UnitTypeId,
					UnitTypeName = unitType.Type,
					StatusText = GetUnitStatusText(stateId, isCustom, customDetails),
					StaffingLevel = staffingLevel
				});
			}
		}

		private async Task BuildPersonnelCandidatesAsync(RecommendationContext context)
		{
			var departmentId = context.Request.DepartmentId;

			var rolesByUser = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(departmentId) ?? new List<ActionLog>();
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(departmentId) ?? new List<UserState>();
			var groupByUser = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(departmentId) ?? new Dictionary<string, DepartmentGroup>();

			var personnelCustomState = await _customStateService.GetActivePersonnelStateForDepartmentAsync(departmentId);
			var staffingCustomState = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(departmentId);

			var personnelDetails = BuildCustomDetailMap(personnelCustomState);
			var staffingDetails = BuildCustomDetailMap(staffingCustomState);

			var logByUser = actionLogs.Where(l => !string.IsNullOrWhiteSpace(l.UserId))
				.GroupBy(l => l.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First());
			var stateByUser = userStates.Where(s => !string.IsNullOrWhiteSpace(s.UserId))
				.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First());

			var allSelections = context.Card.AvailabilitySelections ?? new List<RunCardAvailabilitySelection>();
			var statusSelections = allSelections.Where(s => s.SelectionType == (int)RunCardSelectionTypes.PersonnelStatus).ToList();
			var staffingSelections = allSelections.Where(s => s.SelectionType == (int)RunCardSelectionTypes.PersonnelStaffing).ToList();

			var alreadyDispatched = new HashSet<string>(context.Request.AlreadyDispatchedUserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

			foreach (var pair in rolesByUser)
			{
				var userId = pair.Key;

				if (string.IsNullOrWhiteSpace(userId) || alreadyDispatched.Contains(userId))
					continue;

				logByUser.TryGetValue(userId, out var lastLog);
				stateByUser.TryGetValue(userId, out var lastState);

				if (!IsPersonnelStatusDispatchable(lastLog, statusSelections, personnelDetails))
					continue;

				if (!IsPersonnelStaffingDispatchable(lastState, staffingSelections, staffingDetails))
					continue;

				groupByUser.TryGetValue(userId, out var group);

				var candidate = new PersonnelCandidate
				{
					UserId = userId,
					RoleIds = pair.Value?.Select(r => r.PersonnelRoleId).ToList() ?? new List<int>(),
					StatusText = GetPersonnelStatusText(lastLog, personnelDetails),
					StationGroupId = group?.DepartmentGroupId,
					StationGroupName = group?.Name
				};

				if (lastLog != null)
				{
					var coordinates = lastLog.GetCoordinates();
					if (coordinates != null && coordinates.Latitude.HasValue && coordinates.Longitude.HasValue)
					{
						candidate.Latitude = coordinates.Latitude;
						candidate.Longitude = coordinates.Longitude;
						candidate.LocationTimestamp = lastLog.Timestamp;
					}
				}

				context.PersonnelCandidates.Add(candidate);
			}
		}

		private static Dictionary<string, UnitType> BuildUnitTypeLookup(List<UnitType> unitTypes)
		{
			var lookup = new Dictionary<string, UnitType>(StringComparer.OrdinalIgnoreCase);

			foreach (var unitType in unitTypes)
			{
				if (string.IsNullOrWhiteSpace(unitType?.Type))
					continue;

				lookup[unitType.Type.Trim()] = unitType;
			}

			return lookup;
		}

		private static Dictionary<int, CustomStateDetail> BuildCustomDetailMap(List<CustomState> states)
		{
			var map = new Dictionary<int, CustomStateDetail>();

			foreach (var state in states ?? new List<CustomState>())
				MergeCustomDetails(map, state);

			return map;
		}

		private static Dictionary<int, CustomStateDetail> BuildCustomDetailMap(CustomState state)
		{
			var map = new Dictionary<int, CustomStateDetail>();
			MergeCustomDetails(map, state);
			return map;
		}

		private static void MergeCustomDetails(Dictionary<int, CustomStateDetail> map, CustomState state)
		{
			if (state == null)
				return;

			foreach (var detail in state.GetActiveDetails() ?? new List<CustomStateDetail>())
				map[detail.CustomStateDetailId] = detail;
		}

		private static bool IsUnitStateDispatchable(int stateId, bool isCustom, int unitTypeId,
			List<RunCardAvailabilitySelection> selections, Dictionary<int, CustomStateDetail> customDetails)
		{
			// Typed selections beat generic (null unit type) selections; either set, when
			// present, is authoritative for this unit type.
			var applicable = selections.Where(s => s.UnitTypeId == unitTypeId).ToList();
			if (!applicable.Any())
				applicable = selections.Where(s => !s.UnitTypeId.HasValue).ToList();

			if (applicable.Any())
				return applicable.Any(s => s.StateId == stateId && s.IsCustomState == isCustom);

			// No selections: fall back to the availability matrix; Delayed still counts as
			// dispatchable (mirrors platform reporting's unit availability policy).
			var availability = isCustom
				? AvailabilityMatrix.ForCustomBaseType((int)(customDetails[stateId].BaseType))
				: AvailabilityMatrix.ForUnitStateType(stateId);

			return availability == AvailabilityClass.Available || availability == AvailabilityClass.Delayed;
		}

		private static bool IsPersonnelStatusDispatchable(ActionLog lastLog,
			List<RunCardAvailabilitySelection> selections, Dictionary<int, CustomStateDetail> customDetails)
		{
			// No status on file means the member never set one — the manual dispatch grids
			// show them, so the engine treats them as standing by.
			if (lastLog == null)
				return !selections.Any() || selections.Any(s => !s.IsCustomState && s.StateId == (int)ActionTypes.StandingBy);

			var stateId = lastLog.ActionTypeId;
			var isCustom = customDetails.ContainsKey(stateId);

			if (selections.Any())
				return selections.Any(s => s.StateId == stateId && s.IsCustomState == isCustom);

			var availability = isCustom
				? AvailabilityMatrix.ForCustomBaseType((int)(customDetails[stateId].BaseType))
				: AvailabilityMatrix.ForBuiltInPersonnelActionType(stateId);

			return availability == AvailabilityClass.Available || availability == AvailabilityClass.Delayed;
		}

		private static bool IsPersonnelStaffingDispatchable(UserState lastState,
			List<RunCardAvailabilitySelection> selections, Dictionary<int, CustomStateDetail> customDetails)
		{
			// No staffing row defaults to Available (matches UserStateService semantics).
			if (lastState == null)
				return !selections.Any() || selections.Any(s => !s.IsCustomState && s.StateId == (int)UserStateTypes.Available);

			var stateId = lastState.State;
			var isCustom = customDetails.ContainsKey(stateId);

			if (selections.Any())
				return selections.Any(s => s.StateId == stateId && s.IsCustomState == isCustom);

			if (isCustom)
			{
				var availability = AvailabilityMatrix.ForCustomBaseType((int)(customDetails[stateId].BaseType));
				return availability == AvailabilityClass.Available || availability == AvailabilityClass.Delayed;
			}

			return stateId == (int)UserStateTypes.Available
				|| stateId == (int)UserStateTypes.OnShift
				|| stateId == (int)UserStateTypes.Delayed;
		}

		private static string GetUnitStatusText(int stateId, bool isCustom, Dictionary<int, CustomStateDetail> customDetails)
		{
			if (isCustom && customDetails.TryGetValue(stateId, out var detail))
				return detail.ButtonText;

			if (Enum.IsDefined(typeof(UnitStateTypes), stateId))
				return ((UnitStateTypes)stateId).ToString();

			return stateId.ToString();
		}

		private static string GetPersonnelStatusText(ActionLog lastLog, Dictionary<int, CustomStateDetail> customDetails)
		{
			if (lastLog == null)
				return ActionTypes.StandingBy.ToString();

			if (customDetails.TryGetValue(lastLog.ActionTypeId, out var detail))
				return detail.ButtonText;

			if (Enum.IsDefined(typeof(ActionTypes), lastLog.ActionTypeId))
				return ((ActionTypes)lastLog.ActionTypeId).ToString();

			return lastLog.ActionTypeId.ToString();
		}

		private static GeoMath.GeoPoint? ResolveCallLocation(DispatchRecommendationRequest request)
		{
			if (request.Latitude.HasValue && request.Longitude.HasValue
				&& !(request.Latitude.Value == 0 && request.Longitude.Value == 0))
				return new GeoMath.GeoPoint(request.Latitude.Value, request.Longitude.Value);

			return null;
		}

		private bool IsUnitInRestPeriod(RecommendationContext context, int unitId)
		{
			if (context.Config.RestPeriodMinutes <= 0)
				return false;

			return context.UnitLastDispatched.TryGetValue(unitId, out var last)
				&& (context.Now - last).TotalMinutes < context.Config.RestPeriodMinutes;
		}

		private bool IsUserInRestPeriod(RecommendationContext context, string userId)
		{
			if (context.Config.RestPeriodMinutes <= 0)
				return false;

			return context.UserLastDispatched.TryGetValue(userId, out var last)
				&& (context.Now - last).TotalMinutes < context.Config.RestPeriodMinutes;
		}

		#endregion

		#region Station-based selection

		private async Task FillStationBasedAsync(RecommendationContext context)
		{
			var request = context.Request;
			var anchor = context.CallLocation;

			if (anchor == null && context.Card.HomeStationGroupId.HasValue)
			{
				var homeStation = await _departmentGroupsService.GetGroupByIdAsync(context.Card.HomeStationGroupId.Value, false);
				var homePoint = await _geoService.GetStationCoordinatesAsync(homeStation);

				if (homePoint != null)
				{
					anchor = homePoint;
					context.Result.Notes.Add($"Call has no location; cascading from the run card's home station '{homeStation?.Name}'.");
				}
			}

			if (anchor == null)
			{
				AddAllRequirementShortfalls(context, RequirementShortfallReasons.NoLocationData);
				context.Result.Notes.Add("Call has no usable location and the run card has no home station; station-based selection cannot run.");
				return;
			}

			var stations = await _geoService.OrderStationsByDistanceAsync(request.DepartmentId, anchor.Value.Latitude, anchor.Value.Longitude);

			if (stations == null || !stations.Any())
			{
				AddAllRequirementShortfalls(context, RequirementShortfallReasons.StationsExhausted);
				context.Result.Notes.Add("No station groups with usable coordinates or geofences exist; station-based selection cannot run.");
				return;
			}

			// Containing stations first (nearest containing wins), then everything else by distance.
			var ordered = stations.Where(s => s.ContainsPoint).Concat(stations.Where(s => !s.ContainsPoint)).ToList();

			if (context.CallLocation != null && !stations.Any(s => s.ContainsPoint))
				context.Result.Notes.Add("Call location is not inside any station's response area; filling from the nearest station outward.");

			var dispatchShift = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(request.DepartmentId);
			var stationRosters = await BuildStationRostersAsync(context, ordered, dispatchShift);

			foreach (var requirement in context.Level.UnitRequirements ?? new List<RunCardUnitRequirement>())
				FillUnitRequirementFromStations(context, requirement, ordered);

			foreach (var requirement in context.Level.RoleRequirements ?? new List<RunCardRoleRequirement>())
				FillRoleRequirementFromStations(context, requirement, ordered, stationRosters);
		}

		private async Task<Dictionary<int, HashSet<string>>> BuildStationRostersAsync(RecommendationContext context,
			List<StationDistanceResult> stations, bool dispatchShiftInsteadOfGroup)
		{
			var rosters = new Dictionary<int, HashSet<string>>();

			if (!dispatchShiftInsteadOfGroup)
			{
				foreach (var candidate in context.PersonnelCandidates)
				{
					if (!candidate.StationGroupId.HasValue)
						continue;

					if (!rosters.TryGetValue(candidate.StationGroupId.Value, out var roster))
						rosters[candidate.StationGroupId.Value] = roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

					roster.Add(candidate.UserId);
				}

				return rosters;
			}

			// Shift-based departments dispatch today's shift roster instead of the whole
			// group (same policy CallDispatchStatusService applies to group dispatches).
			foreach (var station in stations)
			{
				var signups = await _shiftsService.GetShiftSignupsByDepartmentGroupIdAndDayAsync(station.Station.DepartmentGroupId, context.Now.Date);
				var roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				foreach (var signup in signups ?? new List<ShiftSignup>())
				{
					if (!string.IsNullOrWhiteSpace(signup.UserId))
						roster.Add(signup.UserId);
				}

				rosters[station.Station.DepartmentGroupId] = roster;
			}

			return rosters;
		}

		private void FillUnitRequirementFromStations(RecommendationContext context, RunCardUnitRequirement requirement, List<StationDistanceResult> stations)
		{
			var picked = new List<UnitCandidate>();
			var pool = context.UnitCandidates
				.Where(c => c.UnitTypeId == requirement.UnitTypeId)
				.Where(c => context.Result.Units.All(u => u.UnitId != c.Unit.UnitId))
				.ToList();

			if (!pool.Any())
			{
				AddUnitShortfall(context, requirement, 0, RequirementShortfallReasons.NoCandidatesAvailable);
				return;
			}

			// Two passes: rested resources anywhere in the cascade beat in-rest resources
			// at nearer stations — that's the whole point of the rest period.
			foreach (var allowRestPeriod in new[] { false, true })
			{
				for (int depth = 0; depth < stations.Count && picked.Count < requirement.RequiredCount; depth++)
				{
					var station = stations[depth];
					var stationUnits = pool
						.Where(c => c.Unit.StationGroupId == station.Station.DepartmentGroupId)
						.Where(c => picked.All(p => p.Unit.UnitId != c.Unit.UnitId))
						.Where(c => IsUnitInRestPeriod(context, c.Unit.UnitId) == allowRestPeriod)
						.OrderBy(c => c.Unit.Name, StringComparer.OrdinalIgnoreCase)
						.ToList();

					foreach (var candidate in stationUnits)
					{
						if (picked.Count >= requirement.RequiredCount)
							break;

						picked.Add(candidate);

						context.Result.Units.Add(new UnitRecommendation
						{
							UnitId = candidate.Unit.UnitId,
							UnitName = candidate.Unit.Name,
							UnitTypeId = candidate.UnitTypeId,
							UnitTypeName = candidate.UnitTypeName,
							StationGroupId = station.Station.DepartmentGroupId,
							StationGroupName = station.Station.Name,
							SelectionReason = allowRestPeriod
								? RecommendationSelectionReasons.RestPeriodOverridden
								: (station.ContainsPoint && depth == 0 ? RecommendationSelectionReasons.InGeofence : RecommendationSelectionReasons.CascadeStation),
							CascadeDepth = depth,
							DistanceMeters = station.DistanceMeters,
							CurrentStatusText = candidate.StatusText,
							StaffingLevel = candidate.StaffingLevel,
							SatisfiesRequirementId = requirement.RunCardUnitRequirementId
						});

						if (allowRestPeriod)
							context.Result.Notes.Add($"Unit '{candidate.Unit.Name}' is inside its rest period but was needed to fill {candidate.UnitTypeName}.");
					}
				}

				if (picked.Count >= requirement.RequiredCount)
					break;
			}

			if (picked.Count < requirement.RequiredCount)
				AddUnitShortfall(context, requirement, picked.Count, RequirementShortfallReasons.StationsExhausted);
		}

		private void FillRoleRequirementFromStations(RecommendationContext context, RunCardRoleRequirement requirement,
			List<StationDistanceResult> stations, Dictionary<int, HashSet<string>> stationRosters)
		{
			var pickedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var pool = context.PersonnelCandidates
				.Where(c => c.RoleIds.Contains(requirement.PersonnelRoleId))
				.Where(c => context.Result.Personnel.All(p => !string.Equals(p.UserId, c.UserId, StringComparison.OrdinalIgnoreCase)))
				.ToList();

			if (!pool.Any())
			{
				AddRoleShortfall(context, requirement, 0, RequirementShortfallReasons.NoCandidatesAvailable);
				return;
			}

			foreach (var allowRestPeriod in new[] { false, true })
			{
				for (int depth = 0; depth < stations.Count && pickedUsers.Count < requirement.RequiredCount; depth++)
				{
					var station = stations[depth];

					if (!stationRosters.TryGetValue(station.Station.DepartmentGroupId, out var roster))
						continue;

					var stationPeople = pool
						.Where(c => roster.Contains(c.UserId) && !pickedUsers.Contains(c.UserId))
						.Where(c => IsUserInRestPeriod(context, c.UserId) == allowRestPeriod)
						.OrderBy(c => c.UserId, StringComparer.OrdinalIgnoreCase)
						.ToList();

					foreach (var candidate in stationPeople)
					{
						if (pickedUsers.Count >= requirement.RequiredCount)
							break;

						pickedUsers.Add(candidate.UserId);

						context.Result.Personnel.Add(new PersonnelRecommendation
						{
							UserId = candidate.UserId,
							RoleId = requirement.PersonnelRoleId,
							StationGroupId = station.Station.DepartmentGroupId,
							StationGroupName = station.Station.Name,
							SelectionReason = allowRestPeriod
								? RecommendationSelectionReasons.RestPeriodOverridden
								: (station.ContainsPoint && depth == 0 ? RecommendationSelectionReasons.InGeofence : RecommendationSelectionReasons.CascadeStation),
							CascadeDepth = depth,
							DistanceMeters = station.DistanceMeters,
							CurrentStatusText = candidate.StatusText,
							SatisfiesRequirementId = requirement.RunCardRoleRequirementId
						});
					}
				}

				if (pickedUsers.Count >= requirement.RequiredCount)
					break;
			}

			if (pickedUsers.Count < requirement.RequiredCount)
				AddRoleShortfall(context, requirement, pickedUsers.Count, RequirementShortfallReasons.StationsExhausted);
		}

		#endregion

		#region Closest-unit selection

		private async Task FillClosestUnitAsync(RecommendationContext context)
		{
			var request = context.Request;
			var anchor = context.CallLocation;

			if (anchor == null && context.Card.HomeStationGroupId.HasValue)
			{
				var homeStation = await _departmentGroupsService.GetGroupByIdAsync(context.Card.HomeStationGroupId.Value, false);
				anchor = await _geoService.GetStationCoordinatesAsync(homeStation);
			}

			if (anchor == null)
			{
				AddAllRequirementShortfalls(context, RequirementShortfallReasons.NoLocationData);
				context.Result.Notes.Add("Call has no usable location; closest-unit selection cannot run.");
				return;
			}

			await AttachUnitLocationsAsync(context);
			AttachPersonnelLocations(context, await _personnelLocationResolver.GetLatestLocationsAsync(
				request.DepartmentId, context.Config.PersonnelMaxLocationAgeSeconds, context.Now));

			foreach (var requirement in context.Level.UnitRequirements ?? new List<RunCardUnitRequirement>())
				await FillUnitRequirementByProximityAsync(context, requirement, anchor.Value);

			foreach (var requirement in context.Level.RoleRequirements ?? new List<RunCardRoleRequirement>())
				await FillRoleRequirementByProximityAsync(context, requirement, anchor.Value);
		}

		private async Task AttachUnitLocationsAsync(RecommendationContext context)
		{
			var locations = await _unitsService.GetLatestUnitLocationsAsync(context.Request.DepartmentId) ?? new List<UnitsLocation>();

			var latestByUnit = locations
				.Where(l => l != null && l.IsValidFix != false && !(l.Latitude == 0 && l.Longitude == 0))
				.GroupBy(l => l.UnitId)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First());

			foreach (var candidate in context.UnitCandidates)
			{
				if (!latestByUnit.TryGetValue(candidate.Unit.UnitId, out var location))
					continue;

				candidate.Latitude = (double)location.Latitude;
				candidate.Longitude = (double)location.Longitude;
				candidate.LocationTimestamp = location.Timestamp;

				if (context.Config.MaxLocationAgeSeconds > 0)
					candidate.LocationIsStale = (context.Now - location.Timestamp).TotalSeconds > context.Config.MaxLocationAgeSeconds;
			}
		}

		private static void AttachPersonnelLocations(RecommendationContext context, Dictionary<string, ResolvedPersonnelLocation> locations)
		{
			foreach (var candidate in context.PersonnelCandidates)
			{
				if (locations == null || !locations.TryGetValue(candidate.UserId, out var location))
					continue;

				// The resolver may be fresher than the candidate's ActionLog fix.
				if (!candidate.LocationTimestamp.HasValue || location.Timestamp > candidate.LocationTimestamp.Value)
				{
					candidate.Latitude = location.Latitude;
					candidate.Longitude = location.Longitude;
					candidate.LocationTimestamp = location.Timestamp;
					candidate.LocationIsStale = location.IsStale;
				}
			}
		}

		private async Task FillUnitRequirementByProximityAsync(RecommendationContext context, RunCardUnitRequirement requirement, GeoMath.GeoPoint anchor)
		{
			var pool = context.UnitCandidates
				.Where(c => c.UnitTypeId == requirement.UnitTypeId)
				.Where(c => context.Result.Units.All(u => u.UnitId != c.Unit.UnitId))
				.ToList();

			if (!pool.Any())
			{
				AddUnitShortfall(context, requirement, 0, RequirementShortfallReasons.NoCandidatesAvailable);
				return;
			}

			var located = pool.Where(c => c.Latitude.HasValue && c.Longitude.HasValue).ToList();
			var unlocated = pool.Count - located.Count;

			if (!context.Config.IncludeStaleLocations)
			{
				var fresh = located.Where(c => !c.LocationIsStale).ToList();

				if (fresh.Count < located.Count)
					context.Result.Notes.Add($"{located.Count - fresh.Count} '{pool.First().UnitTypeName}' candidate location fix(es) were too old and excluded.");

				located = fresh;
			}

			var ranked = located
				.Select(c => new
				{
					Candidate = c,
					Distance = GeoMath.HaversineMeters(anchor.Latitude, anchor.Longitude, c.Latitude.Value, c.Longitude.Value)
				})
				.Where(x => context.Config.MaxRadiusMeters <= 0 || x.Distance <= context.Config.MaxRadiusMeters)
				.OrderBy(x => IsUnitInRestPeriod(context, x.Candidate.Unit.UnitId) ? 1 : 0)
				.ThenBy(x => x.Distance)
				.ToList();

			var outsideRadius = located.Count - ranked.Count;

			var etaByUnitId = new Dictionary<int, double>();
			if (context.Config.UseRoutedEta && ranked.Any())
			{
				var shortlistSize = Math.Max(1, context.Config.EtaShortlistSize);
				var shortlist = ranked.Take(Math.Max(shortlistSize, requirement.RequiredCount)).ToList();

				foreach (var entry in shortlist)
				{
					context.CancellationToken.ThrowIfCancellationRequested();

					var eta = await _geoService.GetEtaInSecondsAsync(
						FormatPoint(entry.Candidate.Latitude.Value, entry.Candidate.Longitude.Value),
						FormatPoint(anchor.Latitude, anchor.Longitude));

					if (eta >= 0)
						etaByUnitId[entry.Candidate.Unit.UnitId] = eta;
				}

				if (etaByUnitId.Any())
				{
					var reranked = shortlist
						.OrderBy(x => IsUnitInRestPeriod(context, x.Candidate.Unit.UnitId) ? 1 : 0)
						.ThenBy(x => etaByUnitId.TryGetValue(x.Candidate.Unit.UnitId, out var eta) ? eta : double.MaxValue)
						.ThenBy(x => x.Distance)
						.ToList();

					ranked = reranked.Concat(ranked.Skip(shortlist.Count)).ToList();
				}
			}

			var picked = 0;
			foreach (var entry in ranked)
			{
				if (picked >= requirement.RequiredCount)
					break;

				var inRest = IsUnitInRestPeriod(context, entry.Candidate.Unit.UnitId);
				var hasEta = etaByUnitId.TryGetValue(entry.Candidate.Unit.UnitId, out var etaSeconds);
				picked++;

				context.Result.Units.Add(new UnitRecommendation
				{
					UnitId = entry.Candidate.Unit.UnitId,
					UnitName = entry.Candidate.Unit.Name,
					UnitTypeId = entry.Candidate.UnitTypeId,
					UnitTypeName = entry.Candidate.UnitTypeName,
					StationGroupId = entry.Candidate.Unit.StationGroupId,
					StationGroupName = entry.Candidate.Unit.StationGroup?.Name,
					SelectionReason = inRest
						? RecommendationSelectionReasons.RestPeriodOverridden
						: (hasEta ? RecommendationSelectionReasons.ClosestByEta : RecommendationSelectionReasons.ClosestByDistance),
					DistanceMeters = entry.Distance,
					EtaSeconds = hasEta ? etaSeconds : (double?)null,
					LocationTimestamp = entry.Candidate.LocationTimestamp,
					LocationIsStale = entry.Candidate.LocationIsStale,
					CurrentStatusText = entry.Candidate.StatusText,
					StaffingLevel = entry.Candidate.StaffingLevel,
					SatisfiesRequirementId = requirement.RunCardUnitRequirementId
				});

				if (inRest)
					context.Result.Notes.Add($"Unit '{entry.Candidate.Unit.Name}' is inside its rest period but was needed to fill {entry.Candidate.UnitTypeName}.");
			}

			if (picked < requirement.RequiredCount)
			{
				var reason = RequirementShortfallReasons.NoCandidatesAvailable;

				if (outsideRadius > 0)
					reason = RequirementShortfallReasons.OutsideRadius;
				else if (unlocated > 0 || pool.Count > located.Count)
					reason = RequirementShortfallReasons.LocationsTooStale;

				AddUnitShortfall(context, requirement, picked, reason);
			}
		}

		private async Task FillRoleRequirementByProximityAsync(RecommendationContext context, RunCardRoleRequirement requirement, GeoMath.GeoPoint anchor)
		{
			var pool = context.PersonnelCandidates
				.Where(c => c.RoleIds.Contains(requirement.PersonnelRoleId))
				.Where(c => context.Result.Personnel.All(p => !string.Equals(p.UserId, c.UserId, StringComparison.OrdinalIgnoreCase)))
				.ToList();

			if (!pool.Any())
			{
				AddRoleShortfall(context, requirement, 0, RequirementShortfallReasons.NoCandidatesAvailable);
				return;
			}

			var located = pool.Where(c => c.Latitude.HasValue && c.Longitude.HasValue).ToList();
			var unlocated = pool.Count - located.Count;

			if (!context.Config.IncludeStaleLocations)
				located = located.Where(c => !c.LocationIsStale).ToList();

			var ranked = located
				.Select(c => new
				{
					Candidate = c,
					Distance = GeoMath.HaversineMeters(anchor.Latitude, anchor.Longitude, c.Latitude.Value, c.Longitude.Value)
				})
				.Where(x => context.Config.MaxRadiusMeters <= 0 || x.Distance <= context.Config.MaxRadiusMeters)
				.OrderBy(x => IsUserInRestPeriod(context, x.Candidate.UserId) ? 1 : 0)
				.ThenBy(x => x.Distance)
				.ToList();

			var outsideRadius = located.Count - ranked.Count;

			var etaByUserId = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
			if (context.Config.UseRoutedEta && ranked.Any())
			{
				var shortlistSize = Math.Max(1, context.Config.EtaShortlistSize);
				var shortlist = ranked.Take(Math.Max(shortlistSize, requirement.RequiredCount)).ToList();

				foreach (var entry in shortlist)
				{
					context.CancellationToken.ThrowIfCancellationRequested();

					var eta = await _geoService.GetEtaInSecondsAsync(
						FormatPoint(entry.Candidate.Latitude.Value, entry.Candidate.Longitude.Value),
						FormatPoint(anchor.Latitude, anchor.Longitude));

					if (eta >= 0)
						etaByUserId[entry.Candidate.UserId] = eta;
				}

				if (etaByUserId.Any())
				{
					var reranked = shortlist
						.OrderBy(x => IsUserInRestPeriod(context, x.Candidate.UserId) ? 1 : 0)
						.ThenBy(x => etaByUserId.TryGetValue(x.Candidate.UserId, out var eta) ? eta : double.MaxValue)
						.ThenBy(x => x.Distance)
						.ToList();

					ranked = reranked.Concat(ranked.Skip(shortlist.Count)).ToList();
				}
			}

			var picked = 0;
			foreach (var entry in ranked)
			{
				if (picked >= requirement.RequiredCount)
					break;

				var inRest = IsUserInRestPeriod(context, entry.Candidate.UserId);
				var hasEta = etaByUserId.TryGetValue(entry.Candidate.UserId, out var etaSeconds);
				picked++;

				context.Result.Personnel.Add(new PersonnelRecommendation
				{
					UserId = entry.Candidate.UserId,
					RoleId = requirement.PersonnelRoleId,
					StationGroupId = entry.Candidate.StationGroupId,
					StationGroupName = entry.Candidate.StationGroupName,
					SelectionReason = inRest
						? RecommendationSelectionReasons.RestPeriodOverridden
						: (hasEta ? RecommendationSelectionReasons.ClosestByEta : RecommendationSelectionReasons.ClosestByDistance),
					DistanceMeters = entry.Distance,
					EtaSeconds = hasEta ? etaSeconds : (double?)null,
					LocationTimestamp = entry.Candidate.LocationTimestamp,
					LocationIsStale = entry.Candidate.LocationIsStale,
					CurrentStatusText = entry.Candidate.StatusText,
					SatisfiesRequirementId = requirement.RunCardRoleRequirementId
				});
			}

			if (picked < requirement.RequiredCount)
			{
				var reason = RequirementShortfallReasons.NoCandidatesAvailable;

				if (outsideRadius > 0)
					reason = RequirementShortfallReasons.OutsideRadius;
				else if (unlocated > 0 || pool.Count > located.Count)
					reason = RequirementShortfallReasons.LocationsTooStale;

				AddRoleShortfall(context, requirement, picked, reason);
			}
		}

		private static string FormatPoint(double latitude, double longitude)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0},{1}", latitude, longitude);
		}

		#endregion

		#region Move-up / backfill

		private async Task RunMoveUpPassAsync(RecommendationContext context)
		{
			var requirements = await _runCardsService.GetStationCoverageRequirementsForDepartmentAsync(context.Request.DepartmentId);
			var enabled = requirements?.Where(r => r.IsEnabled).ToList();

			if (enabled == null || !enabled.Any())
				return;

			var committedUnitIds = new HashSet<int>(context.Result.Units.Select(u => u.UnitId)
				.Concat(context.Request.AlreadyDispatchedUnitIds ?? new List<int>()));
			var committedUserIds = new HashSet<string>(context.Result.Personnel.Select(p => p.UserId)
				.Concat(context.Request.AlreadyDispatchedUserIds ?? new List<string>()), StringComparer.OrdinalIgnoreCase);

			foreach (var requirement in enabled)
			{
				// Resolving a station's coordinates can fall through to address geocoding,
				// so this loop is external I/O per requirement.
				context.CancellationToken.ThrowIfCancellationRequested();

				var station = await _departmentGroupsService.GetGroupByIdAsync(requirement.DepartmentGroupId, false);

				if (station == null)
					continue;

				var stationPoint = await _geoService.GetStationCoordinatesAsync(station);

				if (requirement.UnitTypeId.HasValue)
					EvaluateUnitCoverage(context, requirement, station, stationPoint, committedUnitIds);
				else if (requirement.PersonnelRoleId.HasValue)
					EvaluateRoleCoverage(context, requirement, station, stationPoint, committedUserIds);
			}
		}

		private void EvaluateUnitCoverage(RecommendationContext context, StationCoverageRequirement requirement,
			DepartmentGroup station, GeoMath.GeoPoint? stationPoint, HashSet<int> committedUnitIds)
		{
			var typeUnits = context.UnitCandidates.Where(c => c.UnitTypeId == requirement.UnitTypeId.Value).ToList();

			List<UnitCandidate> remaining;
			if (UseRadiusCoverage(context, requirement, stationPoint, typeUnits.Any(c => c.Latitude.HasValue)))
			{
				remaining = typeUnits
					.Where(c => !committedUnitIds.Contains(c.Unit.UnitId))
					.Where(c => c.Latitude.HasValue && c.Longitude.HasValue
						&& GeoMath.HaversineMeters(stationPoint.Value.Latitude, stationPoint.Value.Longitude, c.Latitude.Value, c.Longitude.Value) <= requirement.RadiusMeters.Value)
					.ToList();
			}
			else
			{
				remaining = typeUnits
					.Where(c => !committedUnitIds.Contains(c.Unit.UnitId))
					.Where(c => c.Unit.StationGroupId == requirement.DepartmentGroupId)
					.ToList();
			}

			if (remaining.Count >= requirement.MinimumAvailableCount)
				return;

			var donor = context.UnitCandidates
				.Where(c => c.UnitTypeId == requirement.UnitTypeId.Value)
				.Where(c => !committedUnitIds.Contains(c.Unit.UnitId))
				.Where(c => c.Unit.StationGroupId != requirement.DepartmentGroupId)
				.OrderBy(c => DistanceToStation(c, stationPoint))
				.FirstOrDefault();

			var typeName = context.UnitCandidates.FirstOrDefault(c => c.UnitTypeId == requirement.UnitTypeId.Value)?.UnitTypeName;

			context.Result.MoveUps.Add(new MoveUpRecommendation
			{
				StationGroupId = requirement.DepartmentGroupId,
				StationGroupName = station.Name,
				UnitTypeId = requirement.UnitTypeId,
				UnitTypeName = typeName,
				MinimumRequired = requirement.MinimumAvailableCount,
				AvailableAfterDispatch = remaining.Count,
				SuggestedUnitId = donor?.Unit.UnitId,
				SuggestedUnitName = donor?.Unit.Name,
				FromStationGroupId = donor?.Unit.StationGroupId,
				FromStationGroupName = donor?.Unit.StationGroup?.Name,
				DistanceMeters = donor != null ? DistanceToStationOrNull(donor, stationPoint) : null
			});

			context.Result.Notes.Add($"Station '{station.Name}' drops below minimum coverage ({remaining.Count}/{requirement.MinimumAvailableCount} {typeName}); move-up recommended.");
		}

		private void EvaluateRoleCoverage(RecommendationContext context, StationCoverageRequirement requirement,
			DepartmentGroup station, GeoMath.GeoPoint? stationPoint, HashSet<string> committedUserIds)
		{
			var roleHolders = context.PersonnelCandidates
				.Where(c => c.RoleIds.Contains(requirement.PersonnelRoleId.Value))
				.ToList();

			List<PersonnelCandidate> remaining;
			if (UseRadiusCoverage(context, requirement, stationPoint, roleHolders.Any(c => c.Latitude.HasValue)))
			{
				remaining = roleHolders
					.Where(c => !committedUserIds.Contains(c.UserId))
					.Where(c => c.Latitude.HasValue && c.Longitude.HasValue
						&& GeoMath.HaversineMeters(stationPoint.Value.Latitude, stationPoint.Value.Longitude, c.Latitude.Value, c.Longitude.Value) <= requirement.RadiusMeters.Value)
					.ToList();
			}
			else
			{
				remaining = roleHolders
					.Where(c => !committedUserIds.Contains(c.UserId))
					.Where(c => c.StationGroupId == requirement.DepartmentGroupId)
					.ToList();
			}

			if (remaining.Count >= requirement.MinimumAvailableCount)
				return;

			var donor = roleHolders
				.Where(c => !committedUserIds.Contains(c.UserId))
				.Where(c => c.StationGroupId != requirement.DepartmentGroupId)
				.FirstOrDefault();

			context.Result.MoveUps.Add(new MoveUpRecommendation
			{
				StationGroupId = requirement.DepartmentGroupId,
				StationGroupName = station.Name,
				PersonnelRoleId = requirement.PersonnelRoleId,
				MinimumRequired = requirement.MinimumAvailableCount,
				AvailableAfterDispatch = remaining.Count,
				SuggestedUserId = donor?.UserId,
				FromStationGroupId = donor?.StationGroupId,
				FromStationGroupName = donor?.StationGroupName
			});

			context.Result.Notes.Add($"Station '{station.Name}' drops below minimum role coverage ({remaining.Count}/{requirement.MinimumAvailableCount}); move-up recommended.");
		}

		/// <summary>
		/// Whether a coverage requirement should be measured by distance from the station
		/// rather than by station assignment. RadiusMeters is a closest-unit concept —
		/// station-based departments define "at this station" by assignment/geofence — so
		/// the mode is checked explicitly instead of inferring it from whether candidates
		/// happen to carry a fix (units are only located in closest-unit mode, personnel
		/// carry ActionLog coordinates in both, which would otherwise split the behaviour).
		/// Falls back to assignment when no candidate has a location, so a department with
		/// no position data reports real coverage instead of a phantom gap.
		/// </summary>
		private static bool UseRadiusCoverage(RecommendationContext context, StationCoverageRequirement requirement,
			GeoMath.GeoPoint? stationPoint, bool anyCandidateLocated)
		{
			return context.Result.ModeUsed == DispatchRecommendationModes.ClosestUnit
				&& requirement.RadiusMeters.HasValue
				&& requirement.RadiusMeters.Value > 0
				&& stationPoint.HasValue
				&& anyCandidateLocated;
		}

		private static double DistanceToStation(UnitCandidate candidate, GeoMath.GeoPoint? stationPoint)
		{
			return DistanceToStationOrNull(candidate, stationPoint) ?? double.MaxValue;
		}

		private static double? DistanceToStationOrNull(UnitCandidate candidate, GeoMath.GeoPoint? stationPoint)
		{
			if (!stationPoint.HasValue || !candidate.Latitude.HasValue || !candidate.Longitude.HasValue)
				return null;

			return GeoMath.HaversineMeters(stationPoint.Value.Latitude, stationPoint.Value.Longitude, candidate.Latitude.Value, candidate.Longitude.Value);
		}

		#endregion

		#region Shortfalls

		private void AddAllRequirementShortfalls(RecommendationContext context, RequirementShortfallReasons reason)
		{
			foreach (var requirement in context.Level.UnitRequirements ?? new List<RunCardUnitRequirement>())
				AddUnitShortfall(context, requirement, 0, reason);

			foreach (var requirement in context.Level.RoleRequirements ?? new List<RunCardRoleRequirement>())
				AddRoleShortfall(context, requirement, 0, reason);
		}

		private void AddUnitShortfall(RecommendationContext context, RunCardUnitRequirement requirement, int filled, RequirementShortfallReasons reason)
		{
			// When staffing-gate exclusions thinned this unit type's pool, that is the
			// actionable cause to surface, not the generic exhaustion reason.
			if ((reason == RequirementShortfallReasons.NoCandidatesAvailable || reason == RequirementShortfallReasons.StationsExhausted)
				&& context.UnitTypesWithStaffingExclusions.Contains(requirement.UnitTypeId))
				reason = RequirementShortfallReasons.UnitsNotStaffed;

			context.Result.Shortfalls.Add(new RequirementShortfall
			{
				IsUnitRequirement = true,
				RequirementId = requirement.RunCardUnitRequirementId,
				TypeOrRoleId = requirement.UnitTypeId,
				TypeOrRoleName = context.UnitCandidates.FirstOrDefault(c => c.UnitTypeId == requirement.UnitTypeId)?.UnitTypeName,
				AlarmLevel = context.Level.AlarmLevel,
				RequiredCount = requirement.RequiredCount,
				FilledCount = filled,
				Reason = reason
			});
		}

		private void AddRoleShortfall(RecommendationContext context, RunCardRoleRequirement requirement, int filled, RequirementShortfallReasons reason)
		{
			context.Result.Shortfalls.Add(new RequirementShortfall
			{
				IsUnitRequirement = false,
				RequirementId = requirement.RunCardRoleRequirementId,
				TypeOrRoleId = requirement.PersonnelRoleId,
				AlarmLevel = context.Level.AlarmLevel,
				RequiredCount = requirement.RequiredCount,
				FilledCount = filled,
				Reason = reason
			});
		}

		#endregion
	}
}
