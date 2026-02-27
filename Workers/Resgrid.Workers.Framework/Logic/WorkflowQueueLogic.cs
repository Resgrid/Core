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

