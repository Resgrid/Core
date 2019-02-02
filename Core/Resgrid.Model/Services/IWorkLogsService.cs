using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IWorkLogsService
	{
		List<Log> GetAllLogsForUser(string userId);
		List<CallLog> GetAllCallLogsForUser(string userId);
		Log SaveLog(Log log);
		CallLog SaveCallLog(CallLog log);
		Log GetWorkLogById(int logId);
		CallLog GetCallLogById(int callLogId);
		void DeleteCallLog(int callLogId);
		void DeleteLog(int logId);
		void DeleteLogsForUser(string userId, string newUserId);
		List<CallLog> GetCallLogsForCall(int callId);
		List<int> GetLogsCountForLast7DaysForDepartment(int departmentId);
		List<Log> GetLogsForCall(int callId);
		List<Log> GetAllLogsForDepartmnt(int departmentId);
		List<Log> GetAllLogsByDepartmentDateRange(int departmentId, LogTypes logType, DateTime start, DateTime end);
		void ClearInvestigationByLogsForUser(string userId);
		LogAttachment SaveLogAttachment(LogAttachment attachment);
		LogAttachment GetAttachmentById(int attachmentId);
		List<LogAttachment> GetAttachmentsForLog(int logId);
		void ClearGroupForLogs(int departmentGroupId);
	}
}