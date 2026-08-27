using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	public class WorkflowQueueLogic
	{
		public static async Task<bool> ProcessWorkflowQueueItem(WorkflowQueueItem item, CancellationToken cancellationToken = default)
		{
			if (item == null) return false;

			try
			{
				// ADP department operation lock: workflow executions can mutate department data, so a
				// locked department's items are requeued unchanged (same attempt number — deferral is
				// not a retry) rather than executed or dead-lettered (plan section 20.2). The short
				// pause keeps the small pre-lock backlog from hot-cycling the bus for the whole
				// window; new triggers barely arrive while the lock blocks mutations upstream.
				if (await DepartmentLockGuard.IsDepartmentLockedAsync(item.DepartmentId))
				{
					await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
					var deferralQueue = Bootstrapper.GetKernel().Resolve<Resgrid.Model.Providers.IOutboundQueueProvider>();
					await deferralQueue.EnqueueWorkflow(item);
					return true;
				}

				var workflowService = Bootstrapper.GetKernel().Resolve<IWorkflowService>();
				var departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();

				// Get the department code needed for credential decryption
				var department = await departmentsService.GetDepartmentByIdAsync(item.DepartmentId, false);
				var departmentCode = department?.Code ?? string.Empty;

				var run = await workflowService.ExecuteWorkflowAsync(
					workflowId: item.WorkflowId,
					eventPayloadJson: item.EventPayloadJson,
					departmentId: item.DepartmentId,
					departmentCode: departmentCode,
					attemptNumber: item.AttemptNumber,
					existingRunId: item.WorkflowRunId,
					cancellationToken: cancellationToken);

				if (run == null) return false;

				// If status is Retrying, re-enqueue with incremented attempt and backoff delay
				if (run.Status == (int)WorkflowRunStatus.Retrying)
				{
					var maxRetry = WorkflowConfig.DefaultMaxRetryCount;
					var backoffBase = WorkflowConfig.RetryBackoffBaseSeconds;

					if (item.AttemptNumber < maxRetry)
					{
						var delaySeconds = (int)Math.Pow(2, item.AttemptNumber - 1) * backoffBase;
						await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

						var outboundQueue = Bootstrapper.GetKernel().Resolve<Resgrid.Model.Providers.IOutboundQueueProvider>();
						await outboundQueue.EnqueueWorkflow(new WorkflowQueueItem
						{
							WorkflowId = item.WorkflowId,
							WorkflowRunId = item.WorkflowRunId,
							DepartmentId = item.DepartmentId,
							DepartmentCode = departmentCode,
							TriggerEventType = item.TriggerEventType,
							EventPayloadJson = item.EventPayloadJson,
							AttemptNumber = item.AttemptNumber + 1,
							EnqueuedOn = DateTime.UtcNow
						});
					}
				}

				return run.Status == (int)WorkflowRunStatus.Completed;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}
	}
}

