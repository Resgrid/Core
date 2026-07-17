using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Two-level help. Bare HELP returns a short topic menu (SMS replies cost per segment, so the
	/// full command dump is never sent unsolicited); HELP &lt;topic&gt; returns the commands for that
	/// operation. Status/staffing topics reflect the department's custom states when configured.
	/// </summary>
	public class HelpActionHandler : IChatbotActionHandler
	{
		private readonly ICustomStateService _customStateService;

		public HelpActionHandler(ICustomStateService customStateService)
		{
			_customStateService = customStateService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.Help;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				string topic = null;
				intent?.Parameters?.TryGetValue("topic", out topic);
				topic = topic?.Trim().TrimEnd('?', '!', '.', ',').ToUpperInvariant();

				var text = topic switch
				{
					"STATUS" => await BuildStatusHelpAsync(session.DepartmentId),
					"STAFFING" => await BuildStaffingHelpAsync(session.DepartmentId),
					"CALLS" => BuildCallsHelp(),
					"MESSAGES" or "MSG" or "MESSAGE" => BuildMessagesHelp(),
					"UNITS" or "UNIT" => BuildUnitsHelp(),
					"SHIFTS" or "SHIFT" => BuildShiftsHelp(),
					"CALENDAR" or "EVENTS" => BuildCalendarHelp(),
					"PERSONNEL" or "STAFF" => BuildPersonnelHelp(),
					"DEPARTMENTS" or "DEPARTMENT" or "DEPTS" or "DEPT" => BuildDepartmentsHelp(),
					"STOP" => BuildStopHelp(),
					null or "" => BuildTopicMenu(),
					_ => $"Unknown help topic \"{topic}\".\n{BuildTopicMenu()}"
				};

				return new ChatbotResponse { Text = text, Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error generating help text.", Processed = false };
			}
		}

		private static string BuildTopicMenu()
		{
			return "Resgrid Chatbot. Text a command, or HELP <topic> for details.\n"
				+ "Topics: STATUS, STAFFING, CALLS, MESSAGES, UNITS, SHIFTS, CALENDAR, PERSONNEL, DEPARTMENTS, STOP";
		}

		private async Task<string> BuildStatusHelpAsync(int departmentId)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Status — text the word or number:");

			var customActions = await GetCustomStateAsync(departmentId, CustomStateTypes.Personnel);
			if (customActions != null)
			{
				// Custom statuses are reachable via the SET STATUS TO <name> form — the numeric/word
				// shortcuts only map to the standard statuses.
				var details = customActions.GetActiveDetails();
				for (int i = 0; i < details.Count; i++)
					sb.AppendLine($"SET STATUS TO {details[i].ButtonText}");
			}
			else
			{
				sb.AppendLine("1 or RESPONDING");
				sb.AppendLine("2 or NOTRESPONDING");
				sb.AppendLine("3 or ONSCENE");
				sb.AppendLine("4 or STANDINGBY");
			}

			sb.Append("STATUS shows your current status & staffing.");
			return sb.ToString();
		}

		private async Task<string> BuildStaffingHelpAsync(int departmentId)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Staffing — text the code or word:");

			var customStaffing = await GetCustomStateAsync(departmentId, CustomStateTypes.Staffing);
			if (customStaffing != null)
			{
				// Custom staffing levels are reachable via the SET STAFFING TO <name> form — the S-code
				// shortcuts only map to the standard levels.
				var details = customStaffing.GetActiveDetails();
				for (int i = 0; i < details.Count; i++)
					sb.AppendLine($"SET STAFFING TO {details[i].ButtonText}");
			}
			else
			{
				sb.AppendLine("S1 or AVAILABLE");
				sb.AppendLine("S2 or DELAYED");
				sb.AppendLine("S3 or UNAVAILABLE");
				sb.AppendLine("S4 or COMMITTED");
				sb.AppendLine("S5 or ONSHIFT");
			}

			sb.Append("STATUS shows your current status & staffing.");
			return sb.ToString();
		}

		private static string BuildCallsHelp()
		{
			return "Calls:\n"
				+ "CALLS: list active calls\n"
				+ "C<id>: call detail (e.g. C1445)\n"
				+ "RESPOND TO C<id>: mark responding to a call\n"
				+ "WHO'S ON <call>: who is responding/on scene\n"
				+ "WHO'S DISPATCHED TO <call>: the dispatch list\n"
				+ "WHAT CALLS AM I ON: your dispatched calls\n"
				+ "DISPATCH <details>: create a new call\n"
				+ "CLOSE CALL C<id>: close a call";
		}

		private static string BuildMessagesHelp()
		{
			return "Messages:\n"
				+ "MESSAGES or NEW MESSAGES: list unread messages\n"
				+ "#<id>: read a message\n"
				+ "REPLY YES/NO TO #<id>: respond\n"
				+ "YES or NO: answers your latest poll\n"
				+ "DELETE MSG <id>: delete\n"
				+ "SEND MESSAGE TO <name>: <text>";
		}

		private static string BuildUnitsHelp()
		{
			return "Units:\n"
				+ "UNITS: list unit statuses\n"
				+ "UNITS AVAILABLE: units free to respond\n"
				+ "WHAT CALLS IS <unit> ON: a unit's dispatched calls\n"
				+ "SET UNIT <name> TO <status>";
		}

		private static string BuildShiftsHelp()
		{
			return "Shifts:\n"
				+ "SHIFTS: list your shifts\n"
				+ "MY SCHEDULE [FOR <day>]: shifts + RSVP'd events\n"
				+ "SIGNUP SHIFT <id>: take a shift\n"
				+ "DROP SHIFT <id>: release a shift";
		}

		private static string BuildCalendarHelp()
		{
			return "Calendar:\n"
				+ "CALENDAR: upcoming events\n"
				+ "RSVP YES/NO/MAYBE TO <event>";
		}

		private static string BuildPersonnelHelp()
		{
			return "Personnel:\n"
				+ "PERSONNEL: personnel status list\n"
				+ "WHO'S AVAILABLE: who can respond right now\n"
				+ "WHO IS <name> / WHERE IS <name>\n"
				+ "POLL <question>: yes/no poll to all members (admins)";
		}

		private static string BuildDepartmentsHelp()
		{
			return "Departments:\n"
				+ "DEPARTMENTS: list your departments\n"
				+ "SWITCH <number or name>: change active department\n"
				+ "ACTIVE DEPARTMENT: show current";
		}

		private static string BuildStopHelp()
		{
			return "STOP turns off ALL Resgrid text messages to this number (calls, messages, notifications). "
				+ "Re-enable them on your Resgrid profile page.";
		}

		private async Task<CustomState> GetCustomStateAsync(int departmentId, CustomStateTypes type)
		{
			var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId);
			var state = customStates?.FirstOrDefault(x => x.Type == (int)type);

			if (state != null && !state.IsDeleted && state.GetActiveDetails()?.Any() == true)
				return state;

			return null;
		}
	}
}
