using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Configuration write, safe audit and revision are one transaction; failures never produce a false successful audit.</summary>
	public sealed class ConfigurationChangeJournal(IUnitOfWork unit, IAdminAssistRepository repository,
		IAuditLogsRepository audits, IProtectedGrantContext principal, TimeProvider clock) : IConfigurationChangeJournal
	{
		public async Task<T> ExecuteAsync<T>(int departmentId, string binding, Func<Task<ConfigurationChangeStamp>> read,
			Func<Task<T>> write, CancellationToken ct)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(binding) || binding.Length > 192) throw new ArgumentException("Invalid configuration scope.");
			var owns = unit.Transaction == null;
			await unit.CreateOrGetConnectionAsync(ct);
			try
			{
				await repository.LockConfigurationAsync(departmentId, ct);
				var before = await read();
				var result = await write();
				var after = await read();
				if (before?.Fingerprint != after?.Fingerprint)
				{
					var correlation = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
					var actor = principal.IsWorkloadCaller ? null : principal.UserId;
					var revision = await repository.AppendConfigurationChangeAsync(departmentId, actor, binding, before?.Values, after?.Values, correlation, ct);
					await audits.SaveOrUpdateAsync(new AuditLog
					{
						DepartmentId = departmentId, ObjectDepartmentId = departmentId, UserId = actor,
						LogType = (int)AuditLogTypes.DepartmentConfigurationChanged, LoggedOn = clock.GetUtcNow().UtcDateTime,
						Successful = true, ObjectId = binding, Message = "ConfigurationChanged",
						Data = JsonSerializer.Serialize(new { binding, revision, correlation, before = before?.Values, after = after?.Values,
							secretChange = before?.Values == after?.Values, source = principal.IsWorkloadCaller ? "workload" : "attended" })
					}, ct);
				}
				if (owns) unit.CommitChanges();
				return result;
			}
			catch { if (owns) unit.DiscardChanges(); throw; }
		}
	}
}
