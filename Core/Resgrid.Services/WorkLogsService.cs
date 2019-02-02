using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class WorkLogsService : IWorkLogsService
	{
		private readonly IGenericDataRepository<Log> _logsRepository;
		private readonly IGenericDataRepository<LogUser> _logUsersRepository;
		private readonly IGenericDataRepository<CallLog> _callLogsRepository;
		private readonly IGenericDataRepository<LogAttachment> _logAttachmentRepository;

		public WorkLogsService(IGenericDataRepository<Log> logsRepository, IGenericDataRepository<CallLog> callLogsRepository, IGenericDataRepository<LogUser> logUsersRepository,
			IGenericDataRepository<LogAttachment> logAttachmentRepository)
		{
			_logsRepository = logsRepository;
			_callLogsRepository = callLogsRepository;
			_logUsersRepository = logUsersRepository;
			_logAttachmentRepository = logAttachmentRepository;
		}

		public List<Log> GetAllLogsForUser(string userId)
		{
			var calls = from c in _logsRepository.GetAll()
						where c.LoggedByUserId == userId
						select c;

			return calls.ToList();
		}

		public List<Log> GetAllLogsForDepartmnt(int departmentId)
		{
			var calls = from c in _logsRepository.GetAll()
									where c.DepartmentId == departmentId
									select c;

			return calls.ToList();
		}

		public List<CallLog> GetAllCallLogsForUser(string userId)
		{
			var calls = from c in _callLogsRepository.GetAll()
						where c.LoggedByUserId == userId
						select c;

			return calls.ToList();
		}

		public Log GetWorkLogById(int logId)
		{
			return _logsRepository.GetAll().FirstOrDefault(x => x.LogId == logId);
		}

		public CallLog GetCallLogById(int callLogId)
		{
			return _callLogsRepository.GetAll().FirstOrDefault(x => x.CallLogId == callLogId);
		}

		public Log SaveLog(Log log)
		{
			log.LoggedOn = DateTime.UtcNow;

			_logsRepository.SaveOrUpdate(log);

			return GetWorkLogById(log.LogId);
		}

		public List<Log> GetLogsForCall(int callId)
		{
			var logs = from l in _logsRepository.GetAll()
								 where l.CallId == callId
								 select l;

			return logs.ToList();
		}


		public List<Log> GetAllLogsByDepartmentDateRange(int departmentId, LogTypes logType, DateTime start, DateTime end)
		{
			return (from c in _logsRepository.GetAll()
						 where c.DepartmentId == departmentId && c.LogType == (int)logType && c.StartedOn >= start && c.StartedOn <= end
						 select c).ToList();
		}


		public CallLog SaveCallLog(CallLog log)
		{
			log.LoggedOn = DateTime.UtcNow;

			_callLogsRepository.SaveOrUpdate(log);

			return GetCallLogById(log.CallLogId);
		}

		public void DeleteCallLog(int callLogId)
		{
			var log = GetCallLogById(callLogId);

			if (log != null)
			{
				_callLogsRepository.DeleteOnSubmit(log);
			}
		}

		public void DeleteLog(int logId)
		{
			var log = GetWorkLogById(logId);

			if (log != null)
			{
				_logsRepository.DeleteOnSubmit(log);
			}
		}

		public void DeleteLogsForUser(string userId, string newUserId)
		{
			var workLogs = from l in _logsRepository.GetAll().AsEnumerable()
			               where l.LoggedByUserId == userId
			               select l;

			var workLogUsers = from wlu in _logUsersRepository.GetAll().AsEnumerable()
				where wlu.UserId == userId
				select wlu;

			var logs = from l in _callLogsRepository.GetAll().AsEnumerable()
			           where l.LoggedByUserId == userId
			           select l;

			_callLogsRepository.DeleteAll(logs);
			_logUsersRepository.DeleteAll(workLogUsers);

			foreach (var wl in workLogs)
			{
				wl.LoggedByUserId = newUserId;
				_logsRepository.SaveOrUpdate(wl);
			}
		}

		public void ClearInvestigationByLogsForUser(string userId)
		{
			var logs = from l in _logsRepository.GetAll().AsEnumerable()
										 where l.InvestigatedByUserId == userId
										 select l;


			foreach (var l in logs)
			{
				l.InvestigatedByUserId = null;
				_logsRepository.SaveOrUpdate(l);
			}
		}

		public List<CallLog> GetCallLogsForCall(int callId)
		{
			var logs = from l in _callLogsRepository.GetAll()
			           where l.CallId == callId
			           select l;

			return logs.ToList();
		}

		public List<int> GetLogsCountForLast7DaysForDepartment(int departmentId)
		{
			List<int> actions = new List<int>();
			var startDate = DateTime.UtcNow.AddDays(-7);

			var worklogsForLast7Days =
				_logsRepository.GetAll()
					.Where(
						x =>
							x.DepartmentId == departmentId &&
							x.LoggedOn >= startDate).ToList();

			var calllogsForLast7Days =
				_callLogsRepository.GetAll()
					.Where(
						x =>
							x.DepartmentId == departmentId &&
							x.LoggedOn >= startDate).ToList();

			for (int i = 0; i < 7; i++)
			{
				int workLogs =
					worklogsForLast7Days
						.Count(
							x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-i).ToShortDateString());


				int callLogs =
					calllogsForLast7Days
						.Count(
							x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-i).ToShortDateString());

				actions.Add(workLogs + callLogs);
			}

			return actions;
		}

		public LogAttachment SaveLogAttachment(LogAttachment attachment)
		{
			_logAttachmentRepository.SaveOrUpdate(attachment);

			return attachment;
		}

		public LogAttachment GetAttachmentById(int attachmentId)
		{
			return _logAttachmentRepository.GetAll().FirstOrDefault(x => x.LogAttachmentId == attachmentId);
		}

		public List<LogAttachment> GetAttachmentsForLog(int logId)
		{
			var attachments = new List<LogAttachment>();
			attachments.AddRange(_logAttachmentRepository.GetAll().Where(x => x.LogId == logId));

			return attachments;
		}

		public void ClearGroupForLogs(int departmentGroupId)
		{
			var logs = _logsRepository.GetAll().Where(x => x.StationGroupId == departmentGroupId).ToList();

			if (logs != null && logs.Any())
			{
				foreach (var log in logs)
				{
					log.StationGroupId = null;
					log.StationGroup = null;
				}

				_logsRepository.SaveOrUpdateAll(logs);
			}
		}
	}
}