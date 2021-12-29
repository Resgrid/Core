using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
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
		public ILogger _logger;

		public TrainingNotiferTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(TrainingNotiferCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				//await Task.Run(async () =>
				//{
				var _trainingService = Bootstrapper.GetKernel().Resolve<ITrainingService>();
				var logic = new TrainingNotifierLogic();

				var trainings = await _trainingService.GetTrainingsToNotifyAsync(DateTime.UtcNow);

				if (trainings != null && trainings.Any())
				{
					_logger.LogInformation("TrainingNotifer::Trainings to Notify: " + trainings.Count());

					foreach (var training in trainings)
					{
						var qi = new TrainingNotifierQueueItem();
						qi.Training = training;

						//progress.Report(3, "TrainingNotifer::Processing Training Notification: " + qi.Training.TrainingId);
						var result = await logic.Process(qi);

						if (result.Item1)
						{
							_logger.LogInformation($"TrainingNotifer::Processed Training Notification {qi.Training.TrainingId} successfully.");
						}
						else
						{
							_logger.LogInformation($"TrainingNotifer::Failed to Process Training Notification {qi.Training.TrainingId} error {result.Item2}");
						}
					}
				}
				//}, cancellationToken);

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
