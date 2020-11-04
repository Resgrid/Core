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
	public class AuditService : IAuditService
	{
		private readonly IAuditLogsRepository _auditLogsRepository;
		private readonly IUserProfileService _userProfileService;

		public AuditService(IAuditLogsRepository auditLogsRepository, IUserProfileService userProfileService)
		{
			_auditLogsRepository = auditLogsRepository;
			_userProfileService = userProfileService;
		}

		public async Task<AuditLog> SaveAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken = default(CancellationToken))
		{
			auditLog.LoggedOn = DateTime.UtcNow;
			return await _auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
		}

		public async Task<AuditLog> GetAuditLogByIdAsync(int auditLogId)
		{
			return await _auditLogsRepository.GetByIdAsync(auditLogId);
		}

		public async Task<List<AuditLog>> GetAllAuditLogsForDepartmentAsync(int departmentId)
		{
			var logs = await _auditLogsRepository.GetAllByDepartmentIdAsync(departmentId);
			return logs.ToList();
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
