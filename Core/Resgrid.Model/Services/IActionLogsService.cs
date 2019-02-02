using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IActionLogsService
	{
		List<ActionLog> GetActionLogsForDepartment(int departmentId, bool forceDisableAutoAvailable = false, bool bypassCache = false);
		List<ActionLog> GetAllActionLogsForUser(string userId);
		ActionLog GetLastActionLogForUser(string userId, int? departmentId = null);
		ActionLog SaveActionLog(ActionLog actionLog);
		ActionLog SetUserAction(string userId, int departmentId, int actionType);
		List<ActionLog> GetAllActionLogsForDepartment(int departmentId);
		void DeleteActionLogsForUser(string userId);
		void SetActionForEntireDepartment(int departmentId, int actionType);
		void DeleteAllActionLogsForDepartment(int departmentId);
		void SetActionForDepartmentGroup(int departmentGroupId, int actionType);
		Dictionary<string, int> GetNewActionsCountForLast5Days();
		ActionLog SetUserAction(string userId, int departmentId, int actionType, string location);
		ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId);
		ActionLog GetLastActionLogForUserNoLimit(string userId);
		List<int> GetActionsCountForLast7DaysForDepartment(int departmentId);
		ActionLog GetActionlogById(int actionLogId);
		ActionLog GetPreviousActionLog(string userId, int actionLogId);
		ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId,
			int destinationType);
		ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, string note);
		ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId,
			int destinationType, string note);
		List<ActionLog> GetAllActionLogsForUserInDateRange(string userId, DateTime startDate, DateTime endDate);
		void SaveAllActionLogs(List<ActionLog> actionLogs);
		ActionLog SetUserAction(string userId, int departmentId, int actionType, string location, int destinationId, string note);
		List<ActionLog> GetActionLogsForCall(int departmentId, int callId);
		void InvalidateActionLogs(int departmentId);
	}
}