using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class SystemAuditsService : ISystemAuditsService
	{
		private readonly ISystemAuditsRepository _systemAuditsRepository;

		public SystemAuditsService(ISystemAuditsRepository systemAuditsRepository)
		{
			_systemAuditsRepository = systemAuditsRepository;
		}

		public async Task<SystemAudit> SaveSystemAuditAsync(SystemAudit auditLog, CancellationToken cancellationToken = default(CancellationToken))
		{
			auditLog.LoggedOn = DateTime.UtcNow;

			if (auditLog.Data == null)
				auditLog.Data = "";

			return await _systemAuditsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
		}
	}
}
