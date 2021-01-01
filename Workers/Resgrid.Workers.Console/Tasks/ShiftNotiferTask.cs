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

				var shifts = await _shiftsService.GetShiftsStartingNextDayAsync(DateTime.UtcNow);

				if (shifts != null && shifts.Any())
				{
					_logger.LogInformation("ShiftNotifer::Shifts to Notify: " + shifts.Count());

					_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
					_logsService = Bootstrapper.GetKernel().Resolve<ILogService>();

					foreach (var shift in shifts)
					{
						var qi = new ShiftNotifierQueueItem();

						var processLog = await _logsService.GetProcessLogForTypeTimeAsync(ProcessLogTypes.ShiftNotifier, shift.ShiftId, shift.StartDay);

						if (processLog != null)
						{
							await _logsService.SetProcessLogAsync(ProcessLogTypes.ShiftNotifier, shift.ShiftId, shift.StartDay);

							if (shift.Personnel != null && shift.Personnel.Any())
								qi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(shift.Personnel.Select(x => x.UserId).ToList());

							qi.Day = shift.GetShiftDayforDateTime(DateTime.UtcNow.AddDays(1));
							if (qi.Day != null)
							{
								if (qi.Profiles == null)
									qi.Profiles = new List<UserProfile>();

								qi.Signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(qi.Day.ShiftDayId);

								if (qi.Signups != null && qi.Signups.Any())
								{
									qi.Profiles.AddRange(await _userProfileService.GetSelectedUserProfilesAsync(qi.Signups.Select(x => x.UserId).ToList()));

									var users = new List<string>();
									foreach (var signup in qi.Signups)
									{
										if (signup.Trade != null)
										{
											if (!String.IsNullOrWhiteSpace(signup.Trade.UserId))
												users.Add(signup.Trade.UserId);
											else if (signup.Trade.TargetShiftSignup != null)
												users.Add(signup.Trade.TargetShiftSignup.UserId);
										}
									}

									if (users.Any())
										qi.Profiles.AddRange(await _userProfileService.GetSelectedUserProfilesAsync(users));
								}
							}

							qi.Shift = shift;

							_logger.LogInformation("ShiftNotifer::Processing Shift Notification: " + qi.Shift.ShiftId);

							var result = await logic.Process(qi);

							if (result.Item1)
							{
								_logger.LogInformation($"ShiftNotifer::Processed Shift Notification {qi.Shift.ShiftId} successfully.");
							}
							else
							{
								_logger.LogInformation($"ShiftNotifer::Failed to Process shift notification {qi.Shift.ShiftId} error {result.Item2}");
							}
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
