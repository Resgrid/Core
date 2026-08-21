using System;
using System.Collections.Generic;
using System.Linq;
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
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Chat;
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
		private readonly IChatbotIngressService _chatbotIngressService;
		private readonly ICallsService _callsService;

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
			IChatbotSessionManager chatbotSessionManager,
			IChatbotIngressService chatbotIngressService,
			ICallsService callsService)
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
			_chatbotIngressService = chatbotIngressService;
			_callsService = callsService;
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
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
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
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
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
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
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
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
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
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
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
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
			}
		}

		/// <summary>
		/// Creates or updates this department's chatbot configuration (admin). A department may set
		/// its own LLM endpoint/key/model so its NLU processing stays with their provider. For the
		/// key: omit (null) to keep the existing one, send "" to clear it, or send a value to set it.
		/// </summary>
		[HttpPut("Config")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<IActionResult> UpdateConfig([FromBody] ChatbotConfigRequest request)
		{
			try
			{
				if (request == null)
					return BadRequest(new { error = "Missing configuration." });

				if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
					return Unauthorized();

				if (!string.IsNullOrWhiteSpace(request.LlmApiEndpoint) &&
					!Resgrid.Chatbot.NLU.LlmEndpointValidator.IsValid(request.LlmApiEndpoint, out var llmEndpointError))
					return BadRequest(new { error = llmEndpointError });

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
			catch (ArgumentException ex)
			{
				// Field-length (column size) validation from the config service.
				return BadRequest(new { error = ex.Message });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
			}
		}

		#region Web Chat conversation

		/// <summary>
		/// Gets (creating if needed) the caller's chatbot conversation channel.
		/// </summary>
		[HttpGet("GetChatChannel")]
		[Authorize(Policy = ResgridResources.Chat_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ChatbotChannelResult>> GetChatChannel()
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.EnsureChatbotChannelAsync(DepartmentId, UserId);
			if (channel == null)
				return NotFound();

			var result = new ChatbotChannelResult
			{
				Data = new ChatbotChannelResultData
				{
					ChatChannelId = channel.ChatChannelId,
					Name = channel.Name,
					LastMessageSeq = channel.LastMessageSeq,
					LastMessageOn = channel.LastMessageOn
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Sends a message to the chatbot. The message is persisted to the caller's chatbot channel and
		/// processed asynchronously by the chatbot pipeline; the reply arrives in the same channel over
		/// SignalR (chatMessageReceived). Idempotent via clientMessageId.
		/// </summary>
		[HttpPost("SendChatMessage")]
		[Authorize(Policy = ResgridResources.Chat_View)]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ChatbotMessageSentResult>> SendChatMessage([FromBody] ChatbotChatMessageRequest request)
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

				var message = await _chatMessageService.SendMessageAsync(DepartmentId, UserId, new ChatMessageSendRequest
				{
					ChatChannelId = channel.ChatChannelId,
					DepartmentId = DepartmentId,
					Body = request.Text.Trim(),
					MessageType = ChatMessageType.Text,
					Priority = ChatMessagePriority.Normal,
					ClientMessageId = request.ClientMessageId
				});

				if (message == null)
					return BadRequest(new { error = "Unable to send message." });

				var queued = await _queueService.EnqueueChatbotMessageAsync(new Resgrid.Model.Queue.ChatbotMessageQueueItem
				{
					DepartmentId = DepartmentId,
					From = UserId,
					Body = message.Body,
					MessageId = message.ChatMessageId,
					Platform = (int)Resgrid.Chatbot.Models.ChatbotPlatform.WebChat,
					// Optional: which incident the sender has open, so board questions ("PAR") resolve
					// against it. Treated as a hint — the pipeline re-checks scoping and permission.
					IncidentCallId = request.CallId > 0 ? request.CallId : (int?)null
				});

				if (!queued)
					return StatusCode(StatusCodes.Status500InternalServerError, BuildMessageSentResult(message, ResponseHelper.Failure));

				// Typing indicator to the user's devices while the worker runs the pipeline.
				_eventAggregator.SendMessage<Resgrid.Model.Events.ChatEventRaised>(new Resgrid.Model.Events.ChatEventRaised
				{
					DepartmentId = DepartmentId,
					ChatChannelId = channel.ChatChannelId,
					Kind = Resgrid.Model.Events.ChatEventKinds.ChatbotTyping,
					TargetUserId = UserId,
					PayloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { channel.ChatChannelId, IsTyping = true })
				});

				return BuildMessageSentResult(message, ResponseHelper.Created);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = "Unable to send message." });
			}
		}

		/// <summary>Builds the V4-populated send-result envelope shared by the success and failure paths.</summary>
		private static ChatbotMessageSentResult BuildMessageSentResult(ChatMessage message, string status)
		{
			var result = new ChatbotMessageSentResult
			{
				Data = new ChatbotMessageSentResultData
				{
					ChatMessageId = message.ChatMessageId,
					MessageSeq = message.MessageSeq,
					SentOn = message.SentOn
				},
				PageSize = 1,
				Status = status
			};

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Resets the chatbot conversational session (context/pending intents). Message history remains.
		/// </summary>
		[HttpPost("NewChatSession")]
		[Authorize(Policy = ResgridResources.Chat_View)]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ChatbotSessionResetResult>> NewChatSession()
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			try
			{
				var channel = await _chatChannelService.EnsureChatbotChannelAsync(DepartmentId, UserId);
				if (channel == null)
					return NotFound();

				var session = await _chatbotSessionManager.GetOrCreateSessionAsync(UserId, DepartmentId, Resgrid.Chatbot.Models.ChatbotPlatform.WebChat, UserId);
				if (session != null)
					await _chatbotSessionManager.EndSessionAsync(session.SessionId);

				// Visible confirmation in the conversation (fans out over SignalR to every client);
				// without it the reset is silent and looks like the button did nothing.
				await _chatMessageService.SendBotMessageAsync(channel.ChatChannelId,
					DepartmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
					"Starting a new conversation — your previous context has been cleared.", "Resgrid Assistant");

				var result = new ChatbotSessionResetResult
				{
					Success = true,
					PageSize = 1,
					Status = ResponseHelper.Success
				};

				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = "Unable to reset the chat session." });
			}
		}

		#endregion Web Chat conversation

		#region Incident command assistant

		/// <summary>
		/// Asks the incident assistant a question about a command board and returns the answer
		/// synchronously. Unlike SendChatMessage (queued, reply fans out over SignalR), a commander
		/// working a board needs the answer in the same round-trip, so the chatbot pipeline runs inline.
		/// The same authorization, per-department gating and rate limiting apply — this is the pipeline,
		/// not a bypass of it.
		/// </summary>
		[HttpPost("AskIncident")]
		[Authorize(Policy = ResgridResources.Chat_View)]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<IncidentAssistantAnswerResult>> AskIncident([FromBody] AskIncidentAssistantInput input)
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			if (input == null || string.IsNullOrWhiteSpace(input.Question))
				return BadRequest(new { error = "A question is required." });

			try
			{
				var message = new ChatbotMessage
				{
					MessageId = Guid.NewGuid().ToString(),
					From = UserId,
					To = DepartmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
					Text = input.Question.Trim(),
					Platform = ChatbotPlatform.WebChat,
					Timestamp = DateTime.UtcNow
				};

				if (input.CallId > 0)
					message.PlatformMetadata["incidentCallId"] = input.CallId;

				var response = await _chatbotIngressService.ProcessMessageAsync(message);

				var result = new IncidentAssistantAnswerResult
				{
					Data = new IncidentAssistantAnswerResultData
					{
						Answer = response?.Text ?? string.Empty,
						Intent = response?.Intent?.Type.ToString() ?? ChatbotIntentType.Unknown.ToString(),
						Confidence = response?.Intent?.Confidence ?? 0,
						Processed = response?.Processed ?? false
					},
					PageSize = 1,
					Status = ResponseHelper.Success
				};

				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest(new { error = "Unable to answer that right now." });
			}
		}

		/// <summary>
		/// Questions worth putting in front of the commander for this incident, chosen from the ICS
		/// playbook inferred from the call's type/name/nature. The IC app ships the same playbooks so it
		/// can build these offline; this endpoint keeps a server-side department in sync with them.
		/// </summary>
		[HttpGet("IncidentSuggestions")]
		[Authorize(Policy = ResgridResources.Chat_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IncidentAssistantSuggestionsResult>> IncidentSuggestions([FromQuery] int callId)
		{
			if (!await ChatbotChatEnabledAsync())
				return NotFound();

			try
			{
				Resgrid.Model.Call call = null;

				if (callId > 0)
				{
					call = await _callsService.GetCallByIdAsync(callId);

					// Tenant isolation: a call from another department reads as not-found, and the caller
					// still needs view permission on their own department's call.
					if (call != null && (call.DepartmentId != DepartmentId || !await _authorizationService.CanUserViewCallAsync(UserId, callId)))
						call = null;
				}

				var playbook = IcsPlaybooks.Infer(call);

				var result = new IncidentAssistantSuggestionsResult
				{
					Data = new IncidentAssistantSuggestionsResultData
					{
						IncidentType = playbook.DisplayName,
						IncidentTypeKey = playbook.Type.ToString(),
						Questions = playbook.SuggestedQuestions.ToList()
					},
					PageSize = 1,
					Status = ResponseHelper.Success
				};

				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
			}
		}

		#endregion Incident command assistant

		private async Task<bool> ChatbotChatEnabledAsync()
		{
			if (!await _authorizationService.IsUserValidWithinLimitsAsync(UserId, DepartmentId))
				return false;

			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId))
				return false;

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(DepartmentId);
			if (settings != null && !settings.ChatbotEnabled)
				return false;

			// Honor the admin toggle from Department Settings > Assistant. Matching the ingress
			// pipeline's semantics, a missing config row means enabled.
			var config = await _departmentConfigService.GetConfigAsync(DepartmentId);
			return config == null || config.IsEnabled;
		}
	}

	public class ChatbotChatMessageRequest
	{
		public string Text { get; set; }
		public string ClientMessageId { get; set; }

		/// <summary>
		/// Optional: the incident (call id) the sender has open on a command board, so incident
		/// questions resolve against that board instead of guessing. 0 or absent for general chat.
		/// </summary>
		public int CallId { get; set; }
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
