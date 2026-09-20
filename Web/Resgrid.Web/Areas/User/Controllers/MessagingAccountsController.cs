using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Framework;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class MessagingAccountsController : SecureBaseController
	{
		private readonly IChatbotUserIdentityService _identityService;
		private readonly CodeLinkingService _linkingService;
		private readonly IChatbotAdapterRegistry _adapterRegistry;

		public MessagingAccountsController(IChatbotUserIdentityService identityService,
			CodeLinkingService linkingService, IChatbotAdapterRegistry adapterRegistry)
		{
			_identityService = identityService;
			_linkingService = linkingService;
			_adapterRegistry = adapterRegistry;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (string.IsNullOrWhiteSpace(UserId))
				return Unauthorized();

			return View(await BuildModelAsync());
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Generate()
		{
			if (string.IsNullOrWhiteSpace(UserId))
				return Unauthorized();

			var model = await BuildModelAsync();
			if (!model.Platforms.Any(platform => platform.IsConfigured))
			{
				model.HasError = true;
				return View("Index", model);
			}

			try
			{
				var code = await _linkingService.GenerateCodeAsync(UserId);
				// Keep the short-lived credential only in this no-store response, never a URL or TempData.
				model.Code = code.Code;
				model.CodeExpiresAt = code.ExpiresAt;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				model.HasError = true;
			}

			return View("Index", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Unlink(string id)
		{
			if (string.IsNullOrWhiteSpace(UserId))
				return Unauthorized();
			if (string.IsNullOrWhiteSpace(id))
				return BadRequest();

			var identities = await _identityService.GetUserIdentitiesAsync(UserId);
			var identity = identities?.FirstOrDefault(account => account != null &&
				account.UserId == UserId && string.Equals(account.Id, id, StringComparison.Ordinal));
			if (identity == null || !(_adapterRegistry.GetAdapter(identity.Platform) is IExternalChatbotAdapter))
				return NotFound();

			try
			{
				await _identityService.UnlinkUserAsync(identity.Id);
				return RedirectToAction(nameof(Index));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				var model = await BuildModelAsync();
				model.HasError = true;
				return View("Index", model);
			}
		}

		private async Task<MessagingAccountsViewModel> BuildModelAsync()
		{
			var model = new MessagingAccountsViewModel();
			var supportedPlatforms = new HashSet<ChatbotPlatform>();
			foreach (var platform in Enum.GetValues<ChatbotPlatform>())
			{
				if (_adapterRegistry.GetAdapter(platform) is IExternalChatbotAdapter adapter)
				{
					supportedPlatforms.Add(platform);
					model.Platforms.Add(new MessagingPlatformViewModel
					{
						Name = PlatformName(platform),
						IsConfigured = adapter.IsInboundConfigured
					});
				}
			}

			var identities = await _identityService.GetUserIdentitiesAsync(UserId);
			if (identities != null)
			{
				model.Accounts = identities
					.Where(account => account != null && account.UserId == UserId && supportedPlatforms.Contains(account.Platform))
					.Select(account => new MessagingAccountViewModel
					{
						Id = account.Id,
						PlatformName = PlatformName(account.Platform),
						DisplayName = string.IsNullOrWhiteSpace(account.PlatformUserName) ? account.PlatformUserId : account.PlatformUserName
					}).ToList();
			}

			return model;
		}

		private static string PlatformName(ChatbotPlatform platform)
			=> platform switch
			{
				ChatbotPlatform.MicrosoftTeams => "Microsoft Teams",
				ChatbotPlatform.GoogleChat => "Google Chat",
				ChatbotPlatform.Line => "LINE",
				_ => platform.ToString()
			};
	}

	public class MessagingAccountsViewModel
	{
		public List<MessagingAccountViewModel> Accounts { get; set; } = new List<MessagingAccountViewModel>();
		public List<MessagingPlatformViewModel> Platforms { get; set; } = new List<MessagingPlatformViewModel>();
		public string Code { get; set; }
		public DateTime? CodeExpiresAt { get; set; }
		public bool HasError { get; set; }
	}

	public class MessagingAccountViewModel
	{
		public string Id { get; set; }
		public string PlatformName { get; set; }
		public string DisplayName { get; set; }
	}

	public class MessagingPlatformViewModel
	{
		public string Name { get; set; }
		public bool IsConfigured { get; set; }
	}
}
