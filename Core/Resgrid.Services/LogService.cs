using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class LogService : ILogService
	{
		private readonly IGenericDataRepository<LogEntry> _logEntriesRepository;
		private readonly IGenericDataRepository<ProcessLog> _processLogRepository;

		public LogService(IGenericDataRepository<LogEntry> logEntriesRepository,
			IGenericDataRepository<ProcessLog> processLogRepository)
		{
			_logEntriesRepository = logEntriesRepository;
			_processLogRepository = processLogRepository;
		}

		public List<LogEntry> GetAll()
		{
			return _logEntriesRepository.GetAll().ToList();
		}

		public LogEntry GetLogById(int logId)
		{
			return _logEntriesRepository.GetAll().FirstOrDefault(x => x.id == logId);
		}

		public Dictionary<string, int> GetNewLogsCountForLast5Days()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();

			var startDate = DateTime.UtcNow.AddDays(-4);
			var filteredRecords =
				_logEntriesRepository.GetAll()
					.Where(
						x => x.TimeStamp >= startDate).ToList();

			data.Add(DateTime.UtcNow.ToShortDateString(),
				filteredRecords.Count(x => x.TimeStamp.ToShortDateString() == DateTime.UtcNow.ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-1).ToShortDateString(),
				filteredRecords.Count(x => x.TimeStamp.ToShortDateString() == DateTime.UtcNow.AddDays(-1).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-2).ToShortDateString(),
				filteredRecords.Count(x => x.TimeStamp.ToShortDateString() == DateTime.UtcNow.AddDays(-2).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-3).ToShortDateString(),
				filteredRecords.Count(x => x.TimeStamp.ToShortDateString() == DateTime.UtcNow.AddDays(-3).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-4).ToShortDateString(),
				filteredRecords.Count(x => x.TimeStamp.ToShortDateString() == DateTime.UtcNow.AddDays(-4).ToShortDateString()));

			return data;
		}

		public ProcessLog SaveProcessLog(ProcessLog log)
		{
			_processLogRepository.SaveOrUpdate(log);

			return log;
		}

		public ProcessLog GetProcessLogForTypeTime(ProcessLogTypes type, int id, DateTime time)
		{
			return
				_processLogRepository.GetAll()
					.FirstOrDefault(x => x.Type == (int) type && x.SourceId == id && x.TargetRunTime == time);
		}

		public ProcessLog SetProcessLog(ProcessLogTypes type, int id, DateTime time)
		{
			var log = new ProcessLog();
			log.Type = (int) type;
			log.SourceId = id;
			log.TargetRunTime = time;
			log.Timestamp = DateTime.UtcNow;

			return SaveProcessLog(log);
		}
	}
}