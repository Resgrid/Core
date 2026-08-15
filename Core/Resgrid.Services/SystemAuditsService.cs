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

			// Several fields carry caller-controlled values (route ids, X-Forwarded-For). Clamp to
			// the SystemAudits column sizes so a hostile over-length value degrades to a truncated
			// audit row instead of a SqlException that loses the audit entirely.
			auditLog.UserId = Truncate(auditLog.UserId, 128);
			auditLog.Username = Truncate(auditLog.Username, 512);
			auditLog.IpAddress = Truncate(auditLog.IpAddress, 512);
			auditLog.ServerName = Truncate(auditLog.ServerName, 512);

			return await _systemAuditsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
		}

		private static string Truncate(string value, int maxLength)
		{
			if (value == null || value.Length <= maxLength)
				return value;

			return value.Substring(0, maxLength);
		}
	}
}
