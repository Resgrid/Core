using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Workers.Framework.Logic
{
	public sealed class AdminAssistMaintenanceLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			IReadOnlyList<int> departments;
			using (var scope = Bootstrapper.GetKernel().BeginLifetimeScope())
				departments = await scope.Resolve<IAdminAssistMaintenanceStore>().GetDueDepartmentsAsync(DateTime.UtcNow, 20, ct);
			var errors = 0;
			foreach (var departmentId in departments)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
					using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
					budget.CancelAfter(TimeSpan.FromMinutes(2));
					await scope.Resolve<IAdminAssistMaintenanceService>().RunDepartmentAsync(departmentId, budget.Token);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (Exception) { errors++; }
			}
			return Tuple.Create(errors == 0, $"Admin Assist: attempted={departments.Count}, errors={errors}.");
		}
	}
}
