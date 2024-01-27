using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using static Resgrid.Framework.Testing.TestData;

namespace Resgrid.Services
{
	public class UnitsService : IUnitsService
	{
		private readonly IUnitsRepository _unitsRepository;
		private readonly IUnitStatesRepository _unitStatesRepository;
		private readonly IUnitLogsRepository _unitLogsRepository;
		private readonly IUnitTypesRepository _unitTypesRepository;
		private readonly IUnitRolesRepository _unitRolesRepository;
		private readonly IUnitStateRoleRepository _unitStateRoleRepository;
		private readonly IMongoRepository<UnitsLocation> _unitLocationRepository;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IUserStateService _userStateService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly IUnitActiveRolesRepository _unitActiveRolesRepository;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ILimitsService _limitsService;

		public UnitsService(IUnitsRepository unitsRepository, IUnitStatesRepository unitStatesRepository,
			IUnitLogsRepository unitLogsRepository, IUnitTypesRepository unitTypesRepository, ISubscriptionsService subscriptionsService,
			IUnitRolesRepository unitRolesRepository, IUnitStateRoleRepository unitStateRoleRepository, IUserStateService userStateService,
			IEventAggregator eventAggregator, ICustomStateService customStateService, IMongoRepository<UnitsLocation> unitLocationRepository,
			IUnitActiveRolesRepository unitActiveRolesRepository, IDepartmentGroupsService departmentGroupsService, ILimitsService limitsService)
		{
			_unitsRepository = unitsRepository;
			_unitStatesRepository = unitStatesRepository;
			_unitLogsRepository = unitLogsRepository;
			_unitTypesRepository = unitTypesRepository;
			_subscriptionsService = subscriptionsService;
			_unitRolesRepository = unitRolesRepository;
			_unitStateRoleRepository = unitStateRoleRepository;
			_userStateService = userStateService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_unitLocationRepository = unitLocationRepository;
			_unitActiveRolesRepository = unitActiveRolesRepository;
			_departmentGroupsService = departmentGroupsService;
			_limitsService = limitsService;
		}

		public async Task<List<Unit>> GetAllAsync()
		{
			var items = await _unitsRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<Unit>();
		}

		public async Task<Unit> SaveUnitAsync(Unit unit, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _unitsRepository.SaveOrUpdateAsync(unit, cancellationToken);
			await _limitsService.InvalidateDepartmentsEntityLimitsCache(unit.DepartmentId);

			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = saved.DepartmentId });

