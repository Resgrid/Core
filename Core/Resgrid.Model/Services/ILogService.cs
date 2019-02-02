using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ILogService
	{
		List<LogEntry> GetAll();
		LogEntry GetLogById(int logId);
		Dictionary<string, int> GetNewLogsCountForLast5Days();
		ProcessLog SaveProcessLog(ProcessLog log);
		ProcessLog GetProcessLogForTypeTime(ProcessLogTypes type, int id, DateTime time);
		ProcessLog SetProcessLog(ProcessLogTypes type, int id, DateTime time);
	}
}