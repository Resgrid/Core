using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IAuditService
	{
		AuditLog SaveAuditLog(AuditLog auditLog);
		List<AuditLog> GetAllAuditLogsForDepartment(int departmentId);
		string GetAuditLogTypeString(AuditLogTypes logType);
		AuditLog GetAuditLogById(int auditLogId);
		void DeleteAuditLogById(int auditLogId);
		void DeleteSelectedAuditLogs(int departmentId, List<int> auditLogIds);
	}
}