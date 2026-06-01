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
				var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(session.DepartmentId);
				var customActions = customStates?.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Personnel);
				var customStaffing = customStates?.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Staffing);

				var sb = new StringBuilder();
				sb.AppendLine("Resgrid Chatbot Commands");
				sb.AppendLine("----------------------");
				sb.AppendLine("Quick Reference — text any command:");
				sb.AppendLine();
				sb.AppendLine("-- Core --");
				sb.AppendLine("HELP: Show this help");
				sb.AppendLine("STOP: End chatbot session");
				sb.AppendLine("STATUS: Your current status & staffing");
				sb.AppendLine("CALLS: List active calls");
				sb.AppendLine("C#####: Call detail (e.g., C1445)");
				sb.AppendLine("UNITS: List unit statuses");
				sb.AppendLine("MSG: List messages");
				sb.AppendLine("CALENDAR: Upcoming events");
				sb.AppendLine("PERSONNEL: Personnel status list");
				sb.AppendLine();
				sb.AppendLine("-- Status --");

				if (customActions != null && !customActions.IsDeleted && customActions.GetActiveDetails()?.Any() == true)
				{
					var details = customActions.GetActiveDetails();
					for (int i = 0; i < details.Count; i++)
					{
						sb.AppendLine($"{details[i].ButtonText?.Replace(" ", "")} or {i + 1}: {details[i].ButtonText}");
					}
				}
				else
				{
					sb.AppendLine("1 or RESPONDING: Responding");
					sb.AppendLine("2 or NOTRESPONDING: Not Responding");
					sb.AppendLine("3 or ONSCENE: On Scene");
					sb.AppendLine("4 or STANDINGBY: Standing By");
				}

				sb.AppendLine();
				sb.AppendLine("-- Staffing --");

				if (customStaffing != null && !customStaffing.IsDeleted && customStaffing.GetActiveDetails()?.Any() == true)
				{
					var details = customStaffing.GetActiveDetails();
					for (int i = 0; i < details.Count; i++)
					{
						sb.AppendLine($"S{i + 1}: {details[i].ButtonText}");
					}
				}
				else
				{
					sb.AppendLine("S1 or AVAILABLE: Available");
					sb.AppendLine("S2 or DELAYED: Delayed");
					sb.AppendLine("S3 or UNAVAILABLE: Unavailable");
					sb.AppendLine("S4 or COMMITTED: Committed");
					sb.AppendLine("S5 or ONSHIFT: On Shift");
				}

				sb.AppendLine();
				sb.AppendLine("-- Departments --");
				sb.AppendLine("DEPARTMENTS: List your departments");
				sb.AppendLine("ACTIVE DEPARTMENT: Show current department");
				sb.AppendLine("SWITCH DEPARTMENT [name]: Switch departments");

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error generating help text.", Processed = false };
			}
		}
	}
}
