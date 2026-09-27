using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.CalendarNotifier;
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Localization.Areas.User.SystemMessages;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Workers.Framework.Logic
{
	public class CalendarNotifierLogic
	{
		public async Task<Tuple<bool,string>> Process(CalendarNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			// Own scope per item: root-scope services would share the process-wide root unit of work.
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			var communicationService = scope.Resolve<ICommunicationService>();
			var userProfileService = scope.Resolve<IUserProfileService>();
			var departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();
			var calendarService = scope.Resolve<ICalendarService>();
			var departmentGroupsService = scope.Resolve<IDepartmentGroupsService>();
			var departmentsService = scope.Resolve<IDepartmentsService>();
			var textResponsePromptService = scope.Resolve<ITextResponsePromptService>();

			if (item?.CalendarItem?.Attendees != null && item.CalendarItem.Attendees.Any())
			{
				try
				{
					var message = String.Empty;
					var title = String.Empty;
					var profiles = await userProfileService.GetSelectedUserProfilesAsync(item.CalendarItem.Attendees.Select(x => x.UserId).ToList());
					var departmentNumber = await departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.CalendarItem.DepartmentId);
					var department = await departmentsService.GetDepartmentByIdAsync(item.CalendarItem.DepartmentId, false);

					var adjustedDateTime = item.CalendarItem.Start.TimeConverter(department);

					title = string.Format("Upcoming: {0}",
						SafeCalendarText(item.CalendarItem.Title, "AdpProtectedCalendarTitle"));

					if (String.IsNullOrWhiteSpace(item.CalendarItem.Location))
						message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()}";
					else
						message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()} at {SafeCalendarText(item.CalendarItem.Location, "AdpProtectedCalendarLocation")}";

					if (item.CalendarItem.SignupType == (int)CalendarItemSignupTypes.RSVP)
						message += " Reply YES or NO.";

					if (ConfigHelper.CanTransmit(department.DepartmentId))
					{
						foreach (var person in item.CalendarItem.Attendees)
						{
							var profile = profiles.FirstOrDefault(x => x.UserId == person.UserId);
							await SendCalendarReminderAsync(communicationService, textResponsePromptService, item.CalendarItem, person.UserId, message, departmentNumber, department, title, profile);
						}
					}
				}
				catch (Exception ex)
				{
					success = false;
					result = ex.ToString();

					Logging.LogException(ex);
				}

				await calendarService.MarkAsNotifiedAsync(item.CalendarItem.CalendarItemId);
			}
			else if (!String.IsNullOrWhiteSpace(item?.CalendarItem?.Entities))
			{
				var items = item.CalendarItem.Entities.Split(char.Parse(","));

				var message = String.Empty;
				var title = String.Empty;
				var profiles = await userProfileService.GetAllProfilesForDepartmentAsync(item.CalendarItem.DepartmentId);
				var departmentNumber = await departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.CalendarItem.DepartmentId);
				var department = await departmentsService.GetDepartmentByIdAsync(item.CalendarItem.DepartmentId, false);

				var adjustedDateTime = item.CalendarItem.Start.TimeConverter(department);
				title = $"Upcoming: {SafeCalendarText(item.CalendarItem.Title, "AdpProtectedCalendarTitle")}";

				if (String.IsNullOrWhiteSpace(item.CalendarItem.Location))
					message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()}";
				else
					message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()} at {SafeCalendarText(item.CalendarItem.Location, "AdpProtectedCalendarLocation")}";

				if (item.CalendarItem.SignupType == (int)CalendarItemSignupTypes.RSVP)
					message += " Reply YES or NO.";

				if (ConfigHelper.CanTransmit(department.DepartmentId))
				{
					if (items.Any(x => x.StartsWith("D:")))
					{
						// Notify the entire department
						foreach (var profile in profiles)
						{
							await SendCalendarReminderAsync(communicationService, textResponsePromptService, item.CalendarItem, profile.Key, message, departmentNumber, department, title, profile.Value);
						}
					}
					else
					{
						var groups = await departmentGroupsService.GetAllGroupsForDepartmentAsync(item.CalendarItem.DepartmentId);
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
											await SendCalendarReminderAsync(communicationService, textResponsePromptService, item.CalendarItem, member.UserId, message, departmentNumber, department, title, profiles[member.UserId]);
										else
											await SendCalendarReminderAsync(communicationService, textResponsePromptService, item.CalendarItem, member.UserId, message, departmentNumber, department, title, null);
									}
								}
							}
						}
					}
				}

				await calendarService.MarkAsNotifiedAsync(item.CalendarItem.CalendarItemId);
			}

			return new Tuple<bool, string>(success, result);
		}

		private static async Task SendCalendarReminderAsync(ICommunicationService communicationService, ITextResponsePromptService textResponsePromptService,
			CalendarItem calendarItem, string userId, string message, string departmentNumber, Department department, string title, UserProfile profile)
		{
			var sent = await communicationService.SendNotificationAsync(userId, calendarItem.DepartmentId, message,
				departmentNumber, department, title, profile);

			if (sent && calendarItem.SignupType == (int)CalendarItemSignupTypes.RSVP)
			{
				try
				{
					await textResponsePromptService.RecordCalendarRsvpPromptAsync(calendarItem, userId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}
		/// <summary>
		/// A reminder is built by a worker, which holds no grant and cannot decrypt. For a protected
		/// department the title and location are envelopes (catalog v9), and dropping ciphertext into
		/// a notification would both leak the shape of the data and get re-encrypted as-is by the
		/// message write net. So an enveloped value becomes a generic phrase and the member opens
		/// the entry signed in - the same rule dispatches follow.
		/// </summary>
		private static string SafeCalendarText(string value, string resourceKey)
			=> ProtectedDataEnvelope.HasEnvelopePrefix(value) || value == ProtectedDataEnvelope.RedactionValue
				? SystemMessagesResources.Get(resourceKey, null)
				: value;

	}
}
