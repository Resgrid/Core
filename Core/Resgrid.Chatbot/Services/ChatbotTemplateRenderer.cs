using System;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Chatbot.Services
{
	public class ChatbotTemplateRenderer : IChatbotTemplateRenderer
	{
		public string Render(string templateName, object model, ChatbotPlatform platform)
		{
			if (string.IsNullOrWhiteSpace(templateName))
				return string.Empty;

			try
			{
				var capabilities = ChatbotPlatformCapabilities.ForPlatform(platform);

				return templateName switch
				{
					"calls_list" => RenderCallsList(model, capabilities),
					"call_detail" => RenderCallDetail(model, capabilities),
					"units_list" => RenderUnitsList(model, capabilities),
					"status_response" => RenderStatusResponse(model, capabilities),
					"messages_list" => RenderMessagesList(model, capabilities),
					"calendar_list" => RenderCalendarList(model, capabilities),
					"shifts_list" => RenderShiftsList(model, capabilities),
					"personnel_list" => RenderPersonnelList(model, capabilities),
					"help" => RenderHelp(model, capabilities),
					"error" => RenderError(model, capabilities),
					_ => RenderDefault(templateName, model)
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return "An error occurred formatting the response.";
			}
		}

		public Task<ChatbotResponse> RenderResponseAsync(string templateName, object model, ChatbotPlatform platform, ChatbotIntent intent)
		{
			var text = Render(templateName, model, platform);
			return Task.FromResult(new ChatbotResponse
			{
				Text = text,
				Processed = true,
				Intent = intent,
				ResponseFormat = "text"
			});
		}

		private static string RenderCallsList(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is CallsListModel m)
			{
				sb.AppendLine($"Active Calls for {m.DepartmentName}:");
				sb.AppendLine("----------------------");
				if (m.Calls == null || m.Calls.Length == 0)
				{
					sb.AppendLine("No active calls.");
				}
				else
				{
					foreach (var c in m.Calls)
						sb.AppendLine($"C#{c.Number}: {c.Name} - {c.Nature} {(string.IsNullOrWhiteSpace(c.Address) ? "" : $"@ {c.Address}")}");
				}
			}
			return sb.ToString();
		}

		private static string RenderCallDetail(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is CallDetailModel m)
			{
				sb.AppendLine($"Call #{m.Number}: {m.Name}");
				sb.AppendLine($"Type: {m.Nature}");
				sb.AppendLine($"Address: {m.Address}");
				sb.AppendLine($"Priority: {m.Priority}");
				sb.AppendLine($"State: {m.State}");
			}
			return sb.ToString();
		}

		private static string RenderUnitsList(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is UnitsListModel m)
			{
				sb.AppendLine($"Unit Statuses for {m.DepartmentName}:");
				sb.AppendLine("----------------------");
				if (m.Units == null || m.Units.Length == 0)
					sb.AppendLine("No units found.");
				else
					foreach (var u in m.Units)
						sb.AppendLine($"{u.Name}: {u.Status}");
			}
			return sb.ToString();
		}

		private static string RenderStatusResponse(object model, ChatbotPlatformCapabilities caps)
		{
			if (model is StatusResponseModel m)
				return $"Status updated to: {m.StatusName}\nStaffing: {m.StaffingLevel}";
			return "Status updated.";
		}

		private static string RenderMessagesList(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is MessagesListModel m)
			{
				sb.AppendLine($"Messages ({m.UnreadCount} unread):");
				sb.AppendLine("----------------------");
				if (m.Messages == null || m.Messages.Length == 0)
					sb.AppendLine("No messages.");
				else
					foreach (var msg in m.Messages)
						sb.AppendLine($"#{msg.Id}: {msg.Subject} - From {msg.From} ({msg.Date})");
			}
			return sb.ToString();
		}

		private static string RenderCalendarList(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is CalendarListModel m)
			{
				sb.AppendLine($"Upcoming Events for {m.DepartmentName}:");
				sb.AppendLine("----------------------");
				if (m.Events == null || m.Events.Length == 0)
					sb.AppendLine("No upcoming events.");
				else
					foreach (var e in m.Events)
						sb.AppendLine($"{e.Start}: {e.Title} ({e.Type})");
			}
			return sb.ToString();
		}

		private static string RenderShiftsList(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is ShiftsListModel m)
			{
				sb.AppendLine($"Shifts for {m.DepartmentName}:");
				sb.AppendLine("----------------------");
				if (m.Shifts == null || m.Shifts.Length == 0)
					sb.AppendLine("No shifts found.");
				else
					foreach (var s in m.Shifts)
						sb.AppendLine($"{s.Start} - {s.End}: {s.Name} ({s.Station})");
			}
			return sb.ToString();
		}

		private static string RenderPersonnelList(object model, ChatbotPlatformCapabilities caps)
		{
			var sb = new StringBuilder();
			if (model is PersonnelListModel m)
			{
				sb.AppendLine($"Personnel for {m.DepartmentName}:");
				sb.AppendLine("----------------------");
				if (m.Personnel == null || m.Personnel.Length == 0)
					sb.AppendLine("No personnel found.");
				else
					foreach (var p in m.Personnel)
						sb.AppendLine($"{p.Name}: {p.Status} ({p.Staffing}){(string.IsNullOrWhiteSpace(p.Destination) ? "" : $" - {p.Destination}")}");
			}
			return sb.ToString();
		}

		private static string RenderHelp(object model, ChatbotPlatformCapabilities caps)
		{
			if (model is HelpModel m)
				return m.HelpText;
			return "Commands: HELP, STOP, STATUS, CALLS, C#####, UNITS, MSG, CALENDAR, PERSONNEL, SHIFTS";
		}

		private static string RenderError(object model, ChatbotPlatformCapabilities caps)
		{
			if (model is ErrorModel m)
				return m.Message;
			return "An error occurred.";
		}

		private static string RenderDefault(string templateName, object model)
		{
			return model?.ToString() ?? string.Empty;
		}
	}

	// Template Models
	public class CallsListModel
	{
		public string DepartmentName;
		public CallEntry[] Calls;
		public class CallEntry { public string Number; public string Name; public string Nature; public string Address; }
	}

	public class CallDetailModel
	{
		public string Number; public string Name; public string Nature; public string Address; public string Priority; public string State;
	}

	public class UnitsListModel
	{
		public string DepartmentName;
		public UnitEntry[] Units;
		public class UnitEntry { public string Name; public string Status; }
	}

	public class StatusResponseModel
	{
		public string StatusName; public string StaffingLevel;
	}

	public class MessagesListModel
	{
		public int UnreadCount;
		public MessageEntry[] Messages;
		public class MessageEntry { public string Id; public string Subject; public string From; public string Date; }
	}

	public class CalendarListModel
	{
		public string DepartmentName;
		public EventEntry[] Events;
		public class EventEntry { public string Start; public string Title; public string Type; }
	}

	public class ShiftsListModel
	{
		public string DepartmentName;
		public ShiftEntry[] Shifts;
		public class ShiftEntry { public string Start; public string End; public string Name; public string Station; }
	}

	public class PersonnelListModel
	{
		public string DepartmentName;
		public PersonnelEntry[] Personnel;
		public class PersonnelEntry { public string Name; public string Status; public string Staffing; public string Destination; }
	}

	public class HelpModel
	{
		public string HelpText;
	}

	public class ErrorModel
	{
		public string Message;
	}
}
