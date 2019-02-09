using Autofac;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.ShiftNotifier;
using Serilog.Core;
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

		public async Task ProcessAsync(ShiftNotiferCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				IUserProfileService _userProfileService = null;
				ILogService _logsService = null;
				var _shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();

				var logic = new ShiftNotifierLogic();

				var shifts = _shiftsService.GetShiftsStartingNextDay(DateTime.UtcNow);

				if (shifts != null && shifts.Any())
				{
					progress.Report(2, "ShiftNotifer::Shifts to Notify: " + shifts.Count());

					_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
					_logsService = Bootstrapper.GetKernel().Resolve<ILogService>();

					foreach (var shift in shifts)
					{
						var qi = new ShiftNotifierQueueItem();

						var processLog = _logsService.GetProcessLogForTypeTime(ProcessLogTypes.ShiftNotifier, shift.ShiftId, shift.StartDay);

						if (processLog != null)
						{
							_logsService.SetProcessLog(ProcessLogTypes.ShiftNotifier, shift.ShiftId, shift.StartDay);

							if (shift.Personnel != null && shift.Personnel.Any())
								qi.Profiles = _userProfileService.GetSelectedUserProfiles(shift.Personnel.Select(x => x.UserId).ToList());

							qi.Day = shift.GetShiftDayforDateTime(DateTime.UtcNow.AddDays(1));
							if (qi.Day != null)
							{
								if (qi.Profiles == null)
									qi.Profiles = new List<UserProfile>();

								qi.Signups = _shiftsService.GetShiftSignpsForShiftDay(qi.Day.ShiftDayId);

								if (qi.Signups != null && qi.Signups.Any())
								{
									qi.Profiles.AddRange(_userProfileService.GetSelectedUserProfiles(qi.Signups.Select(x => x.UserId).ToList()));

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
										qi.Profiles.AddRange(_userProfileService.GetSelectedUserProfiles(users));
								}
							}

							qi.Shift = shift;

							progress.Report(3, "ShiftNotifer::Processing Shift Notification: " + qi.Shift.ShiftId);

							var result = logic.Process(qi);

							if (result.Item1)
							{
								progress.Report(4, $"ShiftNotifer::Processed Shift Notification {qi.Shift.ShiftId} successfully.");
							}
							else
							{
								progress.Report(5, $"ShiftNotifer::Failed to Process shift notification {qi.Shift.ShiftId} error {result.Item2}");
							}
						}
					}
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(6, $"Finishing the {Name} Task");
		}
	}
}
