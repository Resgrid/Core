using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Config;
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
		private readonly ISecurityPinService _securityPinService;

		private const int MaxPinAttempts = 3;

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
			IChatbotRateLimiter rateLimiter,
			ISecurityPinService securityPinService)
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
			_securityPinService = securityPinService;
		}

		public async Task<ChatbotResponse> ProcessMessageAsync(ChatbotMessage message)
		{
			if (message == null || string.IsNullOrWhiteSpace(message.Text))
				return new ChatbotResponse { Text = string.Empty, Processed = false };

			try
			{
				// 1. Identify user from the platform-specific identifier (already-linked only).
				var identity = await ResolveUserIdentityAsync(message);

				// 1b. SMS identities are only trusted while the linked profile's mobile number is
				// verified AND still matches the sending number. Links created before verification was
				// required, or made stale by a number change, must not authenticate the sender — drop
				// them and fall through to the fresh phone-match below (the number may now belong to a
				// different, verified profile).
				if (identity != null && IsSmsPlatform(message.Platform))
				{
					var linkedProfile = await _userProfileService.GetProfileByUserIdAsync(identity.UserId);
					if (linkedProfile == null
						|| linkedProfile.MobileNumberVerified != true
						|| !PhoneNumbersMatch(linkedProfile.GetPhoneNumber(), message.From))
					{
						// Only physically remove SMS links. The phone fallback can also surface a
						// cross-platform identity (e.g. WhatsApp ids are phone numbers); those are
						// merely not trusted for this SMS message, not deleted.
						if (IsSmsPlatform(identity.Platform))
							await _userIdentityService.UnlinkUserAsync(identity.Id);
						identity = null;
					}
				}

				// 1c. SMS: a phone number that matches a Resgrid profile but isn't linked yet. Only a
				// VERIFIED mobile number may authenticate a sender; when it is verified the identity link
				// is created silently (it's an internal optimization, not something the user is asked about).
				if (identity == null && IsSmsPlatform(message.Platform))
				{
					var cleanPhone = message.From?.Replace("+", "").Trim();
					if (!string.IsNullOrWhiteSpace(cleanPhone))
					{
						var profile = await _userProfileService.GetProfileByMobileNumberAsync(cleanPhone);
						if (profile != null)
						{
							if (profile.MobileNumberVerified == true)
							{
								identity = await _userIdentityService.LinkUserAsync(
									profile.UserId, message.Platform, message.From, profile.FullName.AsFirstNameLastName, "phone_match_verified");
							}
							else
							{
								return new ChatbotResponse
								{
									Text = "This mobile number matches a Resgrid profile but hasn't been verified yet. Please verify your mobile number on your Resgrid profile page, then text again.",
									Processed = true
								};
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

				// Persist the touch: LinkUserAsync upserts the existing identity (bumping LastUsedAt
				// and preserving LinkingMethod/PlatformUserName when passed their current values).
				await _userIdentityService.LinkUserAsync(
					identity.UserId, identity.Platform, identity.PlatformUserId, identity.PlatformUserName, identity.LinkingMethod);

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

				// 3b. Check the ACTIVE department's plan supports chatbot features. When it doesn't but the
				// user belongs to other departments that do, don't hard-block: they must still be able to
				// list departments and SWITCH their active department (restricted mode, enforced after
				// intent classification below). Only when there is no supported alternative is this a dead end.
				var isAuthorized = await _limitsService.CanDepartmentProvisionNumberAsync(department.DepartmentId);
				List<Model.DepartmentMember> switchableDepartments = null;
				if (!isAuthorized)
				{
					// A switch target must be one the chatbot can actually serve on this platform (plan
					// supports SMS AND chatbot enabled per department config) — the same filter
					// DepartmentActionHandler applies, so the numbered options shown below map to the
					// memberships its switch handler resolves. Plan-only filtering could strand the user in
					// a department whose chatbot config blocks everything, with no in-band way back.
					var smsSupported = await _departmentsService.GetSmsSupportedMembershipsForUserAsync(identity.UserId)
						?? new List<Model.DepartmentMember>();

					switchableDepartments = new List<Model.DepartmentMember>();
					foreach (var membership in smsSupported)
					{
						if (await _departmentConfigService.IsChatbotUsableForDepartmentAsync(membership.DepartmentId, message.Platform))
							switchableDepartments.Add(membership);
					}

					if (switchableDepartments.Count == 0)
					{
						return await _templateRenderer.RenderResponseAsync("error",
							new Services.ErrorModel { Message = "Your department's plan does not support chatbot features. Please upgrade your plan." },
							message.Platform, new ChatbotIntent { Type = ChatbotIntentType.Unknown });
					}
				}

				// 3c. Per-department configuration gates (when a config row exists; otherwise the
				// system defaults apply and the chatbot stays enabled for backward compatibility).
				// Skipped in restricted (switch-only) mode: the unsupported department's config must not
				// block the user from switching OUT of it.
				var deptConfig = await _departmentConfigService.GetConfigAsync(department.DepartmentId);
				if (deptConfig != null && isAuthorized)
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

				// Restricted (switch-only) mode: the active department's plan doesn't support the chatbot
				// but the user belongs to other departments that do. Only department list/switch/active
				// intents are honored — so the user can move to a supported department — and anything else
				// gets the switch options. Confirmation/PIN/continuation states are intentionally skipped:
				// no destructive intent can be pending for a department that can't dispatch them.
				if (!isAuthorized)
				{
					var restrictedIntent = await _intentRouter.ClassifyIntentAsync(message, session);

					// STOP (telecom opt-out) always works, even in restricted mode.
					if (restrictedIntent.Type == ChatbotIntentType.Stop && IsSmsPlatform(message.Platform))
					{
						await _userProfileService.DisableTextMessagesForUserAsync(identity.UserId);
						return new ChatbotResponse
						{
							Text = "Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.",
							Processed = true,
							Intent = restrictedIntent
						};
					}

					if (restrictedIntent.Type == ChatbotIntentType.ListDepartments
						|| restrictedIntent.Type == ChatbotIntentType.SwitchDepartment
						|| restrictedIntent.Type == ChatbotIntentType.GetActiveDepartment)
					{
						var restrictedHandler = _actionHandlers.FirstOrDefault(h => h.CanHandle(restrictedIntent.Type));
						if (restrictedHandler != null)
						{
							var restrictedResponse = await restrictedHandler.HandleAsync(message, restrictedIntent, session);
							restrictedResponse.Intent = restrictedIntent;
							await _sessionManager.SaveSessionAsync(session);
							return restrictedResponse;
						}
					}

					var optionsBuilder = new System.Text.StringBuilder();
					optionsBuilder.AppendLine("Your active department's plan does not support chatbot features, but you belong to other departments that do:");
					for (int i = 0; i < switchableDepartments.Count; i++)
					{
						var switchableDept = await _departmentsService.GetDepartmentByIdAsync(switchableDepartments[i].DepartmentId);
						optionsBuilder.AppendLine($"{i + 1}: {switchableDept?.Name ?? ("Department " + switchableDepartments[i].DepartmentId)}");
					}
					optionsBuilder.Append("Reply SWITCH <number or name> to change your active department.");

					return new ChatbotResponse { Text = optionsBuilder.ToString(), Processed = true };
				}

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
						// Step-up auth: when the department forces security-PIN usage (or the user opted
						// in), a confirmed destructive action additionally requires the user's 4-digit PIN
						// before it executes.
						if (await _securityPinService.IsPinRequiredAsync(identity.UserId, department.DepartmentId))
						{
							// Provision a missing PIN BEFORE persisting the awaiting-PIN state: if
							// provisioning fails (no profile, or a thrown error caught by the outer
							// handler) the session stays parked in AwaitingConfirmation instead of
							// being stranded awaiting a PIN that doesn't exist.
							bool pinGenerated = false;
							if (!await _securityPinService.HasPinAsync(identity.UserId))
							{
								var provisioned = await _securityPinService.EnsurePinAsync(identity.UserId, department.DepartmentId);
								if (provisioned == null || string.IsNullOrWhiteSpace(provisioned.SecurityPin))
								{
									return new ChatbotResponse
									{
										Text = "This action requires a security PIN, but one couldn't be set up for your account. Please set a PIN on your Resgrid profile page, then reply YES again.",
										Processed = true
									};
								}
								pinGenerated = true;
							}

							session.State = ChatbotDialogState.AwaitingPin;
							session.Context["__pinAttempts"] = "0";
							await _sessionManager.SaveSessionAsync(session);

							if (pinGenerated)
							{
								// Department forces PINs but this user didn't have one yet — one was
								// generated so they can look it up on their profile page and continue.
								return new ChatbotResponse
								{
									Text = "This action requires your security PIN. A PIN has been generated for you — view or change it on your Resgrid profile page, then reply with the 4-digit PIN to continue, or NO to cancel.",
									Processed = true
								};
							}

							return new ChatbotResponse { Text = "For security, reply with your 4-digit security PIN to complete this action, or NO to cancel.", Processed = true };
						}

						return await DispatchConfirmedIntentAsync(message, session);
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

				// 5b. PIN step-up for an already-confirmed destructive action: the user replied YES and a
				// security PIN is required, so the pending intent only executes on a correct PIN.
				if (session.State == ChatbotDialogState.AwaitingPin && session.PendingIntent.HasValue)
				{
					var pinReply = message.Text?.Trim();

					if (IsNegative(pinReply))
					{
						session.Reset();
						await _sessionManager.SaveSessionAsync(session);
						return new ChatbotResponse { Text = "Cancelled.", Processed = true };
					}

					if (!SecurityPinUtility.IsValidFormat(pinReply))
						return new ChatbotResponse { Text = "Please reply with your 4-digit security PIN, or NO to cancel.", Processed = true };

					if (await _securityPinService.ValidatePinAsync(identity.UserId, pinReply))
					{
						session.Context.Remove("__pinAttempts");
						return await DispatchConfirmedIntentAsync(message, session);
					}
					else
					{
						session.Context.TryGetValue("__pinAttempts", out var attemptsRaw);
						int.TryParse(attemptsRaw, out var attempts);
						attempts++;

						if (attempts >= MaxPinAttempts)
						{
							session.Reset();
							await _sessionManager.SaveSessionAsync(session);
							return new ChatbotResponse { Text = "Too many incorrect PIN attempts. The action has been cancelled.", Processed = true };
						}

						session.Context["__pinAttempts"] = attempts.ToString();
						await _sessionManager.SaveSessionAsync(session);
						return new ChatbotResponse { Text = "Incorrect PIN. Reply with your 4-digit security PIN, or NO to cancel.", Processed = true };
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

				// 7. STOP is the telecom opt-out keyword, never a session operation: on SMS it turns off
				// all outbound SMS notifications (calls, messages, notifications) on the user's profile —
				// same behavior as the legacy text-command path. Sessions are infrastructure; users don't
				// start or end them.
				if (intent.Type == ChatbotIntentType.Stop)
				{
					if (IsSmsPlatform(message.Platform))
					{
						await _userProfileService.DisableTextMessagesForUserAsync(identity.UserId);
						return new ChatbotResponse
						{
							Text = "Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.",
							Processed = true,
							Intent = intent
						};
					}

					return new ChatbotResponse
					{
						Text = "To manage your notification settings, update your profile in Resgrid.",
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

		private static bool IsNegative(string text)
		{
			var t = text?.Trim().ToLowerInvariant();
			return t == "no" || t == "n" || t == "cancel" || t == "stop";
		}

		/// <summary>
		/// Executes the pending (already confirmed, and PIN-verified when required) destructive intent.
		/// The owning handler is located BEFORE any session state is mutated: if none can handle the
		/// pending intent the session stays parked and an explicit error is returned rather than
		/// silently dropping the confirmation.
		/// </summary>
		private async Task<ChatbotResponse> DispatchConfirmedIntentAsync(ChatbotMessage message, ChatbotSession session)
		{
			var pendingType = session.PendingIntent.Value;

			var confirmHandler = _actionHandlers.FirstOrDefault(h => h.CanHandle(pendingType));
			if (confirmHandler == null)
			{
				return new ChatbotResponse
				{
					Text = "Unable to complete confirmation — please try again or contact support.",
					Processed = false
				};
			}

			var confirmedIntent = new ChatbotIntent
			{
				Type = pendingType,
				Parameters = new Dictionary<string, string>(session.Context) { ["__confirmed"] = "true" }
			};

			var confirmResponse = await confirmHandler.HandleAsync(message, confirmedIntent, session);
			confirmResponse.Intent = confirmedIntent;
			session.Context.Clear();
			session.State = ChatbotDialogState.Idle;
			session.PendingIntent = null;
			await _sessionManager.SaveSessionAsync(session);
			return confirmResponse;
		}

		/// <summary>
		/// Compares a profile phone number against a sender number, ignoring formatting and tolerating
		/// a leading US country code on either side (mirrors GetProfileByMobileNumberAsync).
		/// </summary>
		private static bool PhoneNumbersMatch(string profileNumber, string senderNumber)
		{
			var a = NormalizePhone(profileNumber);
			var b = NormalizePhone(senderNumber);

			if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
				return false;

			return a == b
				|| (a.Length == 11 && a[0] == '1' && a.Substring(1) == b)
				|| (b.Length == 11 && b[0] == '1' && b.Substring(1) == a);
		}

		private static string NormalizePhone(string number)
			=> number?.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Trim();

		private async Task<Model.Department> ResolveActiveDepartmentAsync(string userId)
		{
			// Shared resolution: the user's active (then default, then first) membership, preferring one whose
			// plan supports SMS. The TwilioController uses the same resolver for the master-number sender path
			// so the flag evaluation and the chatbot agree on which department the sender operates in.
			return await _departmentsService.GetActiveSmsDepartmentForUserAsync(userId);
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
