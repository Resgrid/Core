using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
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
		private readonly IChatbotDepartmentConfigService _departmentConfigService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatMessageService _chatMessageService;
		private readonly IQueueService _queueService;
		private readonly Resgrid.Model.Providers.IEventAggregator _eventAggregator;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IChatbotSessionManager _chatbotSessionManager;

		public ChatbotController(
			IChatbotUserIdentityService userIdentityService,
			OAuthLinkingService oauthLinkingService,
			CodeLinkingService codeLinkingService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService,
			IChatbotDepartmentConfigService departmentConfigService,
			IAuthorizationService authorizationService,
			IChatChannelService chatChannelService,
			IChatMessageService chatMessageService,
			IQueueService queueService,
			Resgrid.Model.Providers.IEventAggregator eventAggregator,
			IFeatureToggleService featureToggleService,
			IChatbotSessionManager chatbotSessionManager)
		{
			_userIdentityService = userIdentityService;
			_oauthLinkingService = oauthLinkingService;
			_codeLinkingService = codeLinkingService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_departmentConfigService = departmentConfigService;
			_authorizationService = authorizationService;
			_chatChannelService = chatChannelService;
			_chatMessageService = chatMessageService;
			_queueService = queueService;
			_eventAggregator = eventAggregator;
			_featureToggleService = featureToggleService;
			_chatbotSessionManager = chatbotSessionManager;
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
		public async Task<IActionResult> GenerateLinkingCode()
		{
			try
			{
				var userId = UserId;

				var linkingCode = await _codeLinkingService.GenerateCodeAsync(userId);

				return Ok(new
				{
					code = linkingCode.Code,
					expiresAt = linkingCode.ExpiresAt,
					instructions = $"Enter this code in your chat app to link your account. Code expires in {ChatbotConfig.LinkingCodeExpiryMinutes} minutes."
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
		/// Begins OAuth2 account linking (Discord, Slack). Returns the platform authorize URL the
		/// user should be sent to; a CSRF state is bound server-side to the authenticated user.
		/// </summary>
		[HttpGet("OAuthStart")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> OAuthStart([FromQuery] string platform)
		{
			try
			{
				var userId = UserId;

				if (!Enum.TryParse<ChatbotPlatform>(platform, true, out var chatbotPlatform))
					return BadRequest(new { error = "Invalid platform. Valid values: Discord, Slack" });

				var result = await _oauthLinkingService.StartLinkAsync(userId, chatbotPlatform);
				if (!result.Success)
					return BadRequest(new { error = result.Message });

				return Ok(new { authorizeUrl = result.AuthorizeUrl });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Completes OAuth2 account linking. The server exchanges the authorization code for the
		/// verified external user id (the client never supplies it) after validating the CSRF state.
		/// </summary>
		[HttpPost("OAuthComplete")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> OAuthComplete([FromBody] OAuthCompleteRequest request)
		{
			try
			{
				if (request == null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
					return BadRequest(new { error = "Missing code or state." });

				var userId = UserId;

				if (!Enum.TryParse<ChatbotPlatform>(request.Platform, true, out var chatbotPlatform))
					return BadRequest(new { error = "Invalid platform. Valid values: Discord, Slack" });

				var result = await _oauthLinkingService.ExchangeAndLinkAsync(userId, chatbotPlatform, request.Code, request.State);

				return Ok(new { success = result.Success, message = result.Message });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Gets this department's chatbot configuration (admin). The LLM API key is never returned;
		/// callers see only whether one is configured.
		/// </summary>
		[HttpGet("Config")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetConfig()
		{
			try
			{
				if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
					return Unauthorized();

				var config = await _departmentConfigService.GetConfigAsync(DepartmentId);

				return Ok(new
				{
					departmentId = DepartmentId,
					isEnabled = config?.IsEnabled ?? false,
					allowedPlatforms = config?.AllowedPlatforms ?? "*",
					maxSessionsPerUser = config?.MaxSessionsPerUser ?? 3,
					sessionTtlMinutes = config?.SessionTtlMinutes ?? 30,
					allowDispatchViaChatbot = config?.AllowDispatchViaChatbot ?? false,
					requireConfirmationForStatusChange = config?.RequireConfirmationForStatusChange ?? false,
					requireLinkingConfirmation = config?.RequireLinkingConfirmation ?? true,
					proactiveNotificationsEnabled = config?.ProactiveNotificationsEnabled ?? false,
					messagesPerUserPerMinute = config?.MessagesPerUserPerMinute,
					messagesPerDepartmentPerMinute = config?.MessagesPerDepartmentPerMinute,
					llmApiEndpoint = config?.LlmApiEndpoint,
					llmModelName = config?.LlmModelName,
					hasLlmApiKey = !string.IsNullOrWhiteSpace(config?.LlmApiKey)
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Creates or updates this department's chatbot configuration (admin). A department may set
		/// its own LLM endpoint/key/model so its NLU processing stays with their provider. For the
		/// key: omit (null) to keep the existing one, send "" to clear it, or send a value to set it.
		/// </summary>
		[HttpPut("Config")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> UpdateConfig([FromBody] ChatbotConfigRequest request)
		{
			try
			{
				if (request == null)
					return BadRequest(new { error = "Missing configuration." });

				if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
					return Unauthorized();

				var config = new ChatbotDepartmentConfig
				{
					DepartmentId = DepartmentId,
					IsEnabled = request.IsEnabled,
					AllowedPlatforms = string.IsNullOrWhiteSpace(request.AllowedPlatforms) ? "*" : request.AllowedPlatforms,
					MaxSessionsPerUser = request.MaxSessionsPerUser ?? 3,
					SessionTtlMinutes = request.SessionTtlMinutes ?? 30,
					AllowDispatchViaChatbot = request.AllowDispatchViaChatbot,
					RequireConfirmationForStatusChange = request.RequireConfirmationForStatusChange,
					RequireLinkingConfirmation = request.RequireLinkingConfirmation,
					ProactiveNotificationsEnabled = request.ProactiveNotificationsEnabled,
					MessagesPerUserPerMinute = request.MessagesPerUserPerMinute,
					MessagesPerDepartmentPerMinute = request.MessagesPerDepartmentPerMinute,
					LlmApiEndpoint = request.LlmApiEndpoint,
					LlmModelName = request.LlmModelName
				};

				await _departmentConfigService.SaveConfigAsync(config, request.LlmApiKey);

				return Ok(new { success = true });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		#region Web Chat conversation

		/// <summary>
		/// Gets (creating if needed) the caller's chatbot conversation channel.
		/// </summary>
		[HttpGet("GetChatChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetChatChannel()
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.EnsureChatbotChannelAsync(DepartmentId, UserId);
			if (channel == null)
				return NotFound();

			return Ok(new { channel.ChatChannelId, channel.Name, channel.LastMessageSeq, channel.LastMessageOn });
		}

		/// <summary>
		/// Sends a message to the chatbot. The message is persisted to the caller's chatbot channel and
		/// processed asynchronously by the chatbot pipeline; the reply arrives in the same channel over
		/// SignalR (chatMessageReceived). Idempotent via clientMessageId.
		/// </summary>
		[HttpPost("SendChatMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> SendChatMessage([FromBody] ChatbotChatMessageRequest request)
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			if (request == null || string.IsNullOrWhiteSpace(request.Text))
				return BadRequest(new { error = "Message text is required." });

			try
			{
				var channel = await _chatChannelService.EnsureChatbotChannelAsync(DepartmentId, UserId);
				if (channel == null)
					return NotFound();

				var message = await _chatMessageService.SendMessageAsync(new ChatMessageSendRequest
				{
					ChatChannelId = channel.ChatChannelId,
					DepartmentId = DepartmentId,
					SenderUserId = UserId,
					Body = request.Text.Trim(),
					MessageType = ChatMessageType.Text,
					Priority = ChatMessagePriority.Normal,
					ClientMessageId = request.ClientMessageId
				});

				if (message == null)
					return BadRequest(new { error = "Unable to send message." });

				await _queueService.EnqueueChatbotMessageAsync(new Resgrid.Model.Queue.ChatbotMessageQueueItem
				{
					DepartmentId = DepartmentId,
					From = UserId,
					Body = message.Body,
					MessageId = message.ChatMessageId,
					Platform = (int)Resgrid.Chatbot.Models.ChatbotPlatform.WebChat
				});

				// Typing indicator to the user's devices while the worker runs the pipeline.
				_eventAggregator.SendMessage<Resgrid.Model.Events.ChatEventRaised>(new Resgrid.Model.Events.ChatEventRaised
				{
					DepartmentId = DepartmentId,
					ChatChannelId = channel.ChatChannelId,
					Kind = Resgrid.Model.Events.ChatEventKinds.ChatbotTyping,
					TargetUserId = UserId,
					PayloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { channel.ChatChannelId, IsTyping = true })
				});

				return Ok(new { message.ChatMessageId, message.MessageSeq, message.SentOn });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = "Unable to send message." });
			}
		}

		/// <summary>
		/// Resets the chatbot conversational session (context/pending intents). Message history remains.
		/// </summary>
		[HttpPost("NewChatSession")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> NewChatSession()
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			try
			{
				var session = await _chatbotSessionManager.GetOrCreateSessionAsync(UserId, DepartmentId, Resgrid.Chatbot.Models.ChatbotPlatform.WebChat, UserId);
				if (session != null)
					await _chatbotSessionManager.EndSessionAsync(session.SessionId);

				return Ok(new { success = true });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = ex.Message });
			}
		}

		private async Task<bool> ChatbotChatEnabledAsync()
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId))
				return false;

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(DepartmentId);
			return settings == null || settings.ChatbotEnabled;
		}

		#endregion Web Chat conversation
	}

	public class ChatbotChatMessageRequest
	{
		public string Text { get; set; }
		public string ClientMessageId { get; set; }
	}

	public class UnlinkRequest
	{
		public string Platform { get; set; }
		public string PlatformUserId { get; set; }
	}

	public class ChatbotConfigRequest
	{
		public bool IsEnabled { get; set; }
		public string AllowedPlatforms { get; set; }
		public int? MaxSessionsPerUser { get; set; }
		public int? SessionTtlMinutes { get; set; }
		public bool AllowDispatchViaChatbot { get; set; }
		public bool RequireConfirmationForStatusChange { get; set; }
		public bool RequireLinkingConfirmation { get; set; }
		public bool ProactiveNotificationsEnabled { get; set; }
		public int? MessagesPerUserPerMinute { get; set; }
		public int? MessagesPerDepartmentPerMinute { get; set; }
		public string LlmApiEndpoint { get; set; }
		public string LlmApiKey { get; set; }
		public string LlmModelName { get; set; }
	}

	public class OAuthCompleteRequest
	{
		public string Platform { get; set; }
		public string Code { get; set; }
		public string State { get; set; }
	}
}
