using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.ShiftNotifier;
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;

namespace Resgrid.Workers.Framework.Logic
{
	public class ShiftNotifierLogic
	{
		public async Task<Tuple<bool, string>> Process(ShiftNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.Shift != null)
			{
				// Own scope per item: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var shiftsService = scope.Resolve<IShiftsService>();
				var communicationService = scope.Resolve<ICommunicationService>();
				var departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();
				var departmentsService = scope.Resolve<IDepartmentsService>();

				var text = shiftsService.GenerateShiftNotificationText(item.Shift);
				string departmentNumber = await departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.Shift.DepartmentId);
				var department = await departmentsService.GetDepartmentByIdAsync(item.Shift.DepartmentId, false);

				if (ConfigHelper.CanTransmit(item.Shift.DepartmentId) && item.UserIds != null)
				{
					// The reminder window is the next 24 hours, so name the day rather than saying "tomorrow".
					if (item.Day != null)
						text = $"Shift ({item.Shift.Name}) starts {item.Day.Start.ToShortDateString()} at {item.Day.Start.ToShortTimeString()}";

					foreach (var userId in item.UserIds.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
					{
						UserProfile profile = item.Profiles?.FirstOrDefault(x => String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
						await communicationService.SendNotificationAsync(userId, item.Shift.DepartmentId, text, departmentNumber, department,
							item.Shift.Name, profile);
					}
				}
				else if (ConfigHelper.CanTransmit(item.Shift.DepartmentId))
				{
					if (item.Shift.Personnel != null)
					{
						foreach (var person in item.Shift.Personnel)
						{
							UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == person.UserId);
							await communicationService.SendNotificationAsync(person.UserId, item.Shift.DepartmentId, text, departmentNumber, department,
								item.Shift.Name, profile);
						}
					}

					if (item.Signups != null)
					{
						foreach (var signup in item.Signups)
						{
							if (signup.Trade != null && signup.Trade.IsTradeComplete())
							{
								if (!String.IsNullOrWhiteSpace(signup.Trade.UserId))
								{
									UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.Trade.UserId);
									await communicationService.SendNotificationAsync(signup.Trade.UserId, item.Shift.DepartmentId, text, departmentNumber, department,
										item.Shift.Name, profile);
								}
								else if (signup.GetTradeType() == ShiftTradeTypes.Source)
								{
									UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.Trade.TargetShiftSignup.UserId);
									await communicationService.SendNotificationAsync(signup.Trade.TargetShiftSignup.UserId, item.Shift.DepartmentId, text, departmentNumber, department,
										item.Shift.Name, profile);
								}
								else if (signup.GetTradeType() == ShiftTradeTypes.Target)
								{
									UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.Trade.SourceShiftSignup.UserId);
									await communicationService.SendNotificationAsync(signup.Trade.SourceShiftSignup.UserId, item.Shift.DepartmentId, text, departmentNumber, department,
										item.Shift.Name, profile);
								}
							}
							else
							{
								UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.UserId);
								await communicationService.SendNotificationAsync(signup.UserId, item.Shift.DepartmentId, text, departmentNumber, department,
									item.Shift.Name, profile);
							}
						}
					}
				}
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
