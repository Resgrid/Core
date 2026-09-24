using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.ShiftNotifier;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class ShiftNotiferTask : IQuidjiboHandler<ShiftNotiferCommand>
	{
		public string Name => "Shift Notifier Prune";
		public int Priority => 1;
		public ILogger _logger;

		public ShiftNotiferTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ShiftNotiferCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				//await Task.Run(async () =>
				//{
				IUserProfileService _userProfileService = null;
				ILogService _logsService = null;
				var _shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();

				var logic = new ShiftNotifierLogic();

				// Every shift day starting in the next 24 hours, each reminded once (the process log is keyed on the shift
				// day, not the shift, so consecutive days each get their reminder), sent to the day's resolved roster.
				var days = await _shiftsService.GetShiftDaysStartingWithinDayAsync(DateTime.UtcNow);

				if (days != null && days.Any())
				{
					_logger.LogInformation("ShiftNotifer::Shift days to Notify: " + days.Count);

					_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
					_logsService = Bootstrapper.GetKernel().Resolve<ILogService>();

					foreach (var schedule in days)
					{
						var processLog = await _logsService.GetProcessLogForTypeTimeAsync(ProcessLogTypes.ShiftDayNotifier, schedule.Day.ShiftDayId, schedule.Day.Day);

						if (processLog != null)
							continue;

						await _logsService.SetProcessLogAsync(ProcessLogTypes.ShiftDayNotifier, schedule.Day.ShiftDayId, schedule.Day.Day);

						var qi = new ShiftNotifierQueueItem();
						qi.Shift = schedule.Shift;
						qi.Day = schedule.Day;
						qi.UserIds = schedule.Roster.Where(x => x.IsOnDuty()).Select(x => x.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
						qi.Profiles = qi.UserIds.Any()
							? await _userProfileService.GetSelectedUserProfilesAsync(qi.UserIds)
							: new List<UserProfile>();

						_logger.LogInformation("ShiftNotifer::Processing Shift Notification: " + qi.Shift.ShiftId + " day " + qi.Day.ShiftDayId);

						var result = await logic.Process(qi);

						if (result.Item1)
							_logger.LogInformation($"ShiftNotifer::Processed Shift Notification {qi.Shift.ShiftId} successfully.");
						else
							_logger.LogInformation($"ShiftNotifer::Failed to Process shift notification {qi.Shift.ShiftId} error {result.Item2}");
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
