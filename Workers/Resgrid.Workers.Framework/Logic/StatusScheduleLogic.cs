using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class StatusScheduleLogic
	{
		public async Task<Tuple<bool, string>> Process(StatusScheduleQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.ScheduledTask != null)
			{
				// Own scope per item: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var actionLogsService = scope.Resolve<IActionLogsService>();
				var scheduledTasksService = scope.Resolve<IScheduledTasksService>();

				// ADP department operation lock: status mutations are deferred, not dropped — the
				// occurrence is skipped WITHOUT a completion log so the scheduler re-picks it after
				// the lock releases (plan section 20.2).
				if (await DepartmentLockGuard.IsDepartmentLockedAsync(item.ScheduledTask.DepartmentId))
					return new Tuple<bool, string>(true, $"deferred: department {item.ScheduledTask.DepartmentId} is locked");

				try
				{
					if (item.ScheduledTask.TaskType == (int)TaskTypes.DepartmentStatusReset)
					{
						using (StatusWriteActor.Begin(null, StatusSetOrigins.Schedule))
							await actionLogsService.SetActionForEntireDepartmentAsync(item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data), $"Department Status Reset {item.ScheduledTask.ScheduledTaskId}");
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					success = false;
					result = ex.ToString();
				}

				if (success)
					await scheduledTasksService.CreateScheduleTaskLogAsync(item.ScheduledTask);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
