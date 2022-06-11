using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ISystemAuditsService
	{
		Task<SystemAudit> SaveSystemAuditAsync(SystemAudit auditLog, CancellationToken cancellationToken = default(CancellationToken));
	}
}
