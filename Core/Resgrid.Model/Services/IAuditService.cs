using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IAuditService
	{
		Task<AuditLog> SaveAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<AuditLog>> GetAllAuditLogsForDepartmentAsync(int departmentId);
		string GetAuditLogTypeString(AuditLogTypes logType);
		Task<AuditLog> GetAuditLogByIdAsync(int auditLogId);
	}
}
