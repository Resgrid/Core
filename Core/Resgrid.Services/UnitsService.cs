using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class UnitsService : IUnitsService
	{
		private readonly IGenericDataRepository<Unit> _unitsRepository;
		private readonly IUnitStatesRepository _unitStatesRepository;
		private readonly IGenericDataRepository<UnitLog> _unitLogsRepository;
		private readonly IGenericDataRepository<UnitType> _unitTypesRepository;
		private readonly IGenericDataRepository<UnitRole> _unitRolesRepository;
		private readonly IGenericDataRepository<UnitStateRole> _unitStateRoleRepository;
		private readonly IUnitLocationRepository _unitLocationRepository;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IUserStateService _userStateService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;

		public UnitsService(IGenericDataRepository<Unit> unitsRepository, IUnitStatesRepository unitStatesRepository,
			IGenericDataRepository<UnitLog> unitLogsRepository, IGenericDataRepository<UnitType> unitTypesRepository, ISubscriptionsService subscriptionsService,
			IGenericDataRepository<UnitRole> unitRolesRepository, IGenericDataRepository<UnitStateRole> unitStateRoleRepository, IUserStateService userStateService,
			IEventAggregator eventAggregator, ICustomStateService customStateService, IUnitLocationRepository unitLocationRepository)
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
		}

		public List<Unit> GetAll()
		{
			return _unitsRepository.GetAll().ToList();
		}

		public Unit SaveUnit(Unit unit)
		{
			_unitsRepository.SaveOrUpdate(unit);
			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = unit.DepartmentId });

			return unit;
		}

		public UnitLog SaveUnitLog(UnitLog unitLog)
		{
			_unitLogsRepository.SaveOrUpdate(unitLog);

			return unitLog;
		}

		public List<Unit> GetUnitsForDepartment(int departmentId)
		{
			List<Unit> systemUnts = new List<Unit>();
			var units = (from unit in _unitsRepository.GetAll()
									 where unit.DepartmentId == departmentId
									 select unit).ToList();

			int limit = _subscriptionsService.GetCurrentPlanForDepartment(departmentId).GetLimitForTypeAsInt(PlanLimitTypes.Units);
			int count = units.Count < limit ? units.Count : limit;

			// Only return units up to the plans unit limit
			for (int i = 0; i < count; i++)
			{
				systemUnts.Add(units[i]);
			}

			return systemUnts;
		}

		public List<Unit> GetUnitsForDepartmentUnlimited(int departmentId)
		{
			var units = from unit in _unitsRepository.GetAll()
									where unit.DepartmentId == departmentId
									select unit;

			return units.ToList();
		}

		public Unit GetUnitById(int unitId)
		{
			return _unitsRepository.GetAll().FirstOrDefault(x => x.UnitId == unitId);
		}

		public void DeleteUnit(int unitId)
		{
			var unit = _unitsRepository.GetAll().FirstOrDefault(x => x.UnitId == unitId);

			if (unit != null)
			{
				var states = _unitStatesRepository.GetAll().Where(x => x.UnitId == unitId);

				if (states != null && states.Any())
					_unitStatesRepository.DeleteAll(states);

				_unitsRepository.DeleteOnSubmit(unit);
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = unit.DepartmentId });

			}
		}

		public UnitState GetLastUnitStateByUnitId(int unitId)
		{
			var userState = (from us in _unitStatesRepository.GetAll()
											 where us.UnitId == unitId
											 orderby us.Timestamp descending
											 select us).FirstOrDefault();

			if (userState != null)
				return userState;

			var state = new UnitState();
			state.UnitId = unitId;
			state.Timestamp = DateTime.UtcNow;
			state.State = (int)UnitStateTypes.Available;

			return state;
		}

		public UnitState GetLastUnitStateBeforeId(int unitId, int unitStateId)
		{
			var states = (from us in _unitStatesRepository.GetAll()
										where us.UnitId == unitId && us.UnitStateId > unitStateId
										orderby us.Timestamp descending
										select us).ToList();

			if (states.Count > 0)
			{
				return states.FirstOrDefault();
			}

			UnitState state = new UnitState();
			state.UnitId = unitId;
			state.Timestamp = DateTime.Now.ToUniversalTime();
			state.State = (int)UnitStateTypes.Available;

			return state;
		}

		public List<UnitState> GetAllLatestStatusForUnitsByDepartmentId(int departmentId)
		{
			var states = new List<UnitState>();
			var units = GetUnitsForDepartment(departmentId);
			var currentStates = _unitStatesRepository.GetLatestUnitStatesForDepartment(departmentId);

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

		public List<UnitState> GetAllStatesForUnit(int unitId)
		{
			var unitStates = (from us in _unitStatesRepository.GetAll()
												where us.UnitId == unitId
												orderby us.Timestamp descending
												select us).ToList();

			return unitStates;
		}

		public UnitState GetUnitStateById(int unitStateId)
		{
			return (from us in _unitStatesRepository.GetAll()
							where us.UnitStateId == unitStateId
							select us).FirstOrDefault();
		}

		public List<UnitType> GetUnitTypesForDepartment(int departmentId)
		{
			var types = (from type in _unitTypesRepository.GetAll()
									 where type.DepartmentId == departmentId
									 select type).ToList();

			return types;
		}

		public Unit GetUnitByNameDepartmentId(int departmentId, string name)
		{
			var units = from unit in _unitsRepository.GetAll()
									where unit.DepartmentId == departmentId && unit.Name == name
									select unit;

			return units.FirstOrDefault();
		}

		public UnitType GetUnitTypeById(int unitTypeId)
		{
			return _unitTypesRepository.GetAll().FirstOrDefault(x => x.UnitTypeId == unitTypeId);
		}

		public UnitType AddUnitType(int departmentId, string name)
		{
			return AddUnitType(departmentId, name, 0);
		}

		public UnitType GetUnitTypeByName(int departmentId, string type)
		{
			return _unitTypesRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.Type == type);
		}

		public UnitType AddUnitType(int departmentId, string name, int customStatesId)
		{
			var type = new UnitType();
			type.DepartmentId = departmentId;
			type.Type = name;

			if (customStatesId != 0)
				type.CustomStatesId = customStatesId;

			_unitTypesRepository.SaveOrUpdate(type);

			return type;
		}

		public UnitType SaveUnitType(UnitType unitType)
		{
			_unitTypesRepository.SaveOrUpdate(unitType);

			return unitType;
		}

		public void DeleteUnitType(int unitTypeId)
		{
			var type = GetUnitTypeById(unitTypeId);

			if (type != null)
				_unitTypesRepository.DeleteOnSubmit(type);
		}

		public UnitState SetUnitState(int unitId, int unitStateType)
		{
			var state = new UnitState();
			state.UnitId = unitId;
			state.State = unitStateType;
			state.Timestamp = DateTime.UtcNow;

			_unitStatesRepository.SaveOrUpdate(state);

			var departmentId = _unitsRepository.GetAll().Where(x => x.UnitId == unitId).Select(x => x.DepartmentId).FirstOrDefault();
			_eventAggregator.SendMessage<UnitStatusEvent>(new UnitStatusEvent { DepartmentId = departmentId, Status = state });

			return state;
		}

		public UnitState SetUnitState(UnitState state, int departmentId = 0)
		{
			if (state.Accuracy > 100000)
				state.Accuracy = 100000;

			_unitStatesRepository.SaveOrUpdate(state);

			if (departmentId == 0)
				departmentId = _unitsRepository.GetAll().Where(x => x.UnitId == state.UnitId).Select(x => x.DepartmentId).FirstOrDefault();

			_eventAggregator.SendMessage<UnitStatusEvent>(new UnitStatusEvent { DepartmentId = departmentId, Status = state });
			
			return state;
		}

		public List<UnitLog> GetLogsForUnit(int unitId)
		{
			var logs = from l in _unitLogsRepository.GetAll()
								 where l.UnitId == unitId
								 orderby l.Timestamp descending
								 select l;

			return logs.ToList();
		}

		public List<UnitRole> GetRolesForUnit(int unitId)
		{
			var roles = from r in _unitRolesRepository.GetAll()
									where r.UnitId == unitId
									select r;

			return roles.ToList();
		}

		public UnitRole GetRoleById(int unitRoleId)
		{
			return _unitRolesRepository.GetAll().FirstOrDefault(x => x.UnitRoleId == unitRoleId);
		}

		public List<UnitRole> SetRolesForUnit(int unitId, List<UnitRole> roles)
		{
			if (unitId <= 0)
				throw new ArgumentException("UnitId cannot be null", "unitId");

			if (roles == null || roles.Count == 0)
				throw new ArgumentException("Unit Roles cannot be null or empty", "roles");

			ClearRolesForUnit(unitId);

			foreach (var role in roles)
			{
				role.UnitId = unitId;
			}
			_unitRolesRepository.SaveOrUpdateAll(roles);

			return roles;
		}

		public void ClearRolesForUnit(int unitId)
		{
			if (unitId <= 0)
				throw new ArgumentException("UnitId cannot be null", "unitId");

			var savedRoles = from r in _unitRolesRepository.GetAll()
											 where r.UnitId == unitId
											 select r;

			_unitRolesRepository.DeleteAll(savedRoles);
		}

		public void ClearGroupForUnits(int departmentGroupId)
		{
			if (departmentGroupId <= 0)
				throw new ArgumentException("DepartmentGroupId cannot be null", "departmentGroupId");

			var units = (from r in _unitsRepository.GetAll()
									 where r.StationGroupId == departmentGroupId
									 select r);

			foreach (var unit in units)
			{
				unit.StationGroupId = null;
				unit.StationGroup = null;
			}

			_unitsRepository.SaveOrUpdateAll(units);
		}

		public void AddUnitStateRoleForEvent(int unitStateId, string userId, int roleId, string unitName, DateTime timestamp, string roleName = "Unknown")
		{
			if (unitStateId <= 0)
				throw new ArgumentException("Unit State Id cannot be 0", "unitStateId");

			if (String.IsNullOrWhiteSpace(userId))
				throw new ArgumentException("User Id cannot be an empty Guid", "userId");

			if (roleId <= 0)
				throw new ArgumentException("Role Id cannot be 0", "roleId");

			//var role = GetRoleById(roleId);
			//string roleName = "Unknown";

			//if (role != null)
			//	roleName = role.Name;

			var unitStateRole = new UnitStateRole();
			unitStateRole.UnitStateId = unitStateId;
			unitStateRole.UserId = userId;
			unitStateRole.Role = roleName;

			//_userStateService.CreateUserState(userId, (int)UserStateTypes.Committed, string.Format("On {0}", unitName), timestamp);

			_unitStateRoleRepository.SaveOrUpdate(unitStateRole);
		}

		public void AddAllUnitStateRoles(List<UnitStateRole> roles)
		{
			_unitStateRoleRepository.SaveOrUpdateAll(roles);
		}

		public void DeleteStatesForUnit(int unitId)
		{
			var unitStates = (from us in _unitStatesRepository.GetAll()
												where us.UnitId == unitId
												orderby us.Timestamp descending
												select us).ToList();

			_unitStatesRepository.DeleteAll(unitStates);
		}

		public List<Unit> GetAllUnitsForType(int departmentId, string type)
		{
			var units = (from unit in _unitsRepository.GetAll()
									 where unit.DepartmentId == departmentId && unit.Type == type
									 select unit).ToList();

			return units;
		}

		public List<Unit> GetAllUnitsForGroup(int groupId)
		{
			var units = (from unit in _unitsRepository.GetAll()
									 where unit.StationGroupId == groupId
									 select unit).ToList();

			return units;
		}

		public List<UnitState> GetUnitStatesForCall(int departmentId, int callId)
		{
			List<int> callEnabledStates = new List<int>();
			var states = _customStateService.GetAllCustomStatesForDepartment(departmentId);

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

			var unitStates = (from us in _unitStatesRepository.GetAll()
												where callEnabledStates.Contains(us.State) && us.DestinationId == callId
												select us).ToList();

			return unitStates;
		}

		public UnitLocation AddUnitLocation(UnitLocation location)
		{
			_unitLocationRepository.SoftAddUnitLocation(location);

			return location;
		}

		public UnitLocation GetLatestUnitLocation(int unitId, DateTime? timestamp = null)
		{
			UnitLocation location = null;

			if (timestamp == null)
				location = (from ul in _unitLocationRepository.GetAll()
										where ul.UnitId == unitId
										orderby ul.UnitLocationId descending
										select ul).FirstOrDefault();
			else
			{
				location = (from ul in _unitLocationRepository.GetAll()
										where ul.UnitId == unitId && ul.Timestamp > timestamp.Value
										orderby ul.UnitLocationId descending
										select ul).FirstOrDefault();
			}

			return location;
		}

		public List<UnitStateRole> GetCurrentRolesForUnit(int unitId)
		{
			return _unitStatesRepository.GetCurrentRolesForUnit(unitId);
		}
	}
}
