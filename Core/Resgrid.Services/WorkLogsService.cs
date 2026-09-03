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
	public class WorkLogsService : IWorkLogsService
	{
		private readonly ILogsRepository _logsRepository;
		private readonly ILogUsersRepository _logUsersRepository;
		private readonly ICallLogsRepository _callLogsRepository;
		private readonly ILogAttachmentRepository _logAttachmentRepository;
		private readonly ILogUnitsRepository _logUnitsRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallsService _callsService;

		// Lazy: defers the protected-write graph (broker client) until a log save actually needs it.
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;

		// Lazy: the Records cutover guard (RMS plan section 4.1) is consulted only on a legacy write.
		private readonly Lazy<IRecordsCutoverService> _recordsCutoverService;

		public WorkLogsService(ILogsRepository logsRepository, ICallLogsRepository callLogsRepository, ILogUsersRepository logUsersRepository,
			ILogAttachmentRepository logAttachmentRepository, ILogUnitsRepository logUnitsRepository, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, ICallsService callsService,
			Lazy<IProtectedWriteService> protectedWriteService, Lazy<IRecordsCutoverService> recordsCutoverService)
		{
			_recordsCutoverService = recordsCutoverService;
			_logsRepository = logsRepository;
			_callLogsRepository = callLogsRepository;
			_logUsersRepository = logUsersRepository;
			_logAttachmentRepository = logAttachmentRepository;
			_logUnitsRepository = logUnitsRepository;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_protectedWriteService = protectedWriteService;
			_callsService = callsService;
		}

		public async Task<List<string>> GetLogYearsByDeptartmentAsync(int departmentId)
		{
			var items = await _logsRepository.SelectLogYearsByDeptAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<string>();
		}

		public async Task<List<Log>> GetAllLogsForUserAsync(string userId)
		{
			var calls = await _logsRepository.GetLogsForUserAsync(userId);
			return calls.ToList();
		}

		public async Task<List<Log>> GetAllLogsForDepartmentAsync(int departmentId)
		{
			var logs = await _logsRepository.GetAllByDepartmentIdAsync(departmentId);

			if (logs != null && logs.Any())
				return logs.ToList();
			else
				return new List<Log>();
		}

		public async Task<List<Log>> GetAllLogsForDepartmentAndYearAsync(int departmentId, string year)
		{
			var logs = await _logsRepository.GetAllLogsByDepartmentIdYearAsync(departmentId, year);

			if (logs != null && logs.Any())
				return logs.ToList();
			else
				return new List<Log>();
		}

		public async Task<List<CallLog>> GetAllCallLogsForUserAsync(string userId)
		{
			var calls = await _callLogsRepository.GetLogsForUserAsync(userId);

			return calls.ToList();
		}

		public async Task<Log> GetWorkLogByIdAsync(int logId)
		{
			var log = await _logsRepository.GetByIdAsync(logId);
			log.Users = (await _logUsersRepository.GetLogsByLogIdAsync(logId)).ToList();
			log.Units = (await _logUnitsRepository.GetLogsByLogIdAsync(logId)).ToList();
			log.Department = await _departmentsService.GetDepartmentByIdAsync(log.DepartmentId);

			if (log.StationGroupId.HasValue)
				log.StationGroup = await _departmentGroupsService.GetGroupByIdAsync(log.StationGroupId.Value);

			if (log.CallId.HasValue)
				log.Call = await _callsService.GetCallByIdAsync(log.CallId.Value);

			return log;
		}

		public async Task<CallLog> GetCallLogByIdAsync(int callLogId)
		{
			return await _callLogsRepository.GetByIdAsync(callLogId);
		}

		public async Task<Log> SaveLogAsync(Log log, CancellationToken cancellationToken = default(CancellationToken))
		{
			// After Records activation no legacy Log is created or edited, whoever the caller is (RMS plan section 4.1).
			await _recordsCutoverService.Value.EnsureLegacyWriteAllowedAsync(log.DepartmentId, "WorkLogsService.SaveLogAsync", log.LoggedByUserId);

			log.LoggedOn = DateTime.UtcNow;

			var savedLog = await _logsRepository.SaveOrUpdateAsync(log, cancellationToken, true);

			foreach (var unit in savedLog.Units)
			{
				unit.LogId = savedLog.LogId;
				var savedLogUnit = _logUnitsRepository.SaveOrUpdateAsync(unit, cancellationToken, true);
			}

			foreach (var user in savedLog.Users)
			{
				user.LogId = savedLog.LogId;
				var savedLogUser = _logUsersRepository.SaveOrUpdateAsync(user, cancellationToken, true);
			}

			// ADP write safety net (plan 4.2/19.2), catalog v3: the incident log's narrative, initial
			// report, cause, contact details, location and body/deceased fields are cataloged. Runs
			// AFTER the save so the identity pk exists (it is the AAD row key), then re-persists the
			// enveloped row. Fails closed by throwing rather than leaving plaintext at rest.
			var protectedWrite = await _protectedWriteService.Value.PrepareLogWriteAsync(savedLog.DepartmentId,
				savedLog, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); log {savedLog.LogId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				savedLog = await _logsRepository.SaveOrUpdateAsync(savedLog, cancellationToken, true);

			return savedLog;
		}

		public async Task<List<Log>> GetLogsForCallAsync(int callId)
		{
			var logs = await _logsRepository.GetLogsForCallAsync(callId);

			if (logs != null && logs.Any())
			{
				var items = logs.ToList();

				for (int i = 0; i < items.Count; i++)
				{
					items[i] = await PopulateLogData(items[i], true, true);
				}

				return items;
			}

			return new List<Log>();
		}


		public async Task<List<Log>> GetAllLogsByDepartmentDateRangeAsync(int departmentId, LogTypes logType, DateTime start, DateTime end)
		{
			var items = (from c in await _logsRepository.GetAllByDepartmentIdAsync(departmentId)
						 where c.DepartmentId == departmentId && c.LogType == (int)logType && c.StartedOn >= start && c.StartedOn <= end
						 select c).ToList();

			if (items != null && items.Any())
			{
				for (int i = 0; i < items.Count; i++)
				{
					items[i] = await PopulateLogData(items[i], true, true);
				}

				return items;
			}

			return new List<Log>();
		}


		public async Task<CallLog> SaveCallLogAsync(CallLog log, CancellationToken cancellationToken = default(CancellationToken))
		{
			log.LoggedOn = DateTime.UtcNow;

			var savedLog = await _callLogsRepository.SaveOrUpdateAsync(log, cancellationToken);

			// ADP write safety net (plan 4.2/19.2): calllogs.narrative is cataloged. Runs AFTER the
			// save so the identity pk exists (it is the AAD row key), then re-persists the enveloped
			// row. Fails closed by throwing rather than leaving plaintext at rest.
			var protectedWrite = await _protectedWriteService.Value.PrepareCallLogWriteAsync(savedLog.DepartmentId,
				savedLog, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); call log {savedLog.CallLogId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				savedLog = await _callLogsRepository.SaveOrUpdateAsync(savedLog, cancellationToken);

			return savedLog;
		}

		public async Task<bool> DeleteCallLogAsync(int callLogId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var log = await GetCallLogByIdAsync(callLogId);

			if (log != null)
			{
				return await _callLogsRepository.DeleteAsync(log, cancellationToken);
			}

			return false;
		}

		public async Task<bool> DeleteLogAsync(int logId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var log = await GetWorkLogByIdAsync(logId);

			if (log != null)
			{
				await _recordsCutoverService.Value.EnsureLegacyWriteAllowedAsync(log.DepartmentId, "WorkLogsService.DeleteLogAsync");

				return await _logsRepository.DeleteAsync(log, cancellationToken);
			}

			return false;
		}

		public async Task<List<CallLog>> GetCallLogsForCallAsync(int callId)
		{
			var logs = await _callLogsRepository.GetLogsForCallAsync(callId);
			return logs.ToList();
		}

		public async Task<LogAttachment> SaveLogAttachmentAsync(LogAttachment attachment, CancellationToken cancellationToken = default(CancellationToken))
		{
			var owningLog = await _logsRepository.GetByIdAsync(attachment.LogId);
			if (owningLog != null)
				await _recordsCutoverService.Value.EnsureLegacyWriteAllowedAsync(owningLog.DepartmentId, "WorkLogsService.SaveLogAttachmentAsync", attachment.UserId);

			return await _logAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken);
		}

		public async Task<LogAttachment> GetAttachmentByIdAsync(int attachmentId)
		{
			return await _logAttachmentRepository.GetByIdAsync(attachmentId);
		}

		public async Task<List<LogAttachment>> GetAttachmentsForLogAsync(int logId)
		{
			var attachments = await _logAttachmentRepository.GetAttachmentsByLogIdAsync(logId);

			return attachments.ToList();
		}

		public async Task<bool> ClearGroupForLogsAsync(int departmentGroupId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var logs = await _logsRepository.GetLogsForGroupAsync(departmentGroupId);

			if (logs != null && logs.Any())
			{
				foreach (var log in logs)
				{
					log.StationGroupId = null;
					log.StationGroup = null;

					await _logsRepository.SaveOrUpdateAsync(log, cancellationToken);
				}

				return true;
			}

			return false;
		}

		public async Task<Log> PopulateLogData(Log log, bool getUsers, bool getUnits)
		{
			if (getUsers && log.Users == null)
			{
				var items = await _logUsersRepository.GetLogsByLogIdAsync(log.LogId);

				if (items != null)
					log.Users = items.ToList();
				else
					log.Users = new List<LogUser>();
			}

			if (getUnits && log.Units == null)
			{
				var items = await _logUnitsRepository.GetLogsByLogIdAsync(log.LogId);

				if (items != null)
					log.Units = items.ToList();
				else
					log.Units = new List<LogUnit>();
			}

			return log;
		}
	}
}
