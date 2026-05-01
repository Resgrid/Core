using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class CalendarActionHandler : IChatbotActionHandler
	{
		private readonly ICalendarService _calendarService;
		private readonly IDepartmentsService _departmentsService;

		public CalendarActionHandler(ICalendarService calendarService, IDepartmentsService departmentsService)
		{
			_calendarService = calendarService;
			_departmentsService = departmentsService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListCalendar;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var upcomingItems = await _calendarService.GetUpcomingCalendarItemsAsync(session.DepartmentId, DateTime.UtcNow);

				if (upcomingItems == null || !upcomingItems.Any())
				{
					return new ChatbotResponse
					{
						Text = $"No upcoming calendar events for {department?.Name}.",
						Processed = true
					};
				}

				var sb = new StringBuilder();
				sb.AppendLine($"Upcoming Events for {department?.Name}:");
				sb.AppendLine("----------------------");

				var items = upcomingItems.Take(10).ToList();
				foreach (var item in items)
				{
					var startStr = item.Start.TimeConverter(department).ToString("g");
					sb.AppendLine($"{startStr}: {item.Title?.Truncate(50)}");
					if (!string.IsNullOrWhiteSpace(item.Location))
						sb.AppendLine($"  Location: {item.Location.Truncate(40)}");
				}

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error retrieving calendar events.", Processed = false };
			}
		}
	}
}
