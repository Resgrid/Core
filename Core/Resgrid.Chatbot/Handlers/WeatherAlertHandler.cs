using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Lists active weather alerts for the user's department (intent <see cref="ChatbotIntentType.WeatherAlert"/>).
	/// Department-read (membership enforced at ingress §2). Responses are localized to the user's culture.
	/// </summary>
	public class WeatherAlertHandler : IChatbotActionHandler
	{
		private const int MaxAlerts = 10;

		private readonly IWeatherAlertService _weatherAlertService;

		public WeatherAlertHandler(IWeatherAlertService weatherAlertService)
		{
			_weatherAlertService = weatherAlertService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.WeatherAlert;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var alerts = await _weatherAlertService.GetActiveAlertsByDepartmentIdAsync(session.DepartmentId);

				if (alerts == null || alerts.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Weather_NoAlerts", culture), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Weather_Header", culture));
				sb.AppendLine("----------------------");

				foreach (var alert in alerts.OrderByDescending(a => a.Severity).Take(MaxAlerts))
				{
					var headline = string.IsNullOrWhiteSpace(alert.Headline) ? alert.Event : alert.Headline;
					sb.AppendLine(ChatbotResources.Get("Weather_AlertItem", culture, headline?.Truncate(90)));
					sb.AppendLine(ChatbotResources.Get("Weather_Effective", culture, alert.EffectiveUtc.ToString("g")));
					if (alert.ExpiresUtc.HasValue)
						sb.AppendLine(ChatbotResources.Get("Weather_Expires", culture, alert.ExpiresUtc.Value.ToString("g")));
				}

				if (alerts.Count > MaxAlerts)
					sb.AppendLine(ChatbotResources.Get("Msg_AndMore", culture, alerts.Count - MaxAlerts));

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Weather_Error", culture), Processed = false };
			}
		}
	}
}
