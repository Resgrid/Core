using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Providers;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class ActionLogsService : IActionLogsService
	{
		#region Private Members and Constructors
		private static string CacheKey = "DepartmentActionLogs_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromHours(1);

		private readonly IActionLogsRepository _actionLogsRepository;
		private readonly IUsersService _usersService;
		private readonly IGenericDataRepository<DepartmentMember> _departmentMembersRepository;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IGeoService _geoService;
		private readonly ICustomStateService _customStateService;
		private readonly ICacheProvider _cacheProvider;

		public ActionLogsService(IActionLogsRepository actionLogsRepository, IUsersService usersService,
			IGenericDataRepository<DepartmentMember> departmentMembersRepository, IDepartmentGroupsService departmentGroupsService,
			IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator,
			IGeoService geoService, ICustomStateService customStateService, ICacheProvider cacheProvider)
		{
			_actionLogsRepository = actionLogsRepository;
			_usersService = usersService;
			_departmentMembersRepository = departmentMembersRepository;
			_departmentGroupsService = departmentGroupsService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_geoService = geoService;
			_customStateService = customStateService;
			_cacheProvider = cacheProvider;
		}

		#endregion Private Members and Constructors

		public List<ActionLog> GetAllActionLogsForDepartment(int departmentId)
		{
			var logs = from al in _actionLogsRepository.GetAll()
								 where al.DepartmentId == departmentId
								 select al;

			foreach (var actionLog in logs)
			{
				if (actionLog.User == null)
					actionLog.User = _usersService.GetUserById(actionLog.UserId, false);

				if (actionLog.DestinationType.GetValueOrDefault() == 1 || actionLog.DestinationType.GetValueOrDefault() == 2 ||
					actionLog.ActionTypeId == (int)ActionTypes.RespondingToScene || actionLog.ActionTypeId == (int)ActionTypes.RespondingToStation)
					actionLog.Eta = _geoService.GetPersonnelEtaInSeconds(actionLog);
			}

			return logs.ToList();
		}

		public void InvalidateActionLogs(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public List<ActionLog> GetActionLogsForDepartment(int departmentId, bool forceDisableAutoAvailable = false, bool bypassCache = false)
		{
			Func<List<ActionLog>> getActionLogs = delegate ()
			{
				var time = DateTime.UtcNow.AddHours(-1);
				bool disableAutoAvailable = false;

				if (forceDisableAutoAvailable)
					disableAutoAvailable = true;
				else
					disableAutoAvailable = _departmentSettingsService.GetDisableAutoAvailableForDepartment(departmentId, false);

				var statuses = _actionLogsRepository.GetLastActionLogsForDepartment(departmentId, disableAutoAvailable, time);

				var values = statuses.GroupBy(l => l.UserId)
				.Select(g => g.OrderByDescending(l => l.ActionLogId).First())
				.ToList();

				var logs = new List<ActionLog>();

				foreach (var v in values)
				{
					if (v.DestinationId.HasValue && (v.DestinationType.GetValueOrDefault() == 1 || v.DestinationType.GetValueOrDefault() == 2 ||
						v.ActionTypeId == (int)ActionTypes.RespondingToScene || v.ActionTypeId == (int)ActionTypes.RespondingToStation))
					{
						v.Eta = _geoService.GetPersonnelEtaInSeconds(v);
						v.EtaPulledOn = DateTime.UtcNow;
					}

					logs.Add(v);
				}

				return logs;
			};

			// Forcing this to bypass as some users are reporting that the data is incorrect. -SJ
			bypassCache = true;

			if (!bypassCache)
			{
				return _cacheProvider.Retrieve(string.Format(CacheKey, departmentId), getActionLogs, CacheLength);
			}

			return getActionLogs();
		}

		public List<ActionLog> GetAllActionLogsForUser(string userId)
		{
			var logs = from al in _actionLogsRepository.GetAll()
								 where al.UserId == userId
								 select al;

			foreach (var actionLog in logs)
			{
				if (actionLog.User == null)
					actionLog.User = _usersService.GetUserById(actionLog.UserId, false);
			}

			return logs.ToList();
		}

		public List<ActionLog> GetAllActionLogsForUserInDateRange(string userId, DateTime startDate, DateTime endDate)
		{
			var logs = from al in _actionLogsRepository.GetAll()
								 where al.UserId == userId && al.Timestamp >= startDate && al.Timestamp <= endDate
								 select al;

			foreach (var actionLog in logs)
			{
				if (actionLog.User == null)
					actionLog.User = _usersService.GetUserById(actionLog.UserId, false);
			}

			return logs.ToList();
		}

		public ActionLog GetLastActionLogForUser(string userId, int? departmentId = null)
		{
			var time = DateTime.UtcNow.AddHours(-1);
			bool disableAutoAvailable = false;

			if (departmentId.HasValue)
				disableAutoAvailable = _departmentSettingsService.GetDisableAutoAvailableForDepartment(departmentId.Value, false);
			else
				disableAutoAvailable = _departmentSettingsService.GetDisableAutoAvailableForDepartmentByUserId(userId);

			var values = from al in _actionLogsRepository.GetAll()
									 where al.UserId == userId && (disableAutoAvailable || al.Timestamp >= time)
									 orderby al.Timestamp descending
									 select al;

			var actionLog = values.FirstOrDefault();

			if (actionLog != null && actionLog.User == null)
				actionLog.User = _usersService.GetUserById(actionLog.UserId, false);

			return actionLog;
		}

		public ActionLog GetLastActionLogForUserNoLimit(string userId)
		{
			var values = from al in _actionLogsRepository.GetAll()
									 where al.UserId == userId
									 orderby al.Timestamp descending
									 select al;

			var actionLog = values.FirstOrDefault();

			if (actionLog != null && actionLog.User == null)
				actionLog.User = _usersService.GetUserById(actionLog.UserId, false);

			return actionLog;
		}

		public ActionLog GetPreviousActionLog(string userId, int actionLogId)
		{
			var values = from al in _actionLogsRepository.GetAll()
									 where al.UserId == userId && al.ActionLogId < actionLogId
									 orderby al.Timestamp descending
									 select al;

			var actionLog = values.FirstOrDefault();

			if (actionLog == null)
			{
				ActionLog log = new ActionLog();
				log.UserId = userId;
				log.Timestamp = DateTime.UtcNow;
				log.ActionTypeId = (int)ActionTypes.StandingBy;
				log.User = _usersService.GetUserById(log.UserId, false);

				actionLog = log;
			}
			else if (actionLog.User == null)
			{
				actionLog.User = _usersService.GetUserById(actionLog.UserId, false);
			}

			return actionLog;
		}

		public ActionLog SaveActionLog(ActionLog actionLog)
		{
			actionLog.Timestamp = actionLog.Timestamp.ToUniversalTime();
			_actionLogsRepository.SaveOrUpdate(actionLog);

			InvalidateActionLogs(actionLog.DepartmentId);
			
			_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() { Status = actionLog });

			return actionLog;
		}

		public void SaveAllActionLogs(List<ActionLog> actionLogs)
		{
			_actionLogsRepository.SaveOrUpdateAll(actionLogs);

			if (actionLogs != null && actionLogs.Count() > 0)
				InvalidateActionLogs(actionLogs[0].DepartmentId);

			foreach (var actionLog in actionLogs)
			{
				_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() { Status = actionLog });
			}
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType)
		{
			var al = new ActionLog();
			al.ActionTypeId = (int)actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;

			al = SaveActionLog(al);

			return al;
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType, string location)
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;

			al = SaveActionLog(al);

			return al;
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, string note)
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.Note = note;

			al = SaveActionLog(al);

			return al;
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId)
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;

			al = SaveActionLog(al);

			return al;
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId, string note)
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;
			al.Note = note;

			al = SaveActionLog(al);

			return al;
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId, int destinationType)
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;
			al.DestinationType = destinationType;

			al = SaveActionLog(al);

			return al;
		}

		public ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId, int destinationType, string note)
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;
			al.DestinationType = destinationType;
			al.Note = note;

			al = SaveActionLog(al);

			return al;
		}

		public void SetActionForEntireDepartment(int departmentId, int actionType)
		{
			var members = (from dm in _departmentMembersRepository.GetAll()
										 where dm.DepartmentId == departmentId
										 select dm).ToList();

			var logs = new List<ActionLog>();
			foreach (var member in members)
			{
				var al = new ActionLog();
				al.ActionTypeId = actionType;
				al.DepartmentId = departmentId;
				al.UserId = member.UserId;
				al.Timestamp = DateTime.UtcNow;

				logs.Add(al);
			}

			SaveAllActionLogs(logs);
		}

		public void SetActionForDepartmentGroup(int departmentGroupId, int actionType)
		{
			var group = _departmentGroupsService.GetGroupById(departmentGroupId);

			if (group != null)
			{
				var logs = new List<ActionLog>();
				foreach (var member in group.Members)
				{
					var al = new ActionLog();
					al.ActionTypeId = actionType;
					al.DepartmentId = group.DepartmentId;
					al.UserId = member.UserId;
					al.Timestamp = DateTime.UtcNow;

					logs.Add(al);
				}

				SaveAllActionLogs(logs);
			}
		}

		public void DeleteActionLogsForUser(string userId)
		{
			var logs = (from al in _actionLogsRepository.GetAll()
									where al.UserId == userId
									select al).ToList();

			_actionLogsRepository.DeleteAll(logs);
		}

		public void DeleteAllActionLogsForDepartment(int departmentId)
		{
			var actionLogs = (from al in _actionLogsRepository.GetAll()
												where al.DepartmentId == departmentId
												select al).ToList();

			_actionLogsRepository.DeleteAll(actionLogs);
		}

		public List<int> GetActionsCountForLast7DaysForDepartment(int departmentId)
		{
			List<int> actions = new List<int>();
			var startDate = DateTime.UtcNow.AddDays(-7);

			var logsForLast7Days =
				_actionLogsRepository.GetAll()
					.Where(
						x =>
							x.DepartmentId == departmentId &&
							x.Timestamp >= startDate).ToList();

			for (int i = 0; i < 7; i++)
			{
				actions.Add(logsForLast7Days.Count(x => x.Timestamp.ToShortDateString() == DateTime.UtcNow.AddDays(-i).ToShortDateString()));
			}

			return actions;
		}

		public ActionLog GetActionlogById(int actionLogId)
		{
			return _actionLogsRepository.GetActionlogById(actionLogId);
		}

		[CoverageIgnore]
		public Dictionary<string, int> GetNewActionsCountForLast5Days()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();
			var startDate = DateTime.UtcNow.AddDays(-4);

			var logsForLast7Days =
				_actionLogsRepository.GetAll()
					.Where(
						x => x.Timestamp >= startDate).ToList();

			data.Add(DateTime.UtcNow.ToShortDateString(), logsForLast7Days.Count(x => x.Timestamp.ToShortDateString() == DateTime.UtcNow.ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-1).ToShortDateString(), logsForLast7Days.Count(x => x.Timestamp.ToShortDateString() == DateTime.UtcNow.AddDays(-1).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-2).ToShortDateString(), logsForLast7Days.Count(x => x.Timestamp.ToShortDateString() == DateTime.UtcNow.AddDays(-2).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-3).ToShortDateString(), logsForLast7Days.Count(x => x.Timestamp.ToShortDateString() == DateTime.UtcNow.AddDays(-3).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-4).ToShortDateString(), logsForLast7Days.Count(x => x.Timestamp.ToShortDateString() == DateTime.UtcNow.AddDays(-4).ToShortDateString()));

			return data;
		}

		public List<ActionLog> GetActionLogsForCall(int departmentId, int callId)
		{
			List<int> callEnabledStates = new List<int>();
			var states = _customStateService.GetAllCustomStatesForDepartment(departmentId);

			callEnabledStates.Add((int)ActionTypes.OnScene);
			callEnabledStates.Add((int)ActionTypes.RespondingToScene);

			var nonNullStates = from state in states
								where state.Details != null
								select state;

			callEnabledStates.AddRange(from state in nonNullStates from detail in state.Details where detail.DetailType == (int)CustomStateDetailTypes.Calls || detail.DetailType == (int)CustomStateDetailTypes.CallsAndStations select detail.DetailType);

			var unitStates = (from us in _actionLogsRepository.GetAll()
												where callEnabledStates.Contains(us.ActionTypeId) && us.DestinationId == callId
												select us).ToList();

			return unitStates;
		}
	}
}