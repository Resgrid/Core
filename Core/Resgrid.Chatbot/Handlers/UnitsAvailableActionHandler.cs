using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Answers "units available?" (intent <see cref="ChatbotIntentType.UnitsAvailable"/>): units whose
	/// latest state classifies as Available via the canonical <see cref="AvailabilityMatrix"/> resolution
	/// (custom unit statuses resolve through their BaseType).
	/// </summary>
	public class UnitsAvailableActionHandler : IChatbotActionHandler
	{
		private const int MaxUnitsToList = 15;

		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IPlatformReportingService _platformReportingService;

		public UnitsAvailableActionHandler(
			IUnitsService unitsService,
			ICustomStateService customStateService,
			IPlatformReportingService platformReportingService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_platformReportingService = platformReportingService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.UnitsAvailable;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(session.DepartmentId);

				if (unitStatuses == null || !unitStatuses.Any())
					return new ChatbotResponse { Text = ChatbotResources.Get("Units_None", culture), Processed = true };

				var lines = new StringBuilder();
				var availableCount = 0;

				foreach (var unitState in unitStatuses.Where(u => u.Unit != null).OrderBy(u => u.Unit.Name))
				{
					var availability = await _platformReportingService.ClassifyUnitAvailabilityAsync(session.DepartmentId, unitState.State);
					if (availability != AvailabilityClass.Available)
						continue;

					availableCount++;
					if (availableCount > MaxUnitsToList)
						continue;

					var status = await _customStateService.GetCustomUnitStateAsync(unitState);
					var statusText = status?.ButtonText ?? ChatbotResources.Get("Personnel_Unknown", culture);
					lines.AppendLine(ChatbotResources.Get("UnitsAvail_Line", culture, unitState.Unit.Name, statusText));
				}

				if (availableCount == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("UnitsAvail_None", culture), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("UnitsAvail_Header", culture, availableCount));
				sb.AppendLine("----------------------");
				sb.Append(lines);

				if (availableCount > MaxUnitsToList)
					sb.AppendLine(ChatbotResources.Get("Msg_AndMore", culture, availableCount - MaxUnitsToList));

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("UnitsAvail_Error", culture), Processed = false };
			}
		}
	}
}
