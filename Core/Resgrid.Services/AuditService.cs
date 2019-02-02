using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AuditService : IAuditService
	{
		private readonly IGenericDataRepository<AuditLog> _auditLogsRepository;
		private readonly IUserProfileService _userProfileService;

		public AuditService(IGenericDataRepository<AuditLog> auditLogsRepository, IUserProfileService userProfileService)
		{
			_auditLogsRepository = auditLogsRepository;
			_userProfileService = userProfileService;
		}

		public AuditLog SaveAuditLog(AuditLog auditLog)
		{
			auditLog.LoggedOn = DateTime.UtcNow;
			_auditLogsRepository.SaveOrUpdate(auditLog);

			return auditLog;
		}

		public AuditLog GetAuditLogById(int auditLogId)
		{
			return _auditLogsRepository.GetAll().FirstOrDefault(x => x.AuditLogId == auditLogId);
		}

		public List<AuditLog> GetAllAuditLogsForDepartment(int departmentId)
		{
			return _auditLogsRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public void DeleteAuditLogById(int auditLogId)
		{
			var auditLog = GetAuditLogById(auditLogId);

			if (auditLog != null)
				_auditLogsRepository.DeleteOnSubmit(auditLog);
		}

		public void DeleteSelectedAuditLogs(int departmentId, List<int> auditLogIds)
		{
			var auditLogs = from al in _auditLogsRepository.GetAll()
				where al.DepartmentId == departmentId &&
							auditLogIds.Contains(al.AuditLogId)
				select al;

			_auditLogsRepository.DeleteAll(auditLogs);
		}

		public string GetAuditLogTypeString(AuditLogTypes logType)
		{
			switch (logType)
			{
				case AuditLogTypes.DepartmentSettingsChanged:
					return "Department Settings Changed";
				case AuditLogTypes.UserAdded:
					return "User Added";
				case AuditLogTypes.UserRemoved:
					return "User Removed";
				case AuditLogTypes.GroupAdded:
					return "Group Added";
				case AuditLogTypes.GroupRemoved:
					return "Group Removed";
				case AuditLogTypes.GroupChanged:
					return "Group Changed";
				case AuditLogTypes.UnitAdded:
					return "Unit Added";
				case AuditLogTypes.UnitRemoved:
					return "Unit Removed";
				case AuditLogTypes.UnitChanged:
					return "Unit Changed";
				case AuditLogTypes.ProfileUpdated:
					return "Profile Updated";
				case AuditLogTypes.PermissionsChanged:
					return "Permissions Changed";
			}

			return "";
		}
	}
}