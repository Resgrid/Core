using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Chatbot account linking and management API.
	/// Endpoints for linking platform accounts to Resgrid users.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ChatbotController : V4AuthenticatedApiControllerbase
	{
		private readonly IChatbotUserIdentityService _userIdentityService;
		private readonly OAuthLinkingService _oauthLinkingService;
		private readonly CodeLinkingService _codeLinkingService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;

		public ChatbotController(
			IChatbotUserIdentityService userIdentityService,
			OAuthLinkingService oauthLinkingService,
			CodeLinkingService codeLinkingService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService)
		{
			_userIdentityService = userIdentityService;
			_oauthLinkingService = oauthLinkingService;
			_codeLinkingService = codeLinkingService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
		}

		/// <summary>
		/// Gets all linked platform identities for the current user.
		/// </summary>
		[HttpGet("GetLinkedAccounts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetLinkedAccounts()
		{
			try
			{
				var userId = UserId;

				var identities = await _userIdentityService.GetUserIdentitiesAsync(userId);

				var result = new List<object>();
				foreach (var id in identities)
				{
					result.Add(new
					{
						Platform = id.Platform.ToString(),
						PlatformUserId = id.PlatformUserId,
						PlatformUserName = id.PlatformUserName,
						CreatedAt = id.CreatedAt,
						LastUsedAt = id.LastUsedAt,
						IsActive = id.IsActive
					});
				}

				return Ok(result);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Generates a linking code for Telegram, Signal, or other code-based platforms.
		/// The user enters this code in their chat app to link their account.
		/// </summary>
		[HttpPost("GenerateLinkingCode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public IActionResult GenerateLinkingCode()
		{
			try
			{
				var userId = UserId;

				var linkingCode = _codeLinkingService.GenerateCode(userId);

				return Ok(new
				{
					code = linkingCode.Code,
					expiresAt = linkingCode.ExpiresAt,
					instructions = "Enter this code in your chat app to link your account. Code expires in 15 minutes."
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Unlinks a platform identity from the current user.
		/// </summary>
		[HttpPost("UnlinkAccount")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> UnlinkAccount([FromBody] UnlinkRequest request)
		{
			try
			{
				if (request == null || string.IsNullOrWhiteSpace(request.PlatformUserId))
					return BadRequest(new { error = "Missing platformUserId." });

				var userId = UserId;

				if (!Enum.TryParse<ChatbotPlatform>(request.Platform, true, out var platform))
					return BadRequest(new { error = "Invalid platform. Valid values: SmsTwilio, SmsSignalWire, Discord, Slack, Telegram, Signal, WebChat" });

				var result = await _oauthLinkingService.UnlinkAsync(userId, platform, request.PlatformUserId);

				return Ok(new { success = result.Success, message = result.Message });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Links a platform account via OAuth2 (Discord, Slack).
		/// Called after the OAuth2 flow completes with the external user ID.
		/// </summary>
		[HttpPost("LinkViaOAuth")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> LinkViaOAuth([FromBody] OAuthLinkRequest request)
		{
			try
			{
				if (request == null || string.IsNullOrWhiteSpace(request.ExternalUserId))
					return BadRequest(new { error = "Missing externalUserId." });

				var userId = UserId;

				if (!Enum.TryParse<ChatbotPlatform>(request.Platform, true, out var platform))
					return BadRequest(new { error = "Invalid platform. Valid values: Discord, Slack" });

				var result = await _oauthLinkingService.LinkViaOAuthAsync(
					userId,
					platform,
					request.ExternalUserId,
					request.ExternalUsername ?? string.Empty,
					request.DisplayName ?? string.Empty);

				return Ok(new { success = result.Success, message = result.Message });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}
	}

	public class UnlinkRequest
	{
		public string Platform { get; set; }
		public string PlatformUserId { get; set; }
	}

	public class OAuthLinkRequest
	{
		public string Platform { get; set; }
		public string ExternalUserId { get; set; }
		public string ExternalUsername { get; set; }
		public string DisplayName { get; set; }
	}
}
