using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize(Policy = ResgridResources.Department_Update)]
	public class ChatbotSettingsController : SecureBaseController
	{
		private readonly IChatbotDepartmentConfigService _chatbotConfigService;
		private readonly IAuthorizationService _authorizationService;

		public ChatbotSettingsController(IChatbotDepartmentConfigService chatbotConfigService, IAuthorizationService authorizationService)
		{
			_chatbotConfigService = chatbotConfigService;
			_authorizationService = authorizationService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var config = await _chatbotConfigService.GetConfigAsync(DepartmentId);

			var model = new ChatbotSettingsModel
			{
				IsEnabled = config?.IsEnabled ?? false,
				AllowedPlatforms = config?.AllowedPlatforms ?? "*",
				AllowDispatchViaChatbot = config?.AllowDispatchViaChatbot ?? false,
				RequireConfirmationForStatusChange = config?.RequireConfirmationForStatusChange ?? false,
				RequireLinkingConfirmation = config?.RequireLinkingConfirmation ?? true,
				ProactiveNotificationsEnabled = config?.ProactiveNotificationsEnabled ?? false,
				MessagesPerUserPerMinute = config?.MessagesPerUserPerMinute,
				MessagesPerDepartmentPerMinute = config?.MessagesPerDepartmentPerMinute,
				LlmApiEndpoint = config?.LlmApiEndpoint,
				LlmModelName = config?.LlmModelName,
				HasLlmApiKey = !string.IsNullOrWhiteSpace(config?.LlmApiKey)
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Index(ChatbotSettingsModel model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			try
			{
				var config = new ChatbotDepartmentConfig
				{
					DepartmentId = DepartmentId,
					IsEnabled = model.IsEnabled,
					AllowedPlatforms = string.IsNullOrWhiteSpace(model.AllowedPlatforms) ? "*" : model.AllowedPlatforms,
					AllowDispatchViaChatbot = model.AllowDispatchViaChatbot,
					RequireConfirmationForStatusChange = model.RequireConfirmationForStatusChange,
					RequireLinkingConfirmation = model.RequireLinkingConfirmation,
					ProactiveNotificationsEnabled = model.ProactiveNotificationsEnabled,
					MessagesPerUserPerMinute = model.MessagesPerUserPerMinute,
					MessagesPerDepartmentPerMinute = model.MessagesPerDepartmentPerMinute,
					LlmApiEndpoint = model.LlmApiEndpoint,
					LlmModelName = model.LlmModelName
				};

				// A blank key means "keep the existing one"; a value is encrypted and stored by the service.
				var newPlaintextKey = string.IsNullOrWhiteSpace(model.LlmApiKey) ? null : model.LlmApiKey;
				await _chatbotConfigService.SaveConfigAsync(config, newPlaintextKey);

				model.HasLlmApiKey = !string.IsNullOrWhiteSpace(config.LlmApiKey);
				model.LlmApiKey = null;
				model.Saved = true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				model.Saved = false;
			}

			return View(model);
		}
	}
}
