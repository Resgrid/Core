using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Workers.Framework.Logic
{
	public sealed class InventoryAlertsLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var alerts = scope.Resolve<IInventoryAlertService>();
				var notifications = scope.Resolve<InventoryAlertNotifications>();
				var after = 0; var processed = 0; var handedOff = 0; var errors = 0;
				while (true)
				{
					ct.ThrowIfCancellationRequested();
					var departments = await alerts.AlertDepartmentsAsync(after);
					if (departments.Count == 0) break;
					if (departments.Count > 100 || departments.Any(id => id <= after) || departments.Distinct().Count() != departments.Count
						|| !departments.SequenceEqual(departments.OrderBy(id => id)))
						throw new InvalidOperationException("Inventory alert department paging is invalid.");
					foreach (var departmentId in departments)
					{
						ct.ThrowIfCancellationRequested(); after = departmentId;
						try
						{
							await alerts.SweepAlertsAsync(departmentId);
							ct.ThrowIfCancellationRequested();
							handedOff += await notifications.ProcessDepartmentAsync(departmentId, ct);
							processed++;
						}
						catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
						catch (Exception ex)
						{
							errors++;
							Resgrid.Framework.Logging.LogError($"Inventory alert sweep failed for department {departmentId}: {ex.GetType().FullName}.");
						}
					}
				}
				ct.ThrowIfCancellationRequested();
				return Tuple.Create(errors == 0, $"Inventory alerts: departments={processed}, handedOff={handedOff}, errors={errors}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogError($"Inventory alert worker failed: {ex.GetType().FullName}.");
				return Tuple.Create(false, "Inventory alert processing failed.");
			}
		}
	}
}
