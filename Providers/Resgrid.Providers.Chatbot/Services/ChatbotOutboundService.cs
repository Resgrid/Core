using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Providers.Chatbot.Services
{
	/// <summary>
	/// Real outbound chat delivery (Phase 3 §15). Given a Resgrid user, resolves their linked chat
	/// identities and pushes the message to each platform the bot can proactively reach, gated by the
	/// department's proactive-notification opt-in and allowed-platform list. SMS identities are skipped
	/// (the SMS channel in <see cref="ICommunicationService"/> already handles those). A failure on one
	/// platform never blocks the others or the SMS/email/push channels.
	/// </summary>
	public class ChatbotOutboundService : IChatbotOutboundService
	{
		private readonly IChatbotUserIdentityService _identityService;
		private readonly IChatbotAdapterRegistry _adapterRegistry;
		private readonly IChatbotDepartmentConfigService _departmentConfigService;

		public ChatbotOutboundService(
			IChatbotUserIdentityService identityService,
			IChatbotAdapterRegistry adapterRegistry,
			IChatbotDepartmentConfigService departmentConfigService)
		{
			_identityService = identityService;
			_adapterRegistry = adapterRegistry;
			_departmentConfigService = departmentConfigService;
		}

		public async Task<ChatbotOutboundResult> SendToUserAsync(string userId, int departmentId, ChatbotOutboundMessage message)
		{
			var result = new ChatbotOutboundResult();
			if (string.IsNullOrWhiteSpace(userId) || message == null)
				return result;

			try
			{
				// Department gate: proactive chat delivery must be opted in for this department (§15.2).
				var config = await _departmentConfigService.GetConfigAsync(departmentId);
				if (config != null && !config.ProactiveNotificationsEnabled)
					return result;

				var identities = await _identityService.GetUserIdentitiesAsync(userId);
				if (identities == null)
					return result;

				foreach (var identity in identities)
				{
					if (identity == null || !identity.IsActive)
						continue;

					// SMS is delivered by the dedicated SMS channel; don't double-handle it here.
					if (identity.Platform == ChatbotPlatform.SmsTwilio || identity.Platform == ChatbotPlatform.SmsSignalWire)
						continue;

					if (config != null && !IsPlatformAllowed(config.AllowedPlatforms, identity.Platform))
						continue;

					// Platforms that can't be initiated proactively yet are skipped so the caller can
					// fall back to the user's other channels (SMS/push).
					if (!_adapterRegistry.CanInitiateProactively(identity.Platform))
						continue;

					var adapter = _adapterRegistry.GetAdapter(identity.Platform);
					if (adapter == null)
						continue;

					try
					{
						var response = new ChatbotResponse
						{
							Text = string.IsNullOrWhiteSpace(message.Title) ? message.Body : $"{message.Title}\n{message.Body}",
							Processed = true
						};
						await adapter.SendRichResponseAsync(identity.PlatformUserId, response);
						result.DeliveredPlatforms.Add(identity.Platform.ToString());
					}
					catch (Exception exSend)
					{
						Logging.LogException(exSend);
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return result;
		}

		private static bool IsPlatformAllowed(string allowedPlatforms, ChatbotPlatform platform)
		{
			if (string.IsNullOrWhiteSpace(allowedPlatforms) || allowedPlatforms.Trim() == "*")
				return true;

			var name = platform.ToString();
			foreach (var entry in allowedPlatforms.Split(','))
			{
				if (string.Equals(entry.Trim(), name, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}
	}
}
