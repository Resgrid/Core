using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;

namespace Resgrid.Workers.Framework.Logic
{
	public class TrainingNotifierLogic
	{
		public async Task<Tuple<bool, string>> Process(TrainingNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			try
			{
				if (item != null && item.Training != null)
				{
					// Own scope per item: root-scope services would share the process-wide root unit of work.
					using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
					var trainingService = scope.Resolve<ITrainingService>();
					var now = DateTime.UtcNow;

					// Per assignee (TrainingNoticePlanner): the first notice, due reminders on the training's lead days ("due
					// within the next N hours and not yet reminded", T-D19) and overdue notices with the creator's digest. Only
					// active members who have not completed, are not exempt and are not on leave; each send is stamped on the row.
					await trainingService.SendTrainingNoticesAsync(item.Training, now);

					// The training row keeps its "announced" stamp once it has started (a scheduled training is not announced early).
					if (!item.Training.Notified.HasValue && item.Training.HasStarted(now))
						await trainingService.MarkAsNotifiedAsync(item.Training.TrainingId);
				}
			}
			catch (Exception ex)
			{
				// One training that fails must not stop the rest of the run.
				Logging.LogException(ex, $"Training notifier failed for training {item?.Training?.TrainingId} in department {item?.Training?.DepartmentId}");
				success = false;
				result = ex.ToString();
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
