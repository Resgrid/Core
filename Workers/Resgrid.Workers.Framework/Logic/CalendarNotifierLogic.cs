using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.CalendarNotifier;
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Helpers;

namespace Resgrid.Workers.Framework.Logic
{
	public class CalendarNotifierLogic
	{
		private ICalendarService _calendarService;
		private ICommunicationService _communicationService;
		private IUserProfileService _userProfileService;
		private IDepartmentSettingsService _departmentSettingsService;
		private IDepartmentGroupsService _departmentGroupsService;
		private IDepartmentsService _departmentsService;

		public CalendarNotifierLogic()
		{
			_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
			_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
			_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
			_calendarService = Bootstrapper.GetKernel().Resolve<ICalendarService>();
			_departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();
			_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
		}

		public async Task<Tuple<bool,string>> Process(CalendarNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item?.CalendarItem?.Attendees != null && item.CalendarItem.Attendees.Any())
			{
				try
				{
					var message = String.Empty;
					var title = String.Empty;
					var profiles = await _userProfileService.GetSelectedUserProfilesAsync(item.CalendarItem.Attendees.Select(x => x.UserId).ToList());
					var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.CalendarItem.DepartmentId);
					var department = await _departmentsService.GetDepartmentByIdAsync(item.CalendarItem.DepartmentId, false);

					var adjustedDateTime = item.CalendarItem.Start.TimeConverter(department);

					title = string.Format("Upcoming: {0}", item.CalendarItem.Title);

					if (String.IsNullOrWhiteSpace(item.CalendarItem.Location))
						message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()}";
					else
						message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()} at {item.CalendarItem.Location}";

					if (ConfigHelper.CanTransmit(department.DepartmentId))
					{
						foreach (var person in item.CalendarItem.Attendees)
						{
							var profile = profiles.FirstOrDefault(x => x.UserId == person.UserId);
							await _communicationService.SendNotificationAsync(person.UserId, item.CalendarItem.DepartmentId, message, departmentNumber, title, profile);
						}
					}
				}
				catch (Exception ex)
				{
					success = false;
					result = ex.ToString();

					Logging.LogException(ex);
				}

				await _calendarService.MarkAsNotifiedAsync(item.CalendarItem.CalendarItemId);
			}
			else if (!String.IsNullOrWhiteSpace(item?.CalendarItem?.Entities))
			{
				var items = item.CalendarItem.Entities.Split(char.Parse(","));

				var message = String.Empty;
				var title = String.Empty;
				var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(item.CalendarItem.DepartmentId);
				var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.CalendarItem.DepartmentId);
				var department = await _departmentsService.GetDepartmentByIdAsync(item.CalendarItem.DepartmentId, false);

				var adjustedDateTime = item.CalendarItem.Start.TimeConverter(department);
				title = $"Upcoming: {item.CalendarItem.Title}";

				if (String.IsNullOrWhiteSpace(item.CalendarItem.Location))
					message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()}";
				else
					message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()} at {item.CalendarItem.Location}";

				if (ConfigHelper.CanTransmit(department.DepartmentId))
				{
					if (items.Any(x => x.StartsWith("D:")))
					{
						// Notify the entire department
						foreach (var profile in profiles)
						{
							await _communicationService.SendNotificationAsync(profile.Key, item.CalendarItem.DepartmentId, message, departmentNumber, title, profile.Value);
						}
					}
					else
					{
						var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(item.CalendarItem.DepartmentId);
						foreach (var val in items)
						{
							int groupId = 0;
							if (int.TryParse(val.Replace("G:", ""), out groupId))
							{
								var group = groups.FirstOrDefault(x => x.DepartmentGroupId == groupId);

								if (group != null)
								{
									foreach (var member in group.Members)
									{
										if (profiles.ContainsKey(member.UserId))
											await _communicationService.SendNotificationAsync(member.UserId, item.CalendarItem.DepartmentId, message, departmentNumber, title, profiles[member.UserId]);
										else
											await _communicationService.SendNotificationAsync(member.UserId, item.CalendarItem.DepartmentId, message, departmentNumber, title, null);
									}
								}
							}
						}
					}
				}

				await _calendarService.MarkAsNotifiedAsync(item.CalendarItem.CalendarItemId);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
