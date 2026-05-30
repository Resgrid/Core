using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Config;
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
		private readonly IAuthorizationService _authorizationService;
		private readonly IChatbotDepartmentConfigService _departmentConfigService;
		private readonly IChatbotRateLimiter _rateLimiter;

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
			ILimitsService limitsService,
			IAuthorizationService authorizationService,
			IChatbotDepartmentConfigService departmentConfigService,
			IChatbotRateLimiter rateLimiter)
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
			_authorizationService = authorizationService;
			_departmentConfigService = departmentConfigService;
			_rateLimiter = rateLimiter;
		}

		public async Task<ChatbotResponse> ProcessMessageAsync(ChatbotMessage message)
		{
			if (message == null || string.IsNullOrWhiteSpace(message.Text))
				return new ChatbotResponse { Text = string.Empty, Processed = false };

			try
			{
				// 1. Identify user from the platform-specific identifier (already-linked only).
				var identity = await ResolveUserIdentityAsync(message);

				// 1b. SMS: a phone number that matches a Resgrid profile but isn't linked yet. Depending
				// on the department's RequireLinkingConfirmation setting, either auto-link or run a
				// one-time "Reply YES to link" confirmation before trusting the number.
				if (identity == null && IsSmsPlatform(message.Platform))
				{
					var cleanPhone = message.From?.Replace("+", "").Trim();
					if (!string.IsNullOrWhiteSpace(cleanPhone))
					{
						var profile = await _userProfileService.GetProfileByMobileNumberAsync(cleanPhone);
						if (profile != null)
						{
							var candidateDepartment = await ResolveActiveDepartmentAsync(profile.UserId);
							var requireConfirmation = candidateDepartment == null
								|| ((await _departmentConfigService.GetConfigAsync(candidateDepartment.DepartmentId))?.RequireLinkingConfirmation ?? true);

							if (!requireConfirmation)
							{
								identity = await _userIdentityService.LinkUserAsync(
									profile.UserId, message.Platform, message.From, profile.FullName.AsFirstNameLastName, "phone_match");
							}
							else
							{
								// Returns the prompt / acknowledgement; links the number on a YES reply.
								return await HandlePhoneLinkConfirmationAsync(message, profile, candidateDepartment);
							}
						}
					}
				}

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

				// 3. Central authorization gate (layer 1): the user must be a valid, active member
				// within the resolved department's limits before any intent is dispatched. Per-handler
				// permission checks (layer 2) enforce action-specific authorization.
				if (!await _authorizationService.IsUserValidWithinLimitsAsync(identity.UserId, department.DepartmentId))
				{
					return await _templateRenderer.RenderResponseAsync("error",
						new Services.ErrorModel { Message = "Your account isn't active in this department. Please contact your administrator." },
						message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
				}

				// 3b. Check department plan supports chatbot features
				var isAuthorized = await _limitsService.CanDepartmentProvisionNumberAsync(department.DepartmentId);
				if (!isAuthorized)
				{
					return await _templateRenderer.RenderResponseAsync("error",
						new Services.ErrorModel { Message = "Your department's plan does not support chatbot features. Please upgrade your plan." },
						message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
				}

				// 3c. Per-department configuration gates (when a config row exists; otherwise the
				// system defaults apply and the chatbot stays enabled for backward compatibility).
				var deptConfig = await _departmentConfigService.GetConfigAsync(department.DepartmentId);
				if (deptConfig != null)
				{
					if (!deptConfig.IsEnabled)
					{
						return await _templateRenderer.RenderResponseAsync("error",
							new Services.ErrorModel { Message = "The chatbot is not enabled for your department. Please contact your administrator." },
							message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
					}

					if (!IsPlatformAllowed(deptConfig.AllowedPlatforms, message.Platform))
					{
						return await _templateRenderer.RenderResponseAsync("error",
							new Services.ErrorModel { Message = "This channel isn't enabled for your department's chatbot." },
							message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
					}
				}

				// 3d. Rate limiting using the department's limits (or system defaults). Emergency/distress
				// messages (mayday, etc.) get a far more generous allowance so a real human in distress is
				// never throttled at any plausible texting speed, but they are NOT fully exempt: a fully-open
				// bypass lets a compromised/abusive account flood the pipeline (each message can trigger a
				// paid cloud NLU call) just by including an emergency keyword. A configured limit of 0 means
				// "unlimited", so emergencies stay unlimited in that case too.
				var perUserLimit = deptConfig?.MessagesPerUserPerMinute ?? ChatbotConfig.MessagesPerUserPerMinute;
				var perDeptLimit = deptConfig?.MessagesPerDepartmentPerMinute ?? ChatbotConfig.MessagesPerDepartmentPerMinute;
				if (IsEmergencyText(message.Text))
				{
					const int emergencyMultiplier = 10;
					perUserLimit = perUserLimit <= 0 ? 0 : perUserLimit * emergencyMultiplier;
					perDeptLimit = perDeptLimit <= 0 ? 0 : perDeptLimit * emergencyMultiplier;
				}

				if (!await _rateLimiter.TryAcquireAsync(identity.UserId, department.DepartmentId, perUserLimit, perDeptLimit))
				{
					return await _templateRenderer.RenderResponseAsync("error",
						new Services.ErrorModel { Message = "You're sending messages too quickly. Please wait a moment and try again." },
						message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
				}

				// 4. Get or create session
				var session = await _sessionManager.GetOrCreateSessionAsync(
					identity.UserId,
					department.DepartmentId,
					message.Platform,
					message.From);

				// Resolve the user's preferred language once per session so responses are localized
				// (UserProfile.Language → supported culture; unknown/none falls back to English).
				if (string.IsNullOrWhiteSpace(session.Culture))
				{
					var cultureProfile = await _userProfileService.GetProfileByUserIdAsync(identity.UserId);
					session.Culture = Localization.ChatbotResources.NormalizeCulture(cultureProfile?.Language);
				}

				// Add message to session history
				session.RecentMessages.Add(message);
				if (session.RecentMessages.Count > 20)
					session.RecentMessages.RemoveAt(0);

				// 5. Handle session state machine via ConversationEngine (Phase 2)

				// 5a. Confirmation of a destructive action (CloseCall, DispatchCall, SetUnitStatus, ...).
				// The owning handler parked the session in AwaitingConfirmation with PendingIntent set and
				// its parameters stashed in Context. On YES we re-dispatch to that handler with a
				// "__confirmed" marker so it actually executes (re-checking authz/ownership); on NO we
				// cancel. This is the real confirmation gate required by the security addendum §5.
				if (session.State == ChatbotDialogState.AwaitingConfirmation && session.PendingIntent.HasValue)
				{
					var reply = message.Text?.Trim().ToUpperInvariant();
					if (reply == "YES" || reply == "Y" || reply == "CONFIRM" || reply == "OK")
					{
						var pendingType = session.PendingIntent.Value;
						var confirmedIntent = new ChatbotIntent
						{
							Type = pendingType,
							Parameters = new Dictionary<string, string>(session.Context) { ["__confirmed"] = "true" }
						};
						session.State = ChatbotDialogState.Idle;
						session.PendingIntent = null;

						var confirmHandler = _actionHandlers.FirstOrDefault(h => h.CanHandle(pendingType));
						if (confirmHandler != null)
						{
							var confirmResponse = await confirmHandler.HandleAsync(message, confirmedIntent, session);
							confirmResponse.Intent = confirmedIntent;
							session.Context.Clear();
							await _sessionManager.SaveSessionAsync(session);
							return confirmResponse;
						}
					}
					else if (reply == "NO" || reply == "N" || reply == "CANCEL")
					{
						session.Reset();
						await _sessionManager.SaveSessionAsync(session);
						return new ChatbotResponse { Text = "Cancelled.", Processed = true };
					}
					else
					{
						return new ChatbotResponse { Text = "Please reply YES to confirm or NO to cancel.", Processed = true };
					}
				}

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
			// Platform-specific identity (already linked).
			var identity = await _userIdentityService.GetIdentityAsync(message.Platform, message.From);

			// Generic lookup for a number already linked to a Resgrid user (any platform). Note: this
			// does NOT auto-link new numbers — that is handled (with optional confirmation) in the ingress.
			if (identity == null)
			{
				var cleanPhone = message.From?.Replace("+", "").Trim();
				if (!string.IsNullOrWhiteSpace(cleanPhone))
					identity = await _userIdentityService.GetIdentityByPhoneAsync(cleanPhone);
			}

			return identity;
		}

		private static bool IsSmsPlatform(ChatbotPlatform platform)
			=> platform == ChatbotPlatform.SmsTwilio || platform == ChatbotPlatform.SmsSignalWire;

		private static bool IsEmergencyText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;

			var t = text.ToLowerInvariant();
			return t.Contains("mayday") || t.Contains("emergency") || t.Contains("officer down") || t.Contains("firefighter down");
		}

		private static bool IsAffirmative(string text)
		{
			var t = text?.Trim().ToLowerInvariant();
			return t == "yes" || t == "y" || t == "confirm";
		}

		private static bool IsNegative(string text)
		{
			var t = text?.Trim().ToLowerInvariant();
			return t == "no" || t == "n" || t == "cancel" || t == "stop";
		}

		/// <summary>
		/// One-time confirmation before linking an unrecognized SMS number to the matching Resgrid
		/// account. Prompts on first contact, links on YES, cancels on NO.
		/// </summary>
		private async Task<ChatbotResponse> HandlePhoneLinkConfirmationAsync(ChatbotMessage message, Model.UserProfile profile, Model.Department department)
		{
			// A matched profile may have no active department membership, in which case the caller
			// passes a null department. The phone-link confirmation only tracks trust of the number
			// (linking doesn't depend on a department), so fall back to department id 0 for the
			// temporary session rather than dereferencing a null department.
			var session = await _sessionManager.GetOrCreateSessionAsync(profile.UserId, department?.DepartmentId ?? 0, message.Platform, message.From);

			var awaitingConfirmation = session.State == ChatbotDialogState.AwaitingConfirmation
				&& session.Context != null
				&& session.Context.TryGetValue("pendingAction", out var pendingAction)
				&& pendingAction == "confirm_phone_link";

			if (awaitingConfirmation)
			{
				if (IsAffirmative(message.Text))
				{
					await _userIdentityService.LinkUserAsync(
						profile.UserId, message.Platform, message.From, profile.FullName.AsFirstNameLastName, "phone_match_confirmed");
					session.State = ChatbotDialogState.Idle;
					session.Context.Remove("pendingAction");
					await _sessionManager.SaveSessionAsync(session);
					return new ChatbotResponse { Text = "Your number is now linked to your Resgrid account. Text HELP to see what you can do.", Processed = true };
				}

				if (IsNegative(message.Text))
				{
					await _sessionManager.EndSessionAsync(session.SessionId);
					return new ChatbotResponse { Text = "No problem — I won't link this number. Reply LINK anytime to connect your account.", Processed = true };
				}

				return new ChatbotResponse { Text = "Please reply YES to link this number to your Resgrid account, or NO to cancel.", Processed = false };
			}

			// First contact for this number: ask for confirmation before linking.
			session.State = ChatbotDialogState.AwaitingConfirmation;
			session.Context["pendingAction"] = "confirm_phone_link";
			await _sessionManager.SaveSessionAsync(session);

			return new ChatbotResponse
			{
				Text = $"We found a Resgrid account for {profile.FirstName}. Reply YES to link this number, or NO to cancel.",
				Processed = true
			};
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

		private static bool IsPlatformAllowed(string allowedPlatforms, ChatbotPlatform platform)
		{
			if (string.IsNullOrWhiteSpace(allowedPlatforms) || allowedPlatforms.Trim() == "*")
				return true;

			var platformName = platform.ToString();
			foreach (var entry in allowedPlatforms.Split(','))
			{
				if (string.Equals(entry.Trim(), platformName, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}
	}
}
