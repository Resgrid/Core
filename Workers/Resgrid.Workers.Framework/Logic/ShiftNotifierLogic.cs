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
		private IShiftsService _shiftsService;
		private ICommunicationService _communicationService;
		private IDepartmentSettingsService _departmentSettingsService;

		public ShiftNotifierLogic()
		{
			_shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();
			_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
			_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
		}

		public async Task<Tuple<bool, string>> Process(ShiftNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.Shift != null)
			{
				var text = _shiftsService.GenerateShiftNotificationText(item.Shift);
				string departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.Shift.DepartmentId);

				if (ConfigHelper.CanTransmit(item.Shift.DepartmentId))
				{
					if (item.Shift.Personnel != null)
					{
						foreach (var person in item.Shift.Personnel)
						{
							UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == person.UserId);
							await _communicationService.SendNotificationAsync(person.UserId, item.Shift.DepartmentId, text, departmentNumber,
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
									await _communicationService.SendNotificationAsync(signup.Trade.UserId, item.Shift.DepartmentId, text, departmentNumber,
										item.Shift.Name, profile);
								}
								else if (signup.GetTradeType() == ShiftTradeTypes.Source)
								{
									UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.Trade.TargetShiftSignup.UserId);
									await _communicationService.SendNotificationAsync(signup.Trade.TargetShiftSignup.UserId, item.Shift.DepartmentId, text, departmentNumber,
										item.Shift.Name, profile);
								}
								else if (signup.GetTradeType() == ShiftTradeTypes.Target)
								{
									UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.Trade.SourceShiftSignup.UserId);
									await _communicationService.SendNotificationAsync(signup.Trade.SourceShiftSignup.UserId, item.Shift.DepartmentId, text, departmentNumber,
										item.Shift.Name, profile);
								}
							}
							else
							{
								UserProfile profile = item.Profiles.FirstOrDefault(x => x.UserId == signup.UserId);
								await _communicationService.SendNotificationAsync(signup.UserId, item.Shift.DepartmentId, text, departmentNumber,
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
