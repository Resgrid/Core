using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class LogService : ILogService
	{
		private readonly ILogEntriesRepository _logEntriesRepository;
		private readonly IProcessLogRepository _processLogRepository;

		public LogService(ILogEntriesRepository logEntriesRepository, IProcessLogRepository processLogRepository)
		{
			_logEntriesRepository = logEntriesRepository;
			_processLogRepository = processLogRepository;
		}

		public async Task<List<LogEntry>> GetAllAsync()
		{
			var items = await _logEntriesRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<LogEntry>();
		}

		public async Task<LogEntry> GetLogByIdAsync(int logId)
		{
			return await _logEntriesRepository.GetByIdAsync(logId);
		}

		public async Task<Dictionary<string, int>> GetNewLogsCountForLast5DaysAsync()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();

			var startDate = DateTime.UtcNow.AddDays(-4);
			var filteredRecords = from l in await GetAllAsync()
													where l.TimeStamp >= startDate
													select l;

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

		public async Task<ProcessLog> SaveProcessLogAsync(ProcessLog log, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _processLogRepository.SaveOrUpdateAsync(log, cancellationToken);
		}

		public async Task<ProcessLog> GetProcessLogForTypeTimeAsync(ProcessLogTypes type, int id, DateTime time)
		{
			var items = from l in await _processLogRepository.GetAllAsync()
				where l.Type == (int) type && l.SourceId == id && l.TargetRunTime == time
						select l;

			return items.FirstOrDefault();
		}

		public async Task<ProcessLog> SetProcessLogAsync(ProcessLogTypes type, int id, DateTime time, CancellationToken cancellationToken = default(CancellationToken))
		{
			var log = new ProcessLog();
			log.Type = (int) type;
			log.SourceId = id;
			log.TargetRunTime = time;
			log.Timestamp = DateTime.UtcNow;

			return await SaveProcessLogAsync(log, cancellationToken);
		}
	}
}
