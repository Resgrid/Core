using Autofac;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
using Serilog.Core;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class TrainingNotiferTask : IQuidjiboHandler<TrainingNotiferCommand>
	{
		public string Name => "Training Notifier";
		public int Priority => 1;


		public async Task ProcessAsync(TrainingNotiferCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				var _trainingService = Bootstrapper.GetKernel().Resolve<ITrainingService>();
				var logic = new TrainingNotifierLogic();

				var trainings = _trainingService.GetTrainingsToNotify(DateTime.UtcNow);

				if (trainings != null && trainings.Any())
				{
					progress.Report(2, "TrainingNotifer::Trainings to Notify: " + trainings.Count());

					foreach (var training in trainings)
					{
						var qi = new TrainingNotifierQueueItem();
						qi.Training = training;

						progress.Report(3, "TrainingNotifer::Processing Training Notification: " + qi.Training.TrainingId);
						var result = logic.Process(qi);

						if (result.Item1)
						{
							progress.Report(4, $"TrainingNotifer::Processed Training Notification {qi.Training.TrainingId} successfully.");
						}
						else
						{
							progress.Report(5, $"TrainingNotifer::Failed to Process Training Notification {qi.Training.TrainingId} error {result.Item2}");
						}
					}
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(6, $"Finishing the {Name} Task");
		}
	}
}