			return saved;
		}

		public async Task<UnitLog> SaveUnitLogAsync(UnitLog unitLog, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _unitLogsRepository.SaveOrUpdateAsync(unitLog, cancellationToken);
		}

		public async Task<List<Unit>> GetUnitsForDepartmentAsync(int departmentId)
		{
			List<Unit> systemUnts = new List<Unit>();
			var units = (await _unitsRepository.GetAllUnitsByDepartmentIdAsync(departmentId)).ToList();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId);
			var limit = await _limitsService.GetLimitsForEntityPlanWithFallbackAsync(departmentId);

			if (units != null && units.Any())
			{
				if (units.Count() > limit.UnitsLimit)
					units = units.Take(limit.UnitsLimit).ToList();

				foreach (var unit in units)
				{
					//unit.Roles = await GetRolesForUnitAsync(unit.UnitId);

					if (unit.StationGroupId.HasValue)
						unit.StationGroup = groups.FirstOrDefault(x => x.DepartmentGroupId == unit.StationGroupId.Value);

					systemUnts.Add(unit);
				}
			}

			return systemUnts;
		}

		public async Task<List<Unit>> GetUnitsForDepartmentUnlimitedAsync(int departmentId)
		{
			var units = await _unitsRepository.GetAllUnitsByDepartmentIdAsync(departmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId);

			if (units != null && units.Any())
			{
				foreach (var unit in units)
				{
					//unit.Roles = await GetRolesForUnitAsync(unit.UnitId);

					if (unit.StationGroupId.HasValue)
						unit.StationGroup = groups.FirstOrDefault(x => x.DepartmentGroupId == unit.StationGroupId.Value);
				}
			}

			return units.ToList();
		}

		public async Task<Unit> GetUnitByIdAsync(int unitId)
		{
			return await _unitsRepository.GetByIdAsync(unitId);
		}

		public async Task<bool> DeleteUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var unit = await _unitsRepository.GetByIdAsync(unitId);

			if (unit != null)
			{
				var states = await _unitStatesRepository.GetAllStatesByUnitIdAsync(unitId);

				if (states != null && states.Any())
				{
					foreach (var unitState in states)
					{
						await _unitStatesRepository.DeleteAsync(unitState, cancellationToken);
					}
				}

				await _unitActiveRolesRepository.DeleteActiveRolesByUnitIdAsync(unit.UnitId, cancellationToken);
				await _unitsRepository.DeleteAsync(unit, cancellationToken);
				await _limitsService.InvalidateDepartmentsEntityLimitsCache(unit.DepartmentId);

				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = unit.DepartmentId });

				return true;
			}

			return false;
		}

		public async Task<UnitState> GetLastUnitStateByUnitIdAsync(int unitId)
		{
			var userState = await _unitStatesRepository.GetLastUnitStateByUnitIdAsync(unitId);

			if (userState != null)
				return userState;

			var state = new UnitState();
			state.UnitId = unitId;
			state.Timestamp = DateTime.UtcNow;
			state.State = (int)UnitStateTypes.Available;

			return state;
		}

		public async Task<UnitState> GetLastUnitStateBeforeIdAsync(int unitId, int unitStateId)
		{
			var state = await _unitStatesRepository.GetLastUnitStateBeforeIdAsync(unitId, unitStateId);

			if (state != null)
				return state;
			

			state = new UnitState();
			state.UnitId = unitId;
			state.Timestamp = DateTime.Now.ToUniversalTime();
			state.State = (int)UnitStateTypes.Available;

			return state;
		}

		public async Task<List<UnitState>> GetAllLatestStatusForUnitsByDepartmentIdAsync(int departmentId)
		{
			var states = new List<UnitState>();
			var units = await GetUnitsForDepartmentAsync(departmentId);
			var currentStates = await _unitStatesRepository.GetLatestUnitStatesForDepartmentAsync(departmentId);

			foreach (var unit in units)
			{
				var currentState = currentStates.FirstOrDefault(x => x.UnitId == unit.UnitId);
				if (currentState != null)
				{
					currentState.Unit = unit;
					states.Add(currentState);
				}
				else
				{
					var state = new UnitState();
					state.UnitId = unit.UnitId;
					state.Timestamp = DateTime.UtcNow;
					state.State = (int)UnitStateTypes.Available;
					state.Unit = unit;

					states.Add(state);
				}
			}

			return states;
		}

		public async Task<List<UnitState>> GetAllStatesForUnitAsync(int unitId)
		{
			var unitStates = (from us in await _unitStatesRepository.GetAllStatesByUnitIdAsync(unitId)
				orderby us.Timestamp descending
				select us).ToList();

			return unitStates;
		}

		public async Task<UnitState> GetUnitStateByIdAsync(int unitStateId)
		{
			return await _unitStatesRepository.GetUnitStateByUnitStateIdAsync(unitStateId);
		}

		public async Task<List<UnitType>> GetUnitTypesForDepartmentAsync(int departmentId)
		{
			var types = await _unitTypesRepository.GetAllByDepartmentIdAsync(departmentId);

			return types.ToList();
		}

		public async Task<Unit> GetUnitByNameDepartmentIdAsync(int departmentId, string name)
		{
			return await _unitsRepository.GetUnitByNameDepartmentIdAsync(departmentId, name);
		}

		public async Task<UnitType> GetUnitTypeByIdAsync(int unitTypeId)
		{
			return await _unitTypesRepository.GetByIdAsync(unitTypeId);
		}

		public async Task<UnitType> AddUnitTypeAsync(int departmentId, string name, int? mapIconType, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await AddUnitTypeAsync(departmentId, name, 0, mapIconType, cancellationToken);
		}

		public async Task<UnitType> GetUnitTypeByNameAsync(int departmentId, string type)
		{
			return await _unitTypesRepository.GetUnitByNameDepartmentIdAsync(departmentId, type);
		}

		public async Task<UnitType> AddUnitTypeAsync(int departmentId, string name, int customStatesId, int? mapIconType, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = new UnitType();
			type.DepartmentId = departmentId;
			type.Type = name;

			if (customStatesId != 0)
				type.CustomStatesId = customStatesId;

			if (mapIconType.HasValue && mapIconType.Value >= 0)
				type.MapIconType = mapIconType;

			return await _unitTypesRepository.SaveOrUpdateAsync(type, cancellationToken);
		}

		public async Task<UnitType> SaveUnitTypeAsync(UnitType unitType, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _unitTypesRepository.SaveOrUpdateAsync(unitType, cancellationToken);
		}

		public async Task<bool> DeleteUnitTypeAsync(int unitTypeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = await GetUnitTypeByIdAsync(unitTypeId);

			if (type != null)
				return await _unitTypesRepository.DeleteAsync(type, cancellationToken);

			return false;
		}

		public async Task<UnitState> SetUnitStateAsync(int unitId, int unitStateType, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var state = new UnitState();
			state.UnitId = unitId;
			state.State = unitStateType;
			state.Timestamp = DateTime.UtcNow;

			var activeRoles = await GetActiveRolesForUnitAsync(state.UnitId);

			if (activeRoles != null && activeRoles.Any())
			{
				state.Roles = new List<UnitStateRole>();
				foreach (var activeRole in activeRoles)
				{
					UnitStateRole role = new UnitStateRole();
					role.Role = activeRole.Role;
					role.UserId = activeRole.UserId;

					state.Roles.Add(role);
				}
			}

			var saved = await _unitStatesRepository.SaveOrUpdateAsync(state, cancellationToken);

			_eventAggregator.SendMessage<UnitStatusEvent>(new UnitStatusEvent { DepartmentId = departmentId, Status = saved });

			return saved;
		}

		public async Task<UnitState> SetUnitStateAsync(UnitState state, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (state.Accuracy > 100000)
				state.Accuracy = 100000;

			if (state.Roles == null || !state.Roles.Any())
			{
				var activeRoles = await GetActiveRolesForUnitAsync(state.UnitId);

				if (activeRoles != null && activeRoles.Any())
				{
					state.Roles = new List<UnitStateRole>();
					foreach (var activeRole in activeRoles)
					{
						UnitStateRole role = new UnitStateRole();
						role.Role = activeRole.Role;
						role.UserId = activeRole.UserId;

						state.Roles.Add(role);
					}
				}
			}

			var saved = await _unitStatesRepository.SaveOrUpdateAsync(state, cancellationToken);

			_eventAggregator.SendMessage<UnitStatusEvent>(new UnitStatusEvent { DepartmentId = departmentId, Status = saved });
			
			return state;
		}

		public async Task<List<UnitLog>> GetLogsForUnitAsync(int unitId)
		{
			var logs = await _unitLogsRepository.GetAllLogsByUnitIdAsync(unitId);

			return logs.ToList();
		}

		public async Task<List<UnitRole>> GetRolesForUnitAsync(int unitId)
		{
			var roles = await _unitRolesRepository.GetAllRolesByUnitIdAsync(unitId);

			if (roles != null && roles.Any())
				return roles.ToList();
			else
				return new List<UnitRole>();
		}

		public async Task<UnitRole> GetRoleByIdAsync(int unitRoleId)
		{
			return await _unitRolesRepository.GetByIdAsync(unitRoleId);
		}

		public async Task<List<UnitRole>> SetRolesForUnitAsync(int unitId, List<UnitRole> roles, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (unitId <= 0)
				throw new ArgumentException("UnitId cannot be null", "unitId");

			if (roles == null || roles.Count == 0)
				throw new ArgumentException("Unit Roles cannot be null or empty", "roles");

			await ClearRolesForUnitAsync(unitId, cancellationToken);

			foreach (var role in roles)
			{
				role.UnitId = unitId;
				await _unitRolesRepository.SaveOrUpdateAsync(role, cancellationToken);
			}

			return roles;
		}

		public async Task<bool> ClearRolesForUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (unitId <= 0)
				throw new ArgumentException("UnitId cannot be null", "unitId");

			var savedRoles = await GetRolesForUnitAsync(unitId);

			if (savedRoles != null && savedRoles.Any())
			{
				foreach (var savedRole in savedRoles)
				{
					await _unitRolesRepository.DeleteAsync(savedRole, cancellationToken);
				}

				return true;
			}

			return false;
		}

		public async Task<bool> ClearGroupForUnitsAsync(int departmentGroupId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (departmentGroupId <= 0)
				throw new ArgumentException("DepartmentGroupId cannot be null", "departmentGroupId");

			var units = await _unitsRepository.GetAllUnitsByGroupIdAsync(departmentGroupId);

			foreach (var unit in units)
			{
				unit.StationGroupId = null;
				unit.StationGroup = null;

				
				await _unitsRepository.SaveOrUpdateAsync(unit, cancellationToken);
			}

			return true;
		}

		public async Task<UnitStateRole> AddUnitStateRoleForEventAsync(int unitStateId, string userId, int roleId, string unitName, DateTime timestamp, string roleName = "Unknown", CancellationToken cancellationToken = default(CancellationToken))
		{
			if (unitStateId <= 0)
				throw new ArgumentException("Unit State Id cannot be 0", "unitStateId");

			if (String.IsNullOrWhiteSpace(userId))
				throw new ArgumentException("User Id cannot be an empty Guid", "userId");

			if (roleId <= 0)
				throw new ArgumentException("Role Id cannot be 0", "roleId");

			var unitStateRole = new UnitStateRole();
			unitStateRole.UnitStateId = unitStateId;
			unitStateRole.UserId = userId;
			unitStateRole.Role = roleName;

			return await _unitStateRoleRepository.SaveOrUpdateAsync(unitStateRole, cancellationToken);
		}

		public async Task<bool> AddAllUnitStateRolesAsync(List<UnitStateRole> roles, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (roles != null && roles.Any())
			{
				foreach (var unitStateRole in roles)
				{
					await _unitStateRoleRepository.SaveOrUpdateAsync(unitStateRole, cancellationToken);
				}

				return true;
			}

			return false;
		}

		public async Task<bool> DeleteStatesForUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var unitStates = await _unitStatesRepository.GetAllStatesByUnitIdAsync(unitId);

			if (unitStates != null && unitStates.Any())
			{
				foreach (var unitState in unitStates)
				{
					await _unitStatesRepository.DeleteAsync(unitState, cancellationToken);
				}

				return true;
			}

			return false;
		}

		public async Task<List<Unit>> GetAllUnitsForTypeAsync(int departmentId, string type)
		{
			var units = await _unitsRepository.GetAllUnitsForTypeAsync(departmentId, type);

			return units.ToList();
		}

		public async Task<List<Unit>> GetAllUnitsForGroupAsync(int groupId)
		{
			var units = await _unitsRepository.GetAllUnitsByGroupIdAsync(groupId);

			return units.ToList();
		}

		public async Task<List<UnitState>> GetUnitStatesForCallAsync(int departmentId, int callId)
		{
			List<int> callEnabledStates = new List<int>();
			var states = await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId);

			callEnabledStates.Add((int)UnitStateTypes.Enroute);
			callEnabledStates.Add((int)UnitStateTypes.Committed);
			callEnabledStates.Add((int)UnitStateTypes.Manual);
			callEnabledStates.Add((int)UnitStateTypes.OnScene);
			callEnabledStates.Add((int)UnitStateTypes.Responding);
			callEnabledStates.Add((int)UnitStateTypes.Returning);
			callEnabledStates.Add((int)UnitStateTypes.Released);
			callEnabledStates.Add((int)UnitStateTypes.Staging);
			callEnabledStates.Add((int)UnitStateTypes.Available);

			var nonNullStates = from state in states
								where state.Details != null
								select state;

			callEnabledStates.AddRange(from state in nonNullStates
									   from detail in state.Details
									   where detail.DetailType == (int)CustomStateDetailTypes.Calls || detail.DetailType == (int)CustomStateDetailTypes.CallsAndStations
									   select detail.CustomStateDetailId);

			var unitStates = (from us in await _unitStatesRepository.GetAllStatesByCallIdAsync(callId)
												where callEnabledStates.Contains(us.State)
												select us).ToList();

			return unitStates;
		}

		public async Task<UnitsLocation> AddUnitLocationAsync(UnitsLocation location, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				if (location.Id.Timestamp == 0)
					await _unitLocationRepository.InsertOneAsync(location);
				else
					await _unitLocationRepository.ReplaceOneAsync(location);

				_eventAggregator.SendMessage<UnitLocationUpdatedEvent>(new UnitLocationUpdatedEvent()
					{
						DepartmentId = departmentId,
						UnitId = location.UnitId.ToString(),
						Latitude = double.Parse(location.Latitude.ToString()),
						Longitude = double.Parse(location.Longitude.ToString()),
						RecordId = location.Id.ToString(),
					});
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return location;
		}

		public async Task<UnitsLocation> GetLatestUnitLocationAsync(int unitId, DateTime? timestamp = null)
		{
			try
			{
				var location = _unitLocationRepository.AsQueryable().Where(x => x.UnitId == unitId).OrderByDescending(y => y.Timestamp).FirstOrDefault();

				//var layers = await _personnelLocationRepository.FilterByAsync(filter => filter.DepartmentId == departmentId && filter.Type == (int)type && filter.IsDeleted == false);

				return location;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return null;
		}

		public async Task<List<UnitsLocation>> GetLatestUnitLocationsAsync(int departmentId)
		{
			try
			{

				//var locations = _unitLocationRepository.AsQueryable().Where(x => x.DepartmentId == departmentId).OrderByDescending(y => y.Timestamp).GroupBy(x => x.UnitId);//.FirstOrDefault();

				var locations = _unitLocationRepository
									.AsQueryable()
									.Where(x => x.DepartmentId == departmentId).OrderByDescending(y => y.Timestamp)
									.GroupBy(pv => pv.UnitId, (key, group) => new
									{
										key,
										LatestLocation = group.First()
									});

				//var locations2 = _unitLocationRepository
				//					.AsQueryable()
				//					.Where(x => x.DepartmentId == departmentId).OrderByDescending(y => y.Timestamp);

				//var locations = _unitLocationRepository.AsQueryable()
				//.Where(x => x.DepartmentId == departmentId)
				//.OrderByDescending(y => y.Timestamp)
				//.GroupBy(s => new { s.UnitId })
				//.Select(n => new
				//{
				//	Value = n.Key,
				//	Location = n.First()
				//}).ForEachAsync();

				//var layers = await _personnelLocationRepository.FilterByAsync(filter => filter.DepartmentId == departmentId && filter.Type == (int)type && filter.IsDeleted == false);

				//if (locations != null)
				//	return locations.OrderBy(y => y.Timestamp)
				//					.GroupBy(x => x.UnitId)
				//					.Select(y => y.First()).ToList();

				if (locations != null)
					return locations.Select(x => x.LatestLocation).ToList();

				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return new List<UnitsLocation>();
		}

		public async Task<List<UnitStateRole>> GetCurrentRolesForUnitAsync(int unitId)
		{
			var items = await _unitStateRoleRepository.GetCurrentRolesForUnitAsync(unitId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<UnitStateRole>();
		}

		public async Task<List<UnitActiveRole>> GetActiveRolesForUnitAsync(int unitId)
		{
			var items = await _unitActiveRolesRepository.GetActiveRolesByUnitIdAsync(unitId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<UnitActiveRole>();
		}

		public async Task<List<UnitActiveRole>> GetAllActiveRolesForUnitsByDepartmentIdAsync(int departmentId)
		{
			var items = await _unitActiveRolesRepository.GetAllActiveRolesForUnitsByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<UnitActiveRole>();
		}

		public async Task<UnitActiveRole> SaveActiveRoleAsync(UnitActiveRole role, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _unitActiveRolesRepository.SaveOrUpdateAsync(role, cancellationToken, true);
		}

		public async Task<bool> DeleteActiveRolesForUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _unitActiveRolesRepository.DeleteActiveRolesByUnitIdAsync(unitId, cancellationToken);
		}
	}
}
