using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
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
		private readonly IDepartmentMembersRepository _departmentMembersRepository;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IGeoService _geoService;
		private readonly ICustomStateService _customStateService;
		private readonly ICacheProvider _cacheProvider;

		public ActionLogsService(IActionLogsRepository actionLogsRepository, IUsersService usersService,
			IDepartmentMembersRepository departmentMembersRepository, IDepartmentGroupsService departmentGroupsService,
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

		public async Task<List<ActionLog>> GetAllActionLogsForDepartmentAsync(int departmentId)
		{
			var logs = await _actionLogsRepository.GetAllActionLogsForDepartmentAsync(departmentId);

			foreach (var actionLog in logs)
			{
				// TODO: Not let this sit till here, return it from dapper with User object populated. -SJ
				if (actionLog.User == null)
					actionLog.User = _usersService.GetUserById(actionLog.UserId, false);

				if (actionLog.DestinationType.GetValueOrDefault() == 1 || actionLog.DestinationType.GetValueOrDefault() == 2 ||
					actionLog.ActionTypeId == (int)ActionTypes.RespondingToScene || actionLog.ActionTypeId == (int)ActionTypes.RespondingToStation)
					actionLog.Eta = await _geoService.GetPersonnelEtaInSecondsAsync(actionLog);
			}

			return logs.ToList();
		}

		public void InvalidateActionLogs(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public async Task<List<ActionLog>> GetLastActionLogsForDepartmentAsync(int departmentId, bool forceDisableAutoAvailable = false, bool bypassCache = false)
		{
			async Task<List<ActionLog>> getActionLogs()
			{
				var time = DateTime.UtcNow.AddHours(-1);
				bool disableAutoAvailable = false;

				if (forceDisableAutoAvailable)
					disableAutoAvailable = true;
				else
					disableAutoAvailable = await _departmentSettingsService.GetDisableAutoAvailableForDepartmentAsync(departmentId, false);

				var statuses = await _actionLogsRepository.GetLastActionLogsForDepartmentAsync(departmentId, disableAutoAvailable, time);

				var values = statuses.GroupBy(l => l.UserId)
					.Select(g => g.OrderByDescending(l => l.ActionLogId).First())
					.ToList();

				var logs = new List<ActionLog>();

				foreach (var v in values)
				{
					if (v.DestinationId.HasValue && (v.DestinationType.GetValueOrDefault() == 1 || v.DestinationType.GetValueOrDefault() == 2 || v.ActionTypeId == (int) ActionTypes.RespondingToScene || v.ActionTypeId == (int) ActionTypes.RespondingToStation))
					{
						v.Eta = await _geoService.GetPersonnelEtaInSecondsAsync(v);
						v.EtaPulledOn = DateTime.UtcNow;
					}

					logs.Add(v);
				}

				return logs;
			}

			// Forcing this to bypass as some users are reporting that the data is incorrect. -SJ
			bypassCache = true;

			if (!bypassCache)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(CacheKey, departmentId), (Func<Task<List<ActionLog>>>) getActionLogs, CacheLength);
			}

			return await getActionLogs();
		}

		public async Task<List<ActionLog>> GetAllActionLogsForUser(string userId)
		{
			var logs = await _actionLogsRepository.GetAllByUserIdAsync(userId);

			foreach (var actionLog in logs)
			{
				if (actionLog.User == null)
					actionLog.User = _usersService.GetUserById(actionLog.UserId, false);
			}

			return logs.ToList();
		}

		public async Task<List<ActionLog>> GetAllActionLogsForUserInDateRangeAsync(string userId, DateTime startDate, DateTime endDate)
		{
			var logs = await _actionLogsRepository.GetAllActionLogsForUserInDateRangeAsync(userId, startDate, endDate);
			return logs.ToList();
		}

		public async Task<List<ActionLog>> GetAllActionLogsInDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			var logs = await _actionLogsRepository.GetAllActionLogsInDateRangeAsync(departmentId, startDate, endDate);
			return logs.ToList();
		}

		public async Task<ActionLog> GetLastActionLogForUserAsync(string userId, int? departmentId = null)
		{
			var time = DateTime.UtcNow.AddHours(-1);
			bool disableAutoAvailable = false;

			if (departmentId.HasValue)
				disableAutoAvailable = await _departmentSettingsService.GetDisableAutoAvailableForDepartmentAsync(departmentId.Value, false);
			else
				disableAutoAvailable = await _departmentSettingsService.GetDisableAutoAvailableForDepartmentByUserIdAsync(userId);

			return await _actionLogsRepository.GetLastActionLogsForUserAsync(userId, disableAutoAvailable, time);
		}

		public async Task<ActionLog> GetLastActionLogForUserNoLimitAsync(string userId)
		{
			return await _actionLogsRepository.GetLastActionLogForUserAsync(userId);
		}

		public async Task<ActionLog> GetPreviousActionLogAsync(string userId, int actionLogId)
		{
			var actionLog = await _actionLogsRepository.GetPreviousActionLogAsync(userId, actionLogId);

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

		public async Task<ActionLog> SaveActionLogAsync(ActionLog actionLog, CancellationToken cancellationToken = default(CancellationToken))
		{
			actionLog.Timestamp = actionLog.Timestamp.ToUniversalTime();
			var saved = await _actionLogsRepository.SaveOrUpdateAsync(actionLog, cancellationToken, true);

			//InvalidateActionLogs(actionLog.DepartmentId);
			
			_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() { Status = actionLog });

			return actionLog;
		}

		public async Task<bool> SaveAllActionLogsAsync(List<ActionLog> actionLogs, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (actionLogs != null && actionLogs.Count() > 0)
			{
				InvalidateActionLogs(actionLogs[0].DepartmentId);

				foreach (var actionLog in actionLogs)
				{
					var saved = await _actionLogsRepository.SaveOrUpdateAsync(actionLog, cancellationToken);
					_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() {Status = saved});
				}

				InvalidateActionLogs(actionLogs[0].DepartmentId);

				return true;
			}

			return false;
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, CancellationToken cancellationToken = default(CancellationToken))
		{
			var al = new ActionLog();
			al.ActionTypeId = (int)actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, CancellationToken cancellationToken = default(CancellationToken))
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, string note, CancellationToken cancellationToken = default(CancellationToken))
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.Note = note;

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, int destinationId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, int destinationId, string note, CancellationToken cancellationToken = default(CancellationToken))
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;
			al.Note = note;

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, int destinationId, int destinationType, CancellationToken cancellationToken = default(CancellationToken))
		{
			var al = new ActionLog();
			al.ActionTypeId = actionType;
			al.DepartmentId = departmentId;
			al.UserId = userId;
			al.Timestamp = DateTime.UtcNow;
			al.GeoLocationData = location;
			al.DestinationId = destinationId;
			al.DestinationType = destinationType;

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, int destinationId, int destinationType, string note, CancellationToken cancellationToken = default(CancellationToken))
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

			return await SaveActionLogAsync(al, cancellationToken);
		}

		public async Task<bool> SetActionForEntireDepartmentAsync(int departmentId, int actionType, string note)
		{
			var members = await _departmentMembersRepository.GetAllByDepartmentIdAsync(departmentId);

			var logs = new List<ActionLog>();
			foreach (var member in members)
			{
				var al = new ActionLog();
				al.ActionTypeId = actionType;
				al.DepartmentId = departmentId;
				al.UserId = member.UserId;
				al.Timestamp = DateTime.UtcNow;
				al.Note = note;

				logs.Add(al);
			}

			return await SaveAllActionLogsAsync(logs);
		}

		public async Task<bool> SetActionForDepartmentGroupAsync(int departmentGroupId, int actionType, string note)
		{
			var group = await _departmentGroupsService.GetGroupByIdAsync(departmentGroupId);

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
					al.Note = note;

					logs.Add(al);
				}

				return await SaveAllActionLogsAsync(logs);
			}

			return false;
		}

		public async Task<bool> DeleteActionLogsForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var logs = await _actionLogsRepository.GetAllActionLogsForUser(userId);

			if (logs != null && logs.Any())
			{
				foreach (var log in logs)
				{
					await _actionLogsRepository.DeleteAsync(log, cancellationToken);
				}
				
				return true;
			}

			return false;
		}

		public async Task<bool> DeleteAllActionLogsForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var logs = await _actionLogsRepository.GetAllActionLogsForDepartmentAsync(departmentId);

			if (logs != null && logs.Any())
			{
				foreach (var log in logs)
				{
					await _actionLogsRepository.DeleteAsync(log, cancellationToken);
				}
				
				return true;
			}

			return false;
		}

		public async Task<ActionLog> GetActionLogByIdAsync(int actionLogId)
		{
			return await _actionLogsRepository.GetByIdAsync(actionLogId);
		}

		public async Task<List<ActionLog>> GetActionLogsForCallAsync(int departmentId, int callId)
		{
			List<int> callEnabledStates = new List<int>();
			var states = await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId);

			callEnabledStates.Add((int)ActionTypes.OnScene);
			callEnabledStates.Add((int)ActionTypes.RespondingToScene);

			var nonNullStates = from state in states
								where state.Details != null
								select state;

			callEnabledStates.AddRange(from state in nonNullStates from detail in state.Details where detail.DetailType == (int)CustomStateDetailTypes.Calls || detail.DetailType == (int)CustomStateDetailTypes.CallsAndStations select detail.CustomStateDetailId);

			var items = await _actionLogsRepository.GetActionLogsForCallAndTypesAsync(callId, callEnabledStates);
			return items.ToList();
		}
	}
}
