using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	public class ChatbotIngressService : IChatbotIngressService
	{
		private readonly IChatbotUserIdentityService _userIdentityService;
		private readonly IChatbotSessionManager _sessionManager;
		private readonly IChatbotIntentRouter _intentRouter;
		private readonly IEnumerable<IChatbotActionHandler> _actionHandlers;
		private readonly IConversationEngine _conversationEngine;
		private readonly IChatbotTemplateRenderer _templateRenderer;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ILimitsService _limitsService;

		public ChatbotIngressService(
			IChatbotUserIdentityService userIdentityService,
			IChatbotSessionManager sessionManager,
			IChatbotIntentRouter intentRouter,
			IEnumerable<IChatbotActionHandler> actionHandlers,
			IConversationEngine conversationEngine,
			IChatbotTemplateRenderer templateRenderer,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService,
			ILimitsService limitsService)
		{
			_userIdentityService = userIdentityService;
			_sessionManager = sessionManager;
			_intentRouter = intentRouter;
			_actionHandlers = actionHandlers;
			_conversationEngine = conversationEngine;
			_templateRenderer = templateRenderer;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_limitsService = limitsService;
		}

		public async Task<ChatbotResponse> ProcessMessageAsync(ChatbotMessage message)
		{
			if (message == null || string.IsNullOrWhiteSpace(message.Text))
				return new ChatbotResponse { Text = string.Empty, Processed = false };

			try
			{
				// 1. Identify user from the platform-specific identifier
				var identity = await ResolveUserIdentityAsync(message);

				if (identity == null || string.IsNullOrWhiteSpace(identity.UserId))
				{
					return await _templateRenderer.RenderResponseAsync("error",
						new Services.ErrorModel { Message = "We couldn't identify your account. Please link your account via the Resgrid web portal or contact your administrator." },
						message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
				}

				identity.LastUsedAt = DateTime.UtcNow;

				// 2. Get active department for this user (respects IsActive flag for multi-dept users)
				var department = await ResolveActiveDepartmentAsync(identity.UserId);
				if (department == null)
				{
					return await _templateRenderer.RenderResponseAsync("error",
						new Services.ErrorModel { Message = "You are not currently a member of any department. Please contact your administrator." },
						message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
				}

				// 3. Check department authorization
				var isAuthorized = await _limitsService.CanDepartmentProvisionNumberAsync(department.DepartmentId);
				if (!isAuthorized)
				{
					return await _templateRenderer.RenderResponseAsync("error",
						new Services.ErrorModel { Message = "Your department's plan does not support chatbot features. Please upgrade your plan." },
						message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
				}

				// 4. Get or create session
				var session = await _sessionManager.GetOrCreateSessionAsync(
					identity.UserId,
					department.DepartmentId,
					message.Platform,
					message.From);

				// Add message to session history
				session.RecentMessages.Add(message);
				if (session.RecentMessages.Count > 20)
					session.RecentMessages.RemoveAt(0);

				// 5. Handle session state machine via ConversationEngine (Phase 2)
				if (session.State != ChatbotDialogState.Idle)
				{
					var continuationResult = await _conversationEngine.HandleContinuationAsync(message, session);
					if (continuationResult != null)
					{
						await _sessionManager.SaveSessionAsync(session);
						return continuationResult;
					}
				}

				// 6. Classify intent
				var intent = await _intentRouter.ClassifyIntentAsync(message, session);

				// 7. Special handling: Stop command ends session
				if (intent.Type == ChatbotIntentType.Stop)
				{
					await _sessionManager.EndSessionAsync(session.SessionId);
					return new ChatbotResponse
					{
						Text = "Chatbot session ended. Text HELP to start a new session.",
						Processed = true,
						Intent = intent
					};
				}

				// 8. Begin conversation for new multi-turn intents
				if (_conversationEngine.IsMultiTurnIntent(intent.Type))
				{
					var turnResult = await _conversationEngine.BeginDialogAsync(intent, session);
					if (turnResult is ChatbotResponse dialogResponse && dialogResponse.Processed)
					{
						await _sessionManager.SaveSessionAsync(session);
						return dialogResponse;
					}
				}

				// 9. Dispatch to action handler (supports multi-intent handlers via CanHandle)
				var handler = _actionHandlers.FirstOrDefault(h => h.CanHandle(intent.Type));
				if (handler != null)
				{
					var response = await handler.HandleAsync(message, intent, session);
					response.Intent = intent;
					await _sessionManager.SaveSessionAsync(session);
					return response;
				}

				// Unknown intent
				return new ChatbotResponse
				{
					Text = "I didn't understand that command. Text HELP to see available commands.",
					Processed = false,
					Intent = intent
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse
				{
					Text = "An error occurred processing your request. Please try again later.",
					Processed = false
				};
			}
		}

		private async Task<ChatbotUserIdentity> ResolveUserIdentityAsync(ChatbotMessage message)
		{
			// Try platform-specific identity first
			var identity = await _userIdentityService.GetIdentityAsync(message.Platform, message.From);

			// For SMS platforms, fall back to phone number lookup via UserProfile
			if (identity == null && (message.Platform == ChatbotPlatform.SmsTwilio || message.Platform == ChatbotPlatform.SmsSignalWire))
			{
				var cleanPhone = message.From?.Replace("+", "").Trim();
				if (!string.IsNullOrWhiteSpace(cleanPhone))
				{
					var profile = await _userProfileService.GetProfileByMobileNumberAsync(cleanPhone);
					if (profile != null)
					{
						identity = await _userIdentityService.LinkUserAsync(
							profile.UserId,
							message.Platform,
							message.From,
							profile.FullName.AsFirstNameLastName,
							"phone_match");
					}
				}
			}

			// Generic phone-based lookup for any platform
			if (identity == null)
			{
				var cleanPhone = message.From?.Replace("+", "").Trim();
				if (!string.IsNullOrWhiteSpace(cleanPhone))
				{
					identity = await _userIdentityService.GetIdentityByPhoneAsync(cleanPhone);
				}
			}

			return identity;
		}

		private async Task<Model.Department> ResolveActiveDepartmentAsync(string userId)
		{
			// Get all department memberships for this user (non-deleted)
			var allMembers = await _departmentsService.GetAllDepartmentsForUserAsync(userId);
			if (allMembers == null || allMembers.Count == 0)
				return null;

			var activeMemberships = allMembers.Where(m => !m.IsDeleted).ToList();
			if (activeMemberships.Count == 0)
				return null;

			// Prefer the member marked IsActive, then IsDefault, then first
			var activeMember = activeMemberships
				.OrderByDescending(m => m.IsActive)
				.ThenByDescending(m => m.IsDefault)
				.FirstOrDefault();

			if (activeMember != null)
				return await _departmentsService.GetDepartmentByIdAsync(activeMember.DepartmentId);

			return null;
		}
	}
}
