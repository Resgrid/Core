using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Models;
using Resgrid.Web.Services.Twilio;
using Twilio.AspNet.Common;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Web.Services.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[Produces("application/xml")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class TwilioController : ControllerBase
	{
		#region Private Readonly Properties and Constructors

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly INumbersService _numbersService;
		private readonly ILimitsService _limitsService;
		private readonly ICallsService _callsService;
		private readonly IQueueService _queueService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly ITextCommandService _textCommandService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly ICommunicationService _communicationService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICustomStateService _customStateService;
		private readonly IUnitsService _unitsService;
		private readonly IUsersService _usersService;
		private readonly ICalendarService _calendarService;
		private readonly ICommunicationTestService _communicationTestService;
		private readonly IEncryptionService _encryptionService;
	private readonly ITwilioVoiceResponseService _twilioVoiceResponseService;
	private readonly IFeatureToggleService _featureToggleService;
	private readonly ITextDepartmentSwitchService _textDepartmentSwitchService;
	private readonly IDispatchRecommendationService _dispatchRecommendationService;
	private readonly IDispatchScopeService _dispatchScopeService;
	private readonly Model.Services.IAuthorizationService _authorizationService;
	private readonly IAdpReleaseService _adpRelease;
	private readonly IProtectedProjectionService _adpProjection;
	private readonly IDepartmentPhoneNumbersService _departmentPhoneNumbersService;
	private readonly IIntakeService _intakeService;
	private readonly IVoiceIntakeService _voiceIntakeService;
	private readonly ITextMessageProvider _textMessageProvider;
	private readonly IIntakeConversationService _intakeConversations;
	private readonly IIntakeConversationOperations _intakeConversationOperations;
	private readonly IIntakeScreeningService _intakeScreening;
	private readonly IDispatcherInboundTextHandler _dispatcherInboundTextHandler;
	private readonly IDispatcherInboundVoiceHandler _dispatcherInboundVoiceHandler;

	public TwilioController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
		ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
		IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
		IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
		IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
		IUsersService usersService, ICalendarService calendarService, ICommunicationTestService communicationTestService,
		IEncryptionService encryptionService, ITwilioVoiceResponseService twilioVoiceResponseService,
		IFeatureToggleService featureToggleService, ITextDepartmentSwitchService textDepartmentSwitchService,
		IDispatchRecommendationService dispatchRecommendationService, IDispatchScopeService dispatchScopeService,
		Model.Services.IAuthorizationService authorizationService, IAdpReleaseService adpRelease, IProtectedProjectionService adpProjection,
		IDepartmentPhoneNumbersService departmentPhoneNumbersService, IIntakeService intakeService, IVoiceIntakeService voiceIntakeService,
		ITextMessageProvider textMessageProvider, IIntakeConversationService intakeConversations, IIntakeConversationOperations intakeConversationOperations,
		IIntakeScreeningService intakeScreening,
		IDispatcherInboundTextHandler dispatcherInboundTextHandler, IDispatcherInboundVoiceHandler dispatcherInboundVoiceHandler)
	{
		_intakeScreening = intakeScreening;
		_intakeConversations = intakeConversations;
		_intakeConversationOperations = intakeConversationOperations;
		_departmentPhoneNumbersService = departmentPhoneNumbersService;
		_intakeService = intakeService;
		_voiceIntakeService = voiceIntakeService;
		_textMessageProvider = textMessageProvider;
		_dispatcherInboundTextHandler = dispatcherInboundTextHandler;
		_dispatcherInboundVoiceHandler = dispatcherInboundVoiceHandler;
		_dispatchScopeService = dispatchScopeService;
		_authorizationService = authorizationService;
		_adpRelease = adpRelease;
		_adpProjection = adpProjection;
		_departmentSettingsService = departmentSettingsService;
		_numbersService = numbersService;
		_limitsService = limitsService;
		_callsService = callsService;
		_queueService = queueService;
		_departmentsService = departmentsService;
		_userProfileService = userProfileService;
		_textCommandService = textCommandService;
		_actionLogsService = actionLogsService;
		_userStateService = userStateService;
		_communicationService = communicationService;
		_geoLocationProvider = geoLocationProvider;
		_departmentGroupsService = departmentGroupsService;
		_customStateService = customStateService;
		_unitsService = unitsService;
		_usersService = usersService;
		_calendarService = calendarService;
		_communicationTestService = communicationTestService;
		_encryptionService = encryptionService;
		_twilioVoiceResponseService = twilioVoiceResponseService;
		_featureToggleService = featureToggleService;
		_textDepartmentSwitchService = textDepartmentSwitchService;
		_dispatchRecommendationService = dispatchRecommendationService;
	}
		#endregion Private Readonly Properties and Constructors

		private const int MAX_DISPATCH_RETRY = 3;

		// How many extra times an outbound dispatch call replays the dispatch and menu when the
		// callee presses nothing. Someone answering while the app's alert tone is still sounding
		// misses the first read, so the call repeats instead of hanging up after one menu
		// (GitHub #267). Replays reuse the cached dispatch audio.
		private const int MAX_DISPATCH_REPLAY = 2;

		// Retry budget for the inbound-menu dynamic listings (active calls, statuses,
		// calendar). Same please-wait-and-redirect pattern as dispatch playback.
		private const int MAX_LISTING_RETRY = 3;

		// How long a webhook waits for listing audio to come out of the TTS cache
		// before redirecting. Generation keeps running server-side after the timeout,
		// so a redirect re-entry finds the audio cached.
		private static readonly TimeSpan ListingReadinessTimeout = TimeSpan.FromSeconds(3);

		// Shared time budget for ALL TTS prompt playback within a single webhook request.
		// A TTS service that is down fails fast and degrades to <Say> inside
		// TwilioVoiceResponseService, but a HUNG service eats the full RestClient
		// timeout per attempt (with retries, ~16s for one prompt) — enough to blow
		// Twilio's 15-second webhook limit before any fallback fires. The budget
		// starts on the first prompt append; once it expires every remaining prompt
		// in the request falls back to Twilio's native <Say> voice immediately.
		private static readonly TimeSpan TtsPromptBudget = TimeSpan.FromSeconds(6);
		private CancellationTokenSource _ttsBudgetCts;

		// Run card enrichment on the text-to-call path runs bulk status/location queries and,
		// when routed ETA is enabled, external routing calls per requirement — unbounded, that
		// can outlast the same 15-second webhook limit and take the dispatch down with it,
		// since the broadcast is enqueued after it. Recommendations are an enhancement to the
		// dispatch, so they get a slice of the budget and are dropped if they overrun.
		private static readonly TimeSpan RunCardEnrichmentBudget = TimeSpan.FromSeconds(5);

		private CancellationToken GetTtsPromptBudgetToken()
		{
			if (_ttsBudgetCts == null)
			{
				_ttsBudgetCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext?.RequestAborted ?? CancellationToken.None);
				_ttsBudgetCts.CancelAfter(TtsPromptBudget);
				HttpContext?.Response.RegisterForDispose(_ttsBudgetCts);
			}

			return _ttsBudgetCts.Token;
		}

		// True when the TTS budget expired on its own (the caller hasn't hung up),
		// which is the only cancellation we convert into a <Say> fallback.
		private bool TtsBudgetExpired =>
			_ttsBudgetCts != null && _ttsBudgetCts.IsCancellationRequested
			&& !(HttpContext?.RequestAborted.IsCancellationRequested ?? false);

		[HttpGet("IncomingMessage")]
		[HttpPost("IncomingMessage")]
		[Produces("application/xml")]
		// Twilio signature validation: without it anyone who learns a member's mobile number can
		// forge inbound SMS and run text commands (respond, staffing, text-to-call) as that member.
		[ValidateRequest]
		public async Task<ActionResult> IncomingMessage([FromQuery] TwilioMessage request)
		{
			if (Request.HasFormContentType)
			{
				var form = await Request.ReadFormAsync();
				request = new TwilioMessage { To = form["To"], From = form["From"], Body = form["Body"], MessageSid = form["MessageSid"] };
			}
			if (request == null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.Body))
				return BadRequest();
			// WhatsApp must use its separately authenticated native endpoint and explicit account link.
			if (request.From.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
				return BadRequest("Configure the ChatbotPlatforms/WhatsApp webhook for this sender.");

			var response = new MessagingResponse();

			var pinCommand = request.Body.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			// Only something shaped like a PIN reply (a challenge identifier, or a short reply carrying a PIN anywhere
			// after OPEN) is answered here. Ordinary text such as "OPEN BURN AT 123 MAIN ST" must still reach
			// text-to-call, text commands and the chatbot. A malformed reply that carries a PIN, such as
			// "OPEN 123456 please", is still answered here, so the PIN never lands in the inbound message log.
			if (pinCommand.Length >= 2 && pinCommand[0].Equals("OPEN", StringComparison.OrdinalIgnoreCase) &&
				(System.Text.RegularExpressions.Regex.IsMatch(pinCommand[1], "^[A-Fa-f0-9]{24}$") ||
				 pinCommand.Length <= 4 && pinCommand.Skip(1).Any(token => System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9]{6,12}$"))))
			{
				var text = Microsoft.AspNetCore.Http.HttpMethods.IsPost(Request.Method) && Request.HasFormContentType && pinCommand.Length == 3
					? await _adpRelease.ReleaseAsync(pinCommand[1].ToUpperInvariant(), request.From, pinCommand[2], ProtectedDataEgressChannel.Sms) : null;
				var profile = await _userProfileService.GetProfileByMobileNumberAsync(request.From.Replace("+", ""));
				response.Message(text ?? Resgrid.Localization.Areas.User.SystemMessages.SystemMessagesResources.Get("AdpPinDenied", profile?.Language));
				return Content(response.ToString(), "application/xml");
			}

			var textMessage = new TextMessage();
			textMessage.To = request.To.Replace("+", "");
			textMessage.Msisdn = request.From.Replace("+", "");
			textMessage.MessageId = request.MessageSid;
			textMessage.Timestamp = DateTime.UtcNow.ToLongDateString();
			textMessage.Data = request.Body;
			textMessage.Text = request.Body;

			var messageEvent = new InboundMessageEvent();
			messageEvent.MessageType = (int)InboundMessageTypes.TextMessage;
			messageEvent.RecievedOn = DateTime.UtcNow;
			messageEvent.Type = typeof(InboundMessageEvent).FullName;
			messageEvent.Data = JsonConvert.SerializeObject(textMessage);
			messageEvent.Processed = false;
			messageEvent.CustomerId = "";

			// Check for Communication Test response (CT- prefix)
			if (!string.IsNullOrWhiteSpace(textMessage.Text) && textMessage.Text.Trim().StartsWith("CT-", StringComparison.OrdinalIgnoreCase))
			{
				var runCode = textMessage.Text.Trim().Split(' ')[0].ToUpperInvariant();
				await _communicationTestService.RecordSmsResponseAsync(runCode, textMessage.Msisdn);
				messageEvent.Processed = true;

				response.Message("Resgrid received your communication test response. Thank you.");

				await _numbersService.SaveInboundMessageEventAsync(messageEvent);
				return new ContentResult
				{
					Content = response.ToString(),
					ContentType = "application/xml",
					StatusCode = 200
				};
			}

			// A department's dedicated number (call-intake plan D5): every text to it is intake, a pending call, whoever sends it,
			// before any profile routing. The TwiML stays empty: the number is inbound-only, so a reply goes from the master number.
			// Intake that failed before anything was saved answers 503: Twilio records the failure (debugger, fallback URL, a configured
			// retry), and a redelivery of the same MessageSid takes the failed claim again rather than being a duplicate.
			var dedicated = await TryHandleDedicatedNumberTextAsync(request);
			if (dedicated != DedicatedNumberTexts.NotDedicated)
				return new ContentResult
				{
					Content = response.ToString(),
					ContentType = "application/xml",
					StatusCode = dedicated == DedicatedNumberTexts.Failed
						? Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable
						: Microsoft.AspNetCore.Http.StatusCodes.Status200OK
				};

			// Intake SMS conversations (plan §8 D15): a member of the public's reply to the master number continues their open thread with
			// a department. This check runs after the PIN / CT- replies and the dedicated-number branch, and BEFORE any other handling of
			// the sender (profile routing, member commands, legacy text-to-call, chatbot). The dispatcher desk's D9 hook (legacy
			// TextToCallNumber numbers) goes after this check, never before it. The text stays out of the inbound message log (the
			// thread's call notes hold it). STOP closes the thread and opts out, then the master number's own STOP handling below runs too.
			var conversationReply = await TryHandleConversationReplyAsync(request);
			if (conversationReply == ConversationReplies.Handled)
				return new ContentResult { Content = response.ToString(), ContentType = "application/xml", StatusCode = 200 };

			// The text is the open thread's but did not become durable (its row, its note on the call, or a STOP's close failed): a retryable
			// 503 (a configured Twilio retry, the debugger, a fallback URL) instead of an acknowledgement, and it never reaches member commands.
			// The redelivery of the same MessageSid finishes what this one could not.
			if (conversationReply == ConversationReplies.Retry)
				return new ContentResult
				{
					Content = response.ToString(),
					ContentType = "application/xml",
					StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable
				};

			try
			{
				// Going-forward model: one master Resgrid number handles all inbound SMS, so the department is
				// identified from the SENDER's profile → their active SMS-capable department (NOT the number
				// that was texted). This keeps the flag evaluation and the chatbot pipeline agreeing on which
				// department the sender operates in. Legacy clients that still text a per-department provisioned
				// inbound number fall back to resolving the department from that number.
				UserProfile userProfile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);

				// SECURITY: an unverified mobile number may be used for ROUTING (department resolution,
				// plan gate, feature flag) so the sender lands in their real department, but it must never
				// authorize ACTIONS — anyone can type someone else's number into a profile. Downstream
				// handlers block commands for unverified senders and prompt them to verify. The single
				// exception is STOP (handled below): opting out of messages must always work.
				bool senderNumberUnverified = userProfile != null && userProfile.MobileNumberVerified != true;
				if (senderNumberUnverified)
					Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} From={Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} matched a profile but the mobile number is not verified; using profile for routing only, actions blocked until verified.");

				// STOP closed a member of the public's intake conversation above. The master number's own STOP handling is a member's
				// (turn their texts off); for anyone else what follows is legacy text-to-call and the chatbot, which must not answer
				// someone who just opted out (found live 2026-11-20: the chatbot replied with an error message). Twilio's carrier-level
				// opt-out sends the STOP confirmation. That holds for any non-member's STOP to the master number, not only one that closed a
				// thread just now: a redelivered STOP (the first one closed the thread, its 200 was lost) finds no open thread any more.
				var nonMemberStop = userProfile == null && IsMasterNumber(textMessage.To) &&
					_textCommandService.DetermineType(textMessage.Text).Type == TextCommandTypes.Stop;
				if ((conversationReply == ConversationReplies.OptedOut || nonMemberStop) && userProfile == null)
				{
					Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} STOP from a non-member closed their intake conversation; nothing else runs.");
					messageEvent.Processed = true;

					// The enclosing finally saves messageEvent, as for a member's STOP below.
					return new ContentResult { Content = response.ToString(), ContentType = "application/xml", StatusCode = 200 };
				}

				int? departmentId = null;
				if (userProfile != null)
				{
					// An identified user (verified or not) always operates in their ACTIVE department. The
					// texted number must NOT override this — the TextToCallNumber mapping is a legacy path
					// that can resolve a department the user isn't even a member of.
					var department = await _departmentsService.GetActiveSmsDepartmentForUserAsync(userProfile.UserId);
					if (department != null)
						departmentId = department.DepartmentId;
				}
				else
				{
					// Legacy fallback for senders with no usable profile only (dispatch centers sending
					// text-to-call imports, unknown numbers): the provisioned inbound number identifies the
					// department directly.
					departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(textMessage.To);
				}

				// Carry the resolved department onto the inbound message event so chatbot-routed events
				// retain the same department context the text-command path records.
				if (departmentId.HasValue)
					messageEvent.CustomerId = departmentId.Value.ToString();

				// Dispatcher desk (plan D9): no profile, to a department number whose desk takes texts (or a reply to a desk conversation).
				if (userProfile == null && await _dispatcherInboundTextHandler.HandleAsync(departmentId,
					request.From, request.To, request.Body, request.MessageSid, HttpContext?.RequestAborted ?? CancellationToken.None))
				{
					messageEvent.Processed = true;
					return Content(response.ToString(), "application/xml");
				}

				// Diagnostic: did we resolve a department for this inbound text? If not, no reply is sent.
				Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} To={textMessage.To} From={Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} resolved DepartmentId={(departmentId.HasValue ? departmentId.Value.ToString() : "none")}");

				// STOP from an unverified sender: honor the opt-out immediately — turning off messages must
				// always work, regardless of verification, plan or feature-flag state.
				if (senderNumberUnverified && _textCommandService.DetermineType(textMessage.Text).Type == TextCommandTypes.Stop)
				{
					Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} unverified sender texted STOP; disabling SMS messaging on the matched profile.");
					await _userProfileService.DisableTextMessagesForUserAsync(userProfile.UserId);
					messageEvent.Processed = true;
					response.Message("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");

					// The enclosing finally saves messageEvent and the shared tail returns the TwiML.
					return new ContentResult
					{
						Content = response.ToString(),
						ContentType = "application/xml",
						StatusCode = 200
					};
				}

				// Feature-flagged rollout: the chatbot ingress is the new path. When the flag is off
				// (globally or for this department) fall back to the original text-command handling so
				// existing behavior is preserved.
				var chatbotEnabled = await _featureToggleService.IsEnabledAsync(
					FeatureFlagKeys.ChatbotTwilioTextIntegration, departmentId ?? 0, false);

				// Diagnostic: which path handled the message — chatbot ingress or legacy text commands?
				Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} DepartmentId={(departmentId.HasValue ? departmentId.Value.ToString() : "none")} ChatbotEnabled={chatbotEnabled} (path={(chatbotEnabled ? "chatbot" : "text-command")})");

				if (chatbotEnabled)
				{
					// Off-thread processing: hand the message to the bus and return an immediate empty
					// TwiML. A worker (QueuesProcessorTask -> ChatbotMessageLogic) runs the chatbot pipeline
					// and sends the reply via outbound SMS, so this webhook never blocks on the pipeline
					// (and can't hit Twilio's ~15s timeout / error 11200) regardless of Redis/LLM/DB latency.
					var chatbotQueueItem = new ChatbotMessageQueueItem
					{
						DepartmentId = departmentId ?? 0,
						To = textMessage.To,
						From = textMessage.Msisdn,
						Body = request.Body,
						MessageId = request.MessageSid,
						Platform = (int)ChatbotPlatform.SmsTwilio
					};

					await _queueService.EnqueueChatbotMessageAsync(chatbotQueueItem);
					messageEvent.Processed = true;

					Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} chatbot enqueued for async processing (DepartmentId={departmentId ?? 0})");
				}
				else
				{
					await ProcessTextCommandsAsync(textMessage, messageEvent, response, departmentId, userProfile, senderNumberUnverified);
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				response.Message("An error occurred processing your request. Please try again later.");
			}
			finally
			{
				await _numbersService.SaveInboundMessageEventAsync(messageEvent);
			}

			// Diagnostic: a body length of ~60 bytes is an empty <Response></Response> (no <Message>),
			// which means no reply will be delivered to the sender. Processed reflects whether a handler
			// claimed the message.
			var twiml = response.ToString();
			Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} responding with {twiml.Length} bytes, Processed={messageEvent.Processed}");

			return new ContentResult
			{
				Content = twiml,
				ContentType = "application/xml",
				StatusCode = 200
			};
		}

		/// <summary>The SMS refusal for a lapsed dedicated number (sent from the master number; no message content).</summary>
		public static string LapsedNumberReply => $"This number no longer accepts messages for the department. For emergencies, dial {EmergencyNumber.Current}.";

		/// <summary>What became of a text sent to a dedicated number.</summary>
		private enum DedicatedNumberTexts
		{
			/// <summary>Not a dedicated number (or SMS intake is off on it): the master-number paths apply.</summary>
			NotDedicated = 0,

			/// <summary>Intake recorded it (a call, a conversation note, a refusal, a duplicate) or it was a STOP: 200.</summary>
			Handled = 1,

			/// <summary>Intake failed before anything was saved: 503, so the failure is visible to Twilio and a redelivery is taken again.</summary>
			Failed = 2
		}

		/// <summary>
		/// Texts to a department's dedicated number (Active with SMS intake on) become pending calls through intake; a Lapsed number
		/// answers with a polite refusal. NotDedicated when the number texted is not a dedicated number (or has SMS intake off), so the
		/// master-number paths (text commands, legacy text-to-call, chatbot) run exactly as before. Acknowledgements and refusals
		/// go out from the master Resgrid number (D5: no outbound from department numbers). Intake text is not copied into the
		/// inbound message log; the pending call holds it and IntakeMessages is the audit.
		/// </summary>
		private async Task<DedicatedNumberTexts> TryHandleDedicatedNumberTextAsync(TwilioMessage request)
		{
			var (number, lookupFailed) = await FindDedicatedNumberAsync(request.To);
			if (number == null)
			{
				// The number table could not be read and the text was not sent to the master number: it may be a dedicated number, whose texts
				// must never run as the sender's member commands in their own department. 503, so the failure shows and a redelivery is taken again.
				if (lookupFailed && !IsMasterNumber(request.To))
					return DedicatedNumberTexts.Failed;
				return DedicatedNumberTexts.NotDedicated;
			}

			var isLapsed = number.Status == (int)DepartmentPhoneNumberStatuses.Lapsed;
			if (!isLapsed && (number.Status != (int)DepartmentPhoneNumberStatuses.Active || !number.SmsIntakeEnabled))
				return DedicatedNumberTexts.NotDedicated;

			// M0326: only a phone number can report something (and be texted back). Short codes and alphanumeric sender ids (marketing,
			// verification codes, carrier notices) are recorded as refused: no call, no conversation, nothing sent back.
			var phone = IntakeChannelAddresses.PhoneNumber(request.From);
			var sender = phone ?? request.From;

			// Opting out never creates a call, and nothing is sent back to someone who asked us to stop. Their open conversation with the
			// department (plan §8) closes and stops texting them: replies come from the master number, which this STOP did not reach.
			// An opt-out that did not take (the thread could not be read, or its close failed) answers 503 until it is durable; the redelivery of
			// the STOP closes the thread.
			if (_textCommandService.DetermineType(request.Body).Type == TextCommandTypes.Stop)
			{
				try
				{
					var thread = await _intakeConversationOperations.GetOpenForParticipantAsync(number.DepartmentId, IntakeChannelTypes.Sms, sender);
					if (thread != null && !await _intakeConversations.HandleParticipantMessageAsync(thread, request.MessageSid, request.Body, DateTime.UtcNow, CancellationToken.None))
					{
						Framework.Logging.LogError($"[Twilio SMS] MessageSid={request.MessageSid} conversation opt-out on a dedicated number did not take; answering 503.");
						return DedicatedNumberTexts.Failed;
					}
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"[Twilio SMS] MessageSid={request.MessageSid} conversation opt-out on a dedicated number failed.");
					return DedicatedNumberTexts.Failed;
				}

				return DedicatedNumberTexts.Handled;
			}

			IntakeResult result;
			try
			{
				result = await _intakeService.ReceiveAsync(new IntakeEnvelope
				{
					ChannelType = IntakeChannelTypes.Sms,
					ChannelAddress = number.PhoneNumber,
					ProviderMessageId = request.MessageSid,
					Sender = sender,
					Body = request.Body,
					ReceivedOnUtc = DateTime.UtcNow,
					RefusalReason = isLapsed ? "NumberLapsed" : phone == null ? "SenderNotPhoneNumber" : null,
					// Plan §8 D15: the text opens (or joins) a conversation continued from the master number; its welcome replaces the ack.
					OpensConversation = !isLapsed && phone != null
				}, HttpContext?.RequestAborted ?? CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"[Twilio SMS] MessageSid={request.MessageSid} dedicated number intake failed.");
				return DedicatedNumberTexts.Failed;
			}

			Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} dedicated number of department {number.DepartmentId}: intake {result?.State} ({result?.Reason})");

			// Failed without a call: nothing holds the text (IntakeService keeps a call it saved as CallCreated, never Failed). A duplicate whose
			// first delivery has no call yet (still running, or failed and not retried) is no proof either: the same 503, never an acknowledgement.
			if (result == null || result.State == IntakeMessageStates.Failed && !result.CallId.HasValue ||
				result.State == IntakeMessageStates.Duplicate && result.Reason == IntakeResultReasons.DuplicateInProgress)
				return DedicatedNumberTexts.Failed;

			try
			{
				var reply = isLapsed ? LapsedNumberReply : result.Acknowledgement;
				// Nothing goes back to a sender that is not a phone number, a blocked sender, or one past the per-sender limit (M0326). A lapsed
				// number's refusal is recorded before intake looks at the block list, so a blocked sender is checked here too.
				if (!string.IsNullOrWhiteSpace(reply) && phone != null && result.State != IntakeMessageStates.Duplicate &&
					result.State != IntakeMessageStates.SenderNotAllowed && result.State != IntakeMessageStates.SenderBlocked &&
					result.State != IntakeMessageStates.AddedToCall && !(isLapsed && await IsSenderBlockedAsync(number.DepartmentId, sender)))
					await _textMessageProvider.SendTextMessage(sender, reply, null, default(MobileCarriers), number.DepartmentId);
			}
			catch (Exception ex)
			{
				// The text is recorded; a lost acknowledgement must not make Twilio deliver it again.
				Framework.Logging.LogException(ex, $"[Twilio SMS] MessageSid={request.MessageSid} dedicated number acknowledgement failed.");
			}

			return DedicatedNumberTexts.Handled;
		}

		private enum ConversationReplies
		{
			/// <summary>The sender has no open intake conversation: the master number's usual handling applies.</summary>
			None = 0,

			/// <summary>The text went to the sender's open conversation; nothing else runs.</summary>
			Handled = 1,

			/// <summary>STOP closed the sender's conversation; the master number's own STOP handling still runs.</summary>
			OptedOut = 2,

			/// <summary>The text is the open conversation's but could not be made durable: 503, nothing else runs.</summary>
			Retry = 3
		}

		/// <summary>
		/// A text to the master number from someone with an open intake conversation (plan §8 D15; the most recently active one within
		/// IntakeConfig.ConversationReplyWindowHours, any department) is that conversation's next message. Only a text to the number the
		/// thread texts from (its ReplyFromAddress, the master number) is a reply: a text to any other number (a dedicated number with SMS
		/// intake off, a legacy department number) keeps that number's own handling.
		/// </summary>
		private async Task<ConversationReplies> TryHandleConversationReplyAsync(TwilioMessage request)
		{
			var sender = IntakeChannelAddresses.PhoneNumber(request.From) ?? request.From;
			IntakeConversation thread;
			try
			{
				thread = await _intakeConversations.FindOpenForReplyAsync(IntakeChannelTypes.Sms, sender, DateTime.UtcNow);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"[Twilio SMS] MessageSid={request.MessageSid} intake conversation reply lookup failed.");
				return ConversationReplies.None;
			}

			if (thread != null && !IsConversationReplyNumber(request.To, thread))
				thread = null;

			var stop = Resgrid.Services.Intake.IntakeConversationTexts.IsOptOut(request.Body);

			// A redelivery (Twilio retrying a 503 or a lost answer) stays with the thread that recorded this MessageSid the first time: even when
			// another department's thread of the same texter has become the most recently active since (the text is never moved to another
			// department's call), and even when no thread of theirs is open any more (a recorded reply never falls through to member commands or
			// the chatbot). One index seek on the texter's own threads; nothing to read for a sender who never texted an intake number.
			(IntakeConversation Thread, bool Recorded) bound;
			try
			{
				bound = await _intakeConversationOperations.FindThreadForProviderMessageAsync(IntakeChannelTypes.Sms, sender, request.MessageSid);
			}
			catch (Exception ex)
			{
				// Which thread first took this text is unknown, so none is chosen: the most recently active one may be another department's.
				// With an open thread to reply to, a retryable answer (the redelivery reads the binding again); with none, the master number's
				// usual handling, as for any text from someone with no open thread.
				Framework.Logging.LogException(ex, $"[Twilio SMS] MessageSid={request.MessageSid} intake conversation redelivery lookup failed.");
				return thread != null ? ConversationReplies.Retry : ConversationReplies.None;
			}

			if (bound.Thread != null)
			{
				// The bound thread decides, but only for a text to the number it texts from (a MessageSid is one message to one number).
				if (!IsConversationReplyNumber(request.To, bound.Thread))
					return ConversationReplies.None;

				if (!bound.Thread.IsOpen)
				{
					// Its thread closed since. Not on its call: the master number's usual handling, as for any reply to a closed thread, and never
					// another thread. On it: done. A STOP whose opt-out never persisted (its close lost to another, the opt-out write failed) is
					// finished on that thread now, nothing reopened or sent; until it is, a retryable answer.
					Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} was first recorded on closed intake conversation " +
						$"{bound.Thread.IntakeConversationId} (department {bound.Thread.DepartmentId}, recorded={bound.Recorded}); not moved to another thread.");
					if (!bound.Recorded)
						return ConversationReplies.None;
					if (!stop)
						return ConversationReplies.Handled;
					return await _intakeConversationOperations.CompleteOptOutAsync(bound.Thread) ? ConversationReplies.OptedOut : ConversationReplies.Retry;
				}

				thread = bound.Thread;
			}

			if (thread == null)
				return ConversationReplies.None;

			// From here the text is the texter's, whatever happens while it is recorded: it never falls through to member commands. Not handled
			// means it is not durable on the thread (the thread closed meanwhile, or its row, note or STOP close failed): a retryable answer, so a
			// redelivery either finishes it on the same thread or, with the thread closed, takes the master number's usual path.
			bool handled;
			try
			{
				handled = await _intakeConversations.HandleParticipantMessageAsync(thread, request.MessageSid, request.Body, DateTime.UtcNow, CancellationToken.None);
				Framework.Logging.LogInfo($"[Twilio SMS] MessageSid={request.MessageSid} reply to intake conversation of department {thread.DepartmentId} (handled={handled}, stop={stop}).");
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"[Twilio SMS] MessageSid={request.MessageSid} intake conversation reply could not be recorded.");
				handled = false;
			}

			if (!handled)
				return ConversationReplies.Retry;
			return stop ? ConversationReplies.OptedOut : ConversationReplies.Handled;
		}

		/// <summary>The texted number is the one the thread texts the caller from (the master number when the thread has none recorded).</summary>
		private static bool IsConversationReplyNumber(string calledNumber, IntakeConversation thread)
		{
			var to = IntakeChannelAddresses.PhoneNumber(calledNumber);
			var replyFrom = IntakeChannelAddresses.PhoneNumber(string.IsNullOrWhiteSpace(thread.ReplyFromAddress) ? Config.NumberProviderConfig.TwilioResgridNumber : thread.ReplyFromAddress);
			return to != null && replyFrom != null && string.Equals(to, replyFrom, StringComparison.Ordinal);
		}

		/// <summary>The dedicated department number (Active or Lapsed) the request was sent to, else null.</summary>
		private async Task<DepartmentPhoneNumber> DedicatedNumberAsync(string calledNumber) => (await FindDedicatedNumberAsync(calledNumber)).Number;

		/// <summary>
		/// The dedicated department number (Active or Lapsed) the request was sent to. <c>LookupFailed</c>: the number table could not be read,
		/// so a null Number does not prove the called number is not a dedicated one (only <see cref="IsMasterNumber"/> does then).
		/// </summary>
		private async Task<(DepartmentPhoneNumber Number, bool LookupFailed)> FindDedicatedNumberAsync(string calledNumber)
		{
			var e164 = IntakeChannelAddresses.PhoneNumber(calledNumber);
			if (e164 == null)
				return (null, false);

			try
			{
				return (await _departmentPhoneNumbersService.GetByNumberAsync(e164), false);
			}
			catch (Exception ex)
			{
				// The master-number paths still work while the number table cannot be read.
				Framework.Logging.LogException(ex, "Dedicated number lookup failed.");
				return (null, true);
			}
		}

		/// <summary>The request was sent to the region's master Resgrid number, which is never a department's dedicated number.</summary>
		private static bool IsMasterNumber(string calledNumber)
		{
			var to = IntakeChannelAddresses.PhoneNumber(calledNumber);
			var master = IntakeChannelAddresses.PhoneNumber(Config.NumberProviderConfig.TwilioResgridNumber);
			return to != null && master != null && string.Equals(to, master, StringComparison.Ordinal);
		}

		/// <summary>The sender is on the department's blocked list (M0326); a list that cannot be read lets the message through, as intake does.</summary>
		private async Task<bool> IsSenderBlockedAsync(int departmentId, string sender)
		{
			try
			{
				return await _intakeScreening.GetBlockAsync(departmentId, sender) != null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"[Twilio SMS] blocked-sender check failed for department {departmentId}.");
				return false;
			}
		}

		/// <summary>
		/// The member IVR's department. A menu read on a department's dedicated number (plan D2) carries that department in every action
		/// URL (<paramref name="boundDepartmentId"/>): the caller stays in it while still its member, and gets nothing otherwise — never the
		/// member's own department, where the same digit would mean another department's status or staffing option. Without one (calls to
		/// the master number) it is the caller's own department, as before; an action URL from before the binding falls back to the
		/// called number. <c>Bound</c> is the department to carry into the next action URL (null on the master number).
		/// </summary>
		private async Task<(Department Department, int? Bound)> ResolveIvrDepartmentAsync(string userId, string calledNumber, int? boundDepartmentId)
		{
			int? bound = boundDepartmentId.HasValue && boundDepartmentId.Value > 0 ? boundDepartmentId : null;
			if (!bound.HasValue)
			{
				var (number, lookupFailed) = string.IsNullOrWhiteSpace(calledNumber) ? (null, false) : await FindDedicatedNumberAsync(calledNumber);
				if (number == null)
				{
					// A number that may be a dedicated one (its lookup failed, and it is not the master number) never falls back to the
					// caller's own department: nothing.
					if (lookupFailed && !IsMasterNumber(calledNumber))
						return (null, null);
					return (await _departmentsService.GetDepartmentByUserIdAsync(userId), null);
				}
				bound = number.DepartmentId;
			}

			try
			{
				if (!string.IsNullOrWhiteSpace(userId) && await _departmentsService.IsMemberOfDepartmentAsync(bound.Value, userId))
					return (await _departmentsService.GetDepartmentByIdAsync(bound.Value, false), bound);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"[Twilio Voice] the member IVR could not confirm the caller in department {bound.Value}.");
			}

			return (null, bound);
		}

		/// <summary>The member IVR action URL, carrying the bound department of a dedicated number's menu (see <see cref="ResolveIvrDepartmentAsync"/>).</summary>
		private static Uri IvrActionUri(string action, string userId, int? boundDepartmentId, string query = null) =>
			new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/{action}?userId={userId}{query}" +
				(boundDepartmentId.HasValue ? $"&departmentId={boundDepartmentId.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty));

		/// <summary>The member IVR cannot continue (the department could not be confirmed): one message and a hang-up.</summary>
		private async Task<ActionResult> IvrUnavailableAsync()
		{
			var response = new VoiceResponse();
			await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable);
			response.Hangup();
			return CreateVoiceContentResult(response);
		}

		// Original (pre-chatbot) inbound text-command handling. Retained so departments that have not
		// enabled the Chatbot Twilio integration feature flag keep their existing behavior. departmentId
		// and userProfile are resolved by the caller so the flag can be evaluated before dispatching here.
		private async System.Threading.Tasks.Task ProcessTextCommandsAsync(TextMessage textMessage, InboundMessageEvent messageEvent,
			MessagingResponse response, int? departmentId, UserProfile userProfile, bool senderNumberUnverified = false)
		{
			// Diagnostic: without a department the legacy path adds no message, so the sender gets no reply.
			if (!departmentId.HasValue)
				Framework.Logging.LogInfo($"[Twilio SMS] Text-command path: no department resolved for sender {Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} → number {textMessage.To}; no reply will be sent.");

			if (departmentId.HasValue)
			{
				// Run all department-level lookups in parallel — they are independent of each other.
				var departmentTask = _departmentsService.GetDepartmentByIdAsync(departmentId.Value);
				var dispatchNumbersTask = _departmentSettingsService.GetTextToCallSourceNumbersForDepartmentAsync(departmentId.Value);
				var authorizedTask = _limitsService.CanDepartmentProvisionNumberAsync(departmentId.Value);
				var customStatesTask = _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId.Value);

				await System.Threading.Tasks.Task.WhenAll(departmentTask, dispatchNumbersTask, authorizedTask, customStatesTask);

				var department = departmentTask.Result;
				var dispatchNumbers = dispatchNumbersTask.Result;
				var authroized = authorizedTask.Result;
				var customStates = customStatesTask.Result;

				messageEvent.CustomerId = departmentId.Value.ToString();

				// Diagnostic: when Authorized=false the whole reply block below is skipped (no <Message>).
				// The matching PlanId is logged by LimitsService.CanDepartmentProvisionNumberAsync.
				Framework.Logging.LogInfo($"[Twilio SMS] DepartmentId={departmentId.Value} Authorized(CanProvisionNumber)={authroized}");

				if (authroized)
				{
					bool isDispatchSource = false;

					if (!String.IsNullOrWhiteSpace(dispatchNumbers))
						isDispatchSource = _numbersService.DoesNumberMatchAnyPattern(dispatchNumbers.Split(Char.Parse(",")).ToList(), textMessage.Msisdn);

					var routing = TextIntakeRouting.Decide(TextIntakePath.TwilioLegacy, isDispatchSource, false, false);

					if (routing.CallBranch)
					{
						var users = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId.Value, true);
						var c = await BuildTextToCallAsync(department, textMessage, users);

						var savedCall = await _callsService.SaveCallAsync(c);

						// Run card auto-dispatch for text-to-call: additively merge
						// recommended units/personnel when auto-dispatch resolves on.
						try
						{
							if (await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, savedCall.DepartmentId))
							{
								// Linked to RequestAborted so a caller that goes away also stops the
								// work, and cancelled after the budget so a slow mapping provider
								// cannot eat the webhook. The engine honours the token at its
								// external-I/O boundaries, so this stops the calls rather than just
								// abandoning the await.
								using (var enrichmentCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext?.RequestAborted ?? CancellationToken.None))
								{
									enrichmentCts.CancelAfter(RunCardEnrichmentBudget);

									var recommendation = await _dispatchRecommendationService.EnrichCallForDispatchAsync(savedCall, 1, true, enrichmentCts.Token);

									if (recommendation.MatchedRunCardId.HasValue && recommendation.AutoDispatch && recommendation.HasRecommendations)
									{
										savedCall = await _callsService.SaveCallAsync(savedCall, enrichmentCts.Token);
										await _dispatchRecommendationService.RecordActivationAsync(savedCall, recommendation, null, enrichmentCts.Token);
									}
								}
							}
						}
						catch (Exception ex)
						{
							// A recommendation failure — including overrunning the budget above —
							// must never block the text-to-call dispatch itself.
							Logging.LogException(ex);
						}

						var cqi = new CallQueueItem();
						cqi.Call = savedCall;
						cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(users.Select(x => x.UserId).ToList());
						cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(cqi.Call.DepartmentId);

						await _queueService.EnqueueCallBroadcastAsync(cqi);

						messageEvent.Processed = true;
					}

					if (routing.CommandBranch)
					{
						// Reuse the profile fetched above when the department was resolved via mobile number;
						// only hit the DB again if the department came from the phone-number lookup path.
						var profile = userProfile ?? await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);

						// SECURITY: same verified-number bar as the caller — an unverified number must not
						// act as the matched user.
						if (profile != null && profile.MobileNumberVerified != true)
						{
							senderNumberUnverified = true;
							profile = null;
						}

						// No matching user profile for the sender's number: reply so the user knows, rather than
						// returning an empty response (pre-refactor behavior).
						if (profile == null)
						{
							Framework.Logging.LogInfo($"[Twilio SMS] DepartmentId={departmentId.Value} sender {Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} has no usable user profile (unverified={senderNumberUnverified}); replying with not-found message.");
							messageEvent.Processed = true;

							if (senderNumberUnverified)
								response.Message("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
							else
								response.Message("Resgrid: We couldn't find a Resgrid user linked to this mobile number. Please add this number to your Resgrid profile to use text commands.");
						}

						if (profile != null)
						{
							var customActions = customStates.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Personnel);
							var customStaffing = customStates.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Staffing);

							// The department's own status and staffing sets, so the commands the HELP reply lists for
							// them (names and positions) are the ones that work.
							var payload = _textCommandService.DetermineType(textMessage.Text, customActions, customStaffing);

							// Diagnostic: which command the inbound text resolved to (None still replies with a hint).
							Framework.Logging.LogInfo($"[Twilio SMS] DepartmentId={departmentId.Value} UserId={profile.UserId} resolved text command={payload.Type}");

							switch (payload.Type)
							{
								case TextCommandTypes.None:
									response.Message("Resgrid (https://resgrid.com) Automated Text System. Unknown command, text help for supported commands.");
									break;
								case TextCommandTypes.Help:
									messageEvent.Processed = true;

									var help = new StringBuilder();
									help.Append("Resgrid Text Commands" + Environment.NewLine);
									help.Append("---------------------" + Environment.NewLine);
									help.Append("These are the commands you can text to alter your status and staffing. Text help for help." + Environment.NewLine);
									help.Append("---------------------" + Environment.NewLine);
									help.Append("Core Commands" + Environment.NewLine);
									help.Append("---------------------" + Environment.NewLine);
									help.Append("STOP: To turn off all text messages" + Environment.NewLine);
									help.Append("HELP: This help text" + Environment.NewLine);
									help.Append("CALLS: List active calls" + Environment.NewLine);
									help.Append("C[CallId]: Get Call Detail i.e. C1445" + Environment.NewLine);
									help.Append("UNITS: List unit statuses" + Environment.NewLine);
									help.Append("SWITCH [name or number]: Change your active department" + Environment.NewLine);
									help.Append("---------------------" + Environment.NewLine);
									help.Append("Status or Action Commands" + Environment.NewLine);
									help.Append("---------------------" + Environment.NewLine);

									if (customActions != null && customActions.IsDeleted == false && customActions.GetActiveDetails() != null && customActions.GetActiveDetails().Any())
									{
										var activeDetails = customActions.GetActiveDetails();
										for (int i = 0; i < activeDetails.Count; i++)
										{
											help.Append($"{activeDetails[i].ButtonText.Trim().Replace(" ", "").Replace("-", "").Replace(":", "")} or {i + 1}: {activeDetails[i].ButtonText}" + Environment.NewLine);
										}
									}
									else
									{
										help.Append("responding or 1: Responding" + Environment.NewLine);
										help.Append("notresponding or 2: Not Responding" + Environment.NewLine);
										help.Append("onscene or 3: On Scene" + Environment.NewLine);
										help.Append("standingby or 4: Available" + Environment.NewLine);
									}

									help.Append("---------------------" + Environment.NewLine);
									help.Append("Staffing Commands" + Environment.NewLine);
									help.Append("---------------------" + Environment.NewLine);

									if (customStaffing != null && customStaffing.IsDeleted == false && customStaffing.GetActiveDetails() != null && customStaffing.GetActiveDetails().Any())
									{
										var activeDetails = customStaffing.GetActiveDetails();
										for (int i = 0; i < activeDetails.Count; i++)
										{
											help.Append($"{activeDetails[i].ButtonText.Trim().Replace(" ", "").Replace("-", "").Replace(":", "")} or S{i + 1}: {activeDetails[i].ButtonText}" + Environment.NewLine);
										}
									}
									else
									{
										help.Append("available or s1: Available Staffing" + Environment.NewLine);
										help.Append("delayed or s2: Delayed Staffing" + Environment.NewLine);
										help.Append("unavailable or s3: Unavailable Staffing" + Environment.NewLine);
										help.Append("committed or s4: Committed Staffing" + Environment.NewLine);
										help.Append("onshift or s5: On Shift Staffing" + Environment.NewLine);
									}

									response.Message(help.ToString());

									//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Help", help.ToString(), department.DepartmentId, textMessage.To, profile);
									break;
								case TextCommandTypes.SwitchDepartment:
									messageEvent.Processed = true;
									response.Message(await _textDepartmentSwitchService.BuildSwitchCommandResponseAsync(profile, payload.Data, "[Twilio SMS]"));
									break;
								case TextCommandTypes.Action:
									messageEvent.Processed = true;
									using (StatusWriteActor.Begin(profile.UserId, StatusSetOrigins.Sms))
										await _actionLogsService.SetUserActionAsync(profile.UserId, department.DepartmentId, (int)payload.GetActionType());
									response.Message(string.Format("Resgrid received your text command. Status changed to: {0}", payload.GetActionType()));
									//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Status", string.Format("Resgrid recieved your text command. Status changed to: {0}", payload.GetActionType()), department.DepartmentId, textMessage.To, profile);
									break;
								case TextCommandTypes.Staffing:
									messageEvent.Processed = true;
									await _userStateService.CreateUserState(profile.UserId, department.DepartmentId, (int)payload.GetStaffingType());
									response.Message(string.Format("Resgrid received your text command. Staffing level changed to: {0}", payload.GetStaffingType()));
									//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Staffing", string.Format("Resgrid recieved your text command. Staffing level changed to: {0}", payload.GetStaffingType()), department.DepartmentId, textMessage.To, profile);
									break;
								case TextCommandTypes.Stop:
									messageEvent.Processed = true;
									await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId);
									response.Message("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
									break;
								case TextCommandTypes.CustomAction:
									messageEvent.Processed = true;
									using (StatusWriteActor.Begin(profile.UserId, StatusSetOrigins.Sms))
										await _actionLogsService.SetUserActionAsync(profile.UserId, department.DepartmentId, payload.GetCustomActionType());

									if (customActions != null && customActions.IsDeleted == false && customActions.GetActiveDetails() != null && customActions.GetActiveDetails().Any() &&
										customActions.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == payload.GetCustomActionType()) != null)
									{
										var detail = customActions.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == payload.GetCustomActionType());
										response.Message(string.Format("Resgrid received your text command. Status changed to: {0}", detail.ButtonText));
									}
									else
									{
										response.Message("Resgrid received your text command and updated your status");
									}

									break;
								case TextCommandTypes.CustomStaffing:
									messageEvent.Processed = true;
									await _userStateService.CreateUserState(profile.UserId, department.DepartmentId, payload.GetCustomStaffingType());

									if (customStaffing != null && customStaffing.IsDeleted == false && customStaffing.GetActiveDetails() != null && customStaffing.GetActiveDetails().Any() &&
										customStaffing.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == payload.GetCustomStaffingType()) != null)
									{
										var detail = customStaffing.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == payload.GetCustomStaffingType());
										response.Message(string.Format("Resgrid received your text command. Staffing changed to: {0}", detail.ButtonText));
									}
									else
									{
										response.Message("Resgrid received your text command and updated your staffing");
									}

									break;
								case TextCommandTypes.MyStatus:
									messageEvent.Processed = true;


									var userStatus = await _actionLogsService.GetLastActionLogForUserAsync(profile.UserId);
									var userStaffing = await _userStateService.GetLastUserStateByUserIdAsync(profile.UserId);

									// No status inside the auto-available hour reads as the department's Available status.
									var customStatusLevel = userStatus != null
										? await _customStateService.GetCustomPersonnelStatusAsync(department.DepartmentId, userStatus)
										: await _customStateService.GetDefaultPersonnelStatusAsync(department.DepartmentId);
									var customStaffingLevel = await _customStateService.GetCustomPersonnelStaffingAsync(department.DepartmentId, userStaffing);

									response.Message(
										$"Hello {profile.FullName.AsFirstNameLastName} at {DateTime.UtcNow.TimeConverterToString(department)} your current status is {customStatusLevel?.ButtonText ?? "Unknown"} and your current staffing is {customStaffingLevel?.ButtonText ?? "Unknown"}.");
									break;
								case TextCommandTypes.Calls:
									messageEvent.Processed = true;

									// Group-scoped dispatch (off by default): only the calls in the texter's area or that they're on.
									var activeCalls = await _dispatchScopeService.FilterCallsForUserAsync(department.DepartmentId, profile.UserId,
										await _callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId));

									var activeCallText = new StringBuilder();
									activeCallText.Append($"Active Calls for {department.Name}" + Environment.NewLine);
									activeCallText.Append("---------------------" + Environment.NewLine);

									foreach (var activeCall in activeCalls)
									{
										activeCallText.Append($"CallId: {activeCall.CallId} Name: {activeCall.Name} Nature:{StringHelpers.StripHtmlTagsCharArray(activeCall.NatureOfCall)}" + Environment.NewLine);
									}

									response.Message(activeCallText.ToString().Truncate(1200));
									break;
								case TextCommandTypes.Units:
									messageEvent.Processed = true;

									var unitStatus = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(department.DepartmentId);

									var unitStatusesText = new StringBuilder();
									unitStatusesText.Append($"Unit Statuses for {department.Name}" + Environment.NewLine);
									unitStatusesText.Append("---------------------" + Environment.NewLine);

									foreach (var unit in unitStatus)
									{
										// Security > View Units, the same filter the unit lists apply.
										if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, profile.UserId, department.DepartmentId))
											continue;

										var unitState = await _customStateService.GetCustomUnitStateAsync(unit);
										unitStatusesText.Append($"{unit.Unit.Name} is {unitState.ButtonText}" + Environment.NewLine);
									}

									response.Message(unitStatusesText.ToString().Truncate(1200));
									break;
								case TextCommandTypes.CallDetail:
									messageEvent.Processed = true;

									var call = await _callsService.GetCallByIdAsync(int.Parse(payload.Data));

									// Guard against a missing call (NRE), against reading a call that belongs to another
									// department (cross-department data leakage), and, with group-scoped dispatch on, against
									// a call outside the texter's area that the CALLS list would not have shown them.
									if (call == null || call.DepartmentId != department.DepartmentId
										|| !await _dispatchScopeService.CanUserAccessCallAsync(department.DepartmentId, profile.UserId, call))
									{
										response.Message("Resgrid could not find that call.");
										break;
									}

									var callText = new StringBuilder();
									callText.Append($"Call Information for {call.Name}" + Environment.NewLine);
									callText.Append("---------------------" + Environment.NewLine);
									callText.Append($"Id: {call.CallId}" + Environment.NewLine);
									callText.Append($"Number: {call.Number}" + Environment.NewLine);
									callText.Append($"Logged: {call.LoggedOn.TimeConverterToString(department)}" + Environment.NewLine);
									callText.Append("-----Nature-----" + Environment.NewLine);
									callText.Append(call.NatureOfCall + Environment.NewLine);
									callText.Append("-----Address-----" + Environment.NewLine);

									if (!String.IsNullOrWhiteSpace(call.Address))
										callText.Append(call.Address + Environment.NewLine);
									else if (!string.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
									{
										try
										{
											string[] points = call.GeoLocationData.Split(char.Parse(","));

											// The task must be awaited — appending it directly stringifies as
											// "System.Threading.Tasks.Task`1[System.String]" in the SMS reply.
											// Bounded like the voice path so a slow geocoder can't stall the webhook.
											// TryParse with InvariantCulture: malformed coordinates skip the lookup
											// instead of throwing, and a comma-decimal server culture can't silently
											// misread "47.606" as 47606.
											if (points != null && points.Length == 2
												&& double.TryParse(points[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
												&& double.TryParse(points[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
											{
												callText.Append(await _geoLocationProvider.GetAproxAddressFromLatLong(latitude, longitude)
													.WaitAsync(TimeSpan.FromSeconds(2), HttpContext?.RequestAborted ?? CancellationToken.None) + Environment.NewLine);
											}
										}
										catch
										{
										}
									}

									response.Message(callText.ToString());
									break;
							}
						}
					}
				}
				else if (senderNumberUnverified)
				{
					// Checked BEFORE the switch offer: switching departments is an action, and an unverified
					// sender may not act as the matched user. Their profile routed them to the right
					// department; verifying the number is the prerequisite for everything else.
					Framework.Logging.LogInfo($"[Twilio SMS] DepartmentId={departmentId.Value} not authorized for inbound text (plan gate) and sender is unverified; replying with verify-number message.");
					messageEvent.Processed = true;
					response.Message("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
				}
				else if (userProfile != null)
				{
					// The user's ACTIVE department doesn't support inbound text. STOP is honored, a SWITCH
					// command is processed, and other texts get the switch options (or the plan message
					// when no supported alternatives exist). Shared with SignalWire via the switch service.
					messageEvent.Processed = true;
					response.Message(await _textDepartmentSwitchService.BuildUnsupportedActiveDepartmentResponseAsync(
						textMessage.Text, departmentId.Value, userProfile, "[Twilio SMS]"));
				}
				else
				{
					// Department resolved but its plan doesn't include inbound text messaging (only the free
					// tier lacks it): reply instead of returning an empty response (pre-refactor behavior).
					Framework.Logging.LogInfo($"[Twilio SMS] DepartmentId={departmentId.Value} not authorized for inbound text (plan gate); replying with unsupported message.");
					messageEvent.Processed = true;
					response.Message("Resgrid: Inbound text messaging isn't available on your department's current plan. Please upgrade to a paid plan to enable text commands.");
				}
			}
			else if (textMessage.To == Config.NumberProviderConfig.TwilioResgridNumber) // Resgrid master text number
			{
				var profile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);
				var payload = _textCommandService.DetermineType(textMessage.Text);

				// A sender whose profile mobile number is unverified can't be identified, so commands can't
				// be acted on — direct them to verify instead of replying "unknown command". HELP and STOP
				// still work below: neither needs a trusted identity, and STOP must always opt out.
				if (profile != null && profile.MobileNumberVerified != true
					&& payload.Type != TextCommandTypes.Help && payload.Type != TextCommandTypes.Stop)
				{
					Framework.Logging.LogInfo($"[Twilio SMS] Master number: sender {Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} matched a profile but the mobile number is not verified; replying with verify-number message.");
					messageEvent.Processed = true;
					response.Message("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
					return;
				}

				switch (payload.Type)
				{
					case TextCommandTypes.None:
						response.Message("Resgrid (https://resgrid.com) Automated Text System. Unknown command, text help for supported commands.");
						break;
					case TextCommandTypes.Help:
						messageEvent.Processed = true;

						var help = new StringBuilder();
						help.Append("Resgrid Text Commands" + Environment.NewLine);
						help.Append("---------------------" + Environment.NewLine);
						help.Append("This is the Resgrid system for first responders (https://resgrid.com) automated text system. Your department isn't signed up for inbound text messages, but you can send the following commands." +
									Environment.NewLine);
						help.Append("---------------------" + Environment.NewLine);
						help.Append("STOP: To turn off all text messages" + Environment.NewLine);
						help.Append("HELP: This help text" + Environment.NewLine);

						response.Message(help.ToString());

						break;
					case TextCommandTypes.Stop:
						messageEvent.Processed = true;

						if (profile == null)
						{
							response.Message("Unable to locate your profile. Please log in to Resgrid to manage your text message settings.");
							break;
						}

						await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId);
						response.Message("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
						break;
				}
			}
			else if (senderNumberUnverified)
			{
				// No department resolved and this isn't the master number, but the sender did match a
				// profile with an unverified mobile number. Previously this was silence; tell them the
				// actionable fix instead.
				Framework.Logging.LogInfo($"[Twilio SMS] No department resolved for sender {Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} (unverified profile match); replying with verify-number message.");
				messageEvent.Processed = true;
				response.Message("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
			}
		}

		[HttpGet("VoiceCall")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceCall(string userId, int callId, [FromQuery] string retry = null, [FromQuery] string replay = null)
		{
			if (!int.TryParse(replay, out var replayCount) || replayCount < 0)
				replayCount = 0;

			var response = new VoiceResponse();
			var call = await _callsService.GetCallByIdAsync(callId);
			call = await _callsService.PopulateCallData(call, true, true, false, false, false, false, false, false, false);

			if (call == null)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosed);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			if (call.State == (int)CallStates.Cancelled || call.State == (int)CallStates.Closed || call.IsDeleted)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosedByNumber(call.Number), call.DepartmentId);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			// The signed provider callback binds this challenge to the dialed, verified phone.
			var destination = Request.Query["To"].ToString();
			string challenge = null;
			try { challenge = await _adpRelease.CreateChallengeAsync(call.DepartmentId, callId, userId, destination, ProtectedDataEgressChannel.Voice); }
			catch (Exception) { Logging.LogError($"ADP voice challenge unavailable for department {call.DepartmentId}; using the safe prompt."); }
			if (challenge != null)
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
				var gatherPin = new Gather(action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/AdpVoicePin?challenge={challenge}"), method: "POST", finishOnKey: "#");
				gatherPin.Say(Resgrid.Localization.Areas.User.SystemMessages.SystemMessagesResources.Get("AdpPinVoiceChallenge", profile?.Language));
				response.Append(gatherPin);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}
			call = await _adpProjection.BuildNotificationSafeCallAsync(call.DepartmentId, call, ProtectedDataEgressChannel.Voice);

			// Load the department's custom priority so GetPriorityText() speaks its real
			// name. The broadcast worker loads it the same way before pre-warming the
			// dispatch TTS — the prompt text (and therefore the TTS cache key) must match.
			try
			{
				if (call.CallPriority == null)
					call.CallPriority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			}
			catch { /* enum fallback text still works */ }

			// For outbound calls, allow a brief pause for the audio bridge to
			// stabilize after the callee answers before attempting playback.
			response.Pause(length: 1);

			// For dispatch playback, attempt to fetch (or pre-warm) the TTS URL
			// within a short timeout so that the TwiML response is returned before
			// Twilio's 15-second webhook timeout expires. If the dispatch text is
			// not yet cached, we play a brief "please wait" prompt and redirect
			// back to this endpoint, giving the TTS service time to complete
			// generation in the background.
			var (dispatchReady, dispatchText) = await TryAppendDispatchPlaybackAsync(response, call);
			if (!dispatchReady)
			{
				// Parse and increment the retry counter from the incoming request.
				if (!int.TryParse(retry, out var retryCount))
					retryCount = 0;

				if (retryCount >= MAX_DISPATCH_RETRY)
				{
					// The dispatch audio never became ready within the retry budget. If the paid
					// <Say> fallback is enabled, speak the dispatch with Twilio's native voice;
					// otherwise this is a no-op and the caller goes straight to the menu, where
					// "press 1" re-enters VoiceCall with a fresh retry budget. Either way do NOT
					// play the "call closed" prompt (a new call is not closed) and hang up.
					_twilioVoiceResponseService.AppendSayFallback(response, dispatchText);
				}
				else
				{
					// Dispatch audio isn't ready yet. Pre-warm it in the background
					// and redirect back — by the time Twilio re-fetches this endpoint
					// the audio should be cached.
					var ttsLanguage = await GetDepartmentTtsLanguageAsync(call.DepartmentId);

					// Fire off TTS generation in the background. The TTS microservice
					// caches the result, so the redirect will find it once ready.
					_twilioVoiceResponseService.PreWarmDispatchPromptAsync(dispatchText, ttsLanguage)
						.ContinueWith(t =>
						{
							if (t.IsFaulted && t.Exception != null)
								Logging.LogException(t.Exception);
						}, TaskContinuationOptions.OnlyOnFaulted);

					// We're only in this loop because TTS is already slow — the shared
					// per-request budget bounds the "please wait" prompt (falling back to
					// <Say>) so a hung TTS service can't stall this webhook past Twilio's
					// 15-second limit.
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.PleaseWaitForDispatch, call.DepartmentId);
					// Carry the replay count through the retry hop so a dispatch whose audio never
					// becomes ready still runs out of replays and hangs up.
					var nextRetry = retryCount + 1;
					response.Redirect(
						new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}&retry={nextRetry}{(replayCount > 0 ? $"&replay={replayCount}" : "")}"),
						"GET");
					return CreateVoiceContentResult(response);
				}
			}

			// Dispatch is ready (fast path, retry with cached audio, or native-voice fallback).
			var gather = new Gather(numDigits: 1, action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCallAction?userId={userId}&callId={callId}"), method: "GET")
			{
				BargeIn = true
			};
			await AppendVoicePromptAsync(gather, TwilioVoicePromptCatalog.OutboundDispatchMenu, call.DepartmentId);
			response.Append(gather);

			// No key pressed: Twilio falls through the Gather. Read the dispatch again (bounded)
			// rather than hanging up on someone who may not have heard it.
			if (replayCount < MAX_DISPATCH_REPLAY)
				response.Redirect(
					new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}&replay={replayCount + 1}"),
					"GET");
			else
				response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpPost("AdpVoicePin")]
		[ValidateRequest]
		public async Task<ActionResult> AdpVoicePin([FromQuery] string challenge, [FromForm] VoiceRequest request)
		{
			var response = new VoiceResponse();
			var text = await _adpRelease.ReleaseAsync(challenge, request.To, request.Digits, ProtectedDataEgressChannel.Voice);
			// Say sends no protected audio into Resgrid's shared TTS/URL cache.
			foreach (var chunk in DispatchVoicePromptBuilder.ChunkText(text ?? Resgrid.Localization.Areas.User.SystemMessages.SystemMessagesResources.Get("AdpPinDenied", null)))
				response.Say(chunk);
			response.Hangup();
			return CreateVoiceContentResult(response);
		}

		[HttpGet("VoiceCallAction")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceCallAction(string userId, int callId, [FromQuery] VoiceRequest twilioRequest)
		{
			var response = new VoiceResponse();

			if (twilioRequest?.Digits == "1")
			{
				response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}"), "GET");
				return CreateVoiceContentResult(response);
			}

			if (twilioRequest?.Digits == "2")
			{
				response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCallResponseOptions?userId={userId}&callId={callId}"), "GET");
				return CreateVoiceContentResult(response);
			}

			if (!string.IsNullOrWhiteSpace(twilioRequest?.Digits))
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidSelection, call?.DepartmentId);
			}

			response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}"), "GET");
			return CreateVoiceContentResult(response);
		}

		[HttpGet("VoiceCallResponseOptions")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceCallResponseOptions(string userId, int callId)
		{
			var response = new VoiceResponse();
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosed);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			if (call.State == (int)CallStates.Cancelled || call.State == (int)CallStates.Closed || call.IsDeleted)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosedByNumber(call.Number), call.DepartmentId);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(call.DepartmentId);

			for (int repeat = 0; repeat < 2; repeat++)
			{
				var gatherResponse = new Gather(action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCallRespond?userId={userId}&callId={callId}"), method: "GET", finishOnKey: "#")
				{
					BargeIn = true
				};
				await AppendVoicePromptsAsync(gatherResponse, BuildVoiceCallResponseOptionPrompts(stations), call.DepartmentId);
				response.Append(gatherResponse);
			}

			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpGet("VoiceCallRespond")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceCallRespond(string userId, int callId, [FromQuery] VoiceRequest twilioRequest)
		{
			var response = new VoiceResponse();
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosed);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			if (call.State == (int)CallStates.Cancelled || call.State == (int)CallStates.Closed || call.IsDeleted)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosedByNumber(call.Number), call.DepartmentId);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			if (twilioRequest?.Digits == "0")
			{
				response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}"), "GET");
				return CreateVoiceContentResult(response);
			}

			if (twilioRequest?.Digits == "1")
			{
				// Group-scoped dispatch (off by default). Whoever this call-back reached was dispatched, so the call is
				// in their scope; anything else is refused without recording a response.
				if (!await _dispatchScopeService.CanUserAccessCallAsync(call.DepartmentId, userId, call))
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidSelection, call.DepartmentId);
					response.Hangup();
					return CreateVoiceContentResult(response);
				}

				// The department's own Responding status when it has one (StatusTargetResolver), else the built-in one.
				var respondingToCall = await _customStateService.ResolvePersonnelStatusAsync(call.DepartmentId, ActionBaseTypes.Responding, DestinationEntityTypes.Call)
					?? (int)ActionTypes.RespondingToScene;

				using (StatusWriteActor.Begin(userId, StatusSetOrigins.Voice))
					await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, respondingToCall, null, call.CallId, (int)DestinationEntityTypes.Call);
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.RespondingToScene, call.DepartmentId);
				response.Hangup();
				return CreateVoiceContentResult(response);
			}

			if (int.TryParse(twilioRequest?.Digits, out var digit))
			{
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(call.DepartmentId);
				var index = digit - 2;

				if (index >= 0 && index < stations.Count)
				{
					var station = stations[index];

					if (station != null)
					{
						var respondingToStation = await _customStateService.ResolvePersonnelStatusAsync(call.DepartmentId, ActionBaseTypes.Responding, DestinationEntityTypes.Station)
							?? (int)ActionTypes.RespondingToStation;

						using (StatusWriteActor.Begin(userId, StatusSetOrigins.Voice))
							await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, respondingToStation, null, station.DepartmentGroupId, (int)DestinationEntityTypes.Station);
						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.RespondingToStation(station.Name), call.DepartmentId);
						response.Hangup();
						return CreateVoiceContentResult(response);
					}
				}
			}

			await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidSelection, call.DepartmentId);
			response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCallResponseOptions?userId={userId}&callId={callId}"), "GET");
			return CreateVoiceContentResult(response);
		}

		[HttpGet("VoiceVerification")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceVerification(string userId, int contactType)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return await GetVoiceVerificationErrorResult();

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, bypassCache: true);
			if (profile == null)
				return await GetVoiceVerificationErrorResult();

			string encryptedCode;
			DateTime? expiry;
			bool alreadyConsumed;
			switch ((ContactVerificationType)contactType)
			{
				case ContactVerificationType.MobileNumber:
					encryptedCode = profile.MobileVerificationCode;
					expiry = profile.MobileVerificationCodeExpiry;
					alreadyConsumed = profile.MobileVerificationVoiceCodeConsumed;
					break;
				case ContactVerificationType.HomeNumber:
					encryptedCode = profile.HomeVerificationCode;
					expiry = profile.HomeVerificationCodeExpiry;
					alreadyConsumed = profile.HomeVerificationVoiceCodeConsumed;
					break;
				default:
					return await GetVoiceVerificationErrorResult();
			}

			if (alreadyConsumed || string.IsNullOrWhiteSpace(encryptedCode) || !expiry.HasValue || DateTime.UtcNow > expiry.Value)
				return await GetVoiceVerificationErrorResult();

			string code;
			try
			{
				code = _encryptionService.Decrypt(encryptedCode);
			}
			catch (CryptographicException ex)
			{
				ReportVoiceVerificationDecryptionFailure(ex);
				return await GetVoiceVerificationErrorResult();
			}

			if (string.IsNullOrWhiteSpace(code))
				return await GetVoiceVerificationErrorResult();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(profile.UserId, false);
			if (department == null)
				return await GetVoiceVerificationErrorResult();

			switch ((ContactVerificationType)contactType)
			{
				case ContactVerificationType.MobileNumber:
					profile.MobileVerificationVoiceCodeConsumed = true;
					break;
				case ContactVerificationType.HomeNumber:
					profile.HomeVerificationVoiceCodeConsumed = true;
					break;
			}

			await _userProfileService.SaveProfileAsync(department.DepartmentId, profile);

			var response = new VoiceResponse();
			var spokenCode = string.Join(", ", code.ToCharArray());

			await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VerificationGreeting, department.DepartmentId);
			for (int i = 0; i < 3; i++)
			{
				response.Pause(length: 1);
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VerificationCode(spokenCode), department.DepartmentId);
			}
			response.Pause(length: 1);
			await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VerificationClosing, department.DepartmentId);
			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpGet("InboundVoice")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> InboundVoice([FromQuery] TwilioGatherRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.From))
				return BadRequest();

			// A department's dedicated number (plan D2): its members get the member IVR in that department, anyone else Enhanced AI
			// voice intake (or the "please text" message). Calls to the master number keep the profile-based IVR below.
			var (dedicatedNumber, lookupFailed) = await FindDedicatedNumberAsync(request.To);
			if (dedicatedNumber != null)
				return await DedicatedNumberVoiceAsync(dedicatedNumber, request.CallSid, request.From, passedGate: false);

			// The number table could not be read and the call is not to the master number: it may be a dedicated number, whose menu must never
			// name (or act in) the caller's own department. One message and a hang-up; the master number keeps its profile IVR.
			if (lookupFailed && !IsMasterNumber(request.To))
				return await IvrUnavailableAsync();

			var response = new VoiceResponse();
			UserProfile profile = null;
			profile = await _userProfileService.GetProfileByMobileNumberAsync(request.From.Replace("+", ""));

			if (profile == null)
				profile = await _userProfileService.GetProfileByHomeNumberAsync(request.From.Replace("+", ""));

			// Dispatcher desk (plan D8): no profile, to a department number whose desk takes calls: held for a dispatcher.
			if (profile == null && await _dispatcherInboundVoiceHandler.HandleAsync(request.From, request.To,
				Request?.Query["CallSid"].ToString(), HttpContext?.RequestAborted ?? CancellationToken.None) is string deskTwiml)
				return Content(deskTwiml, "application/xml");

			if (profile != null)
			{
				var department = await _departmentsService.GetDepartmentByUserIdAsync(profile.UserId, false);

				if (department != null)
				{
					var authroized = await _limitsService.CanDepartmentProvisionNumberAsync(department.DepartmentId);

					request.From.Replace("+", "");
					if (authroized)
					{
						for (int repeat = 0; repeat < 2; repeat++)
						{
							var gatherResponse = new Gather(numDigits: 1, action: new Uri(string.Format("{0}/api/Twilio/InboundVoiceAction?userId={1}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, profile.UserId)), method: "GET")
							{
								BargeIn = true
							};
							await AppendVoicePromptsAsync(gatherResponse, BuildMainMenuPrompts(profile.FirstName, department.Name), department.DepartmentId);
							response.Append(gatherResponse);
						}

						response.Hangup();

						return CreateVoiceContentResult(response);
					}
					else
					{
						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable, department.DepartmentId);
						response.Hangup();
					}
				}
				else
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable);
					response.Hangup();
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable);
				response.Hangup();
			}

			return CreateVoiceContentResult(response);
		}

		/// <summary>
		/// A call to a department's dedicated number (plan D2, D6): members get the member IVR in the number's department; others
		/// get Enhanced AI voice intake when the number has it on, the department is entitled and under the monthly limit, else
		/// the "please text this number" message. With IntakeConfig.VoiceGateEnabled (M0326) the caller first presses 1
		/// (<see cref="VoiceIntakeGate"/>), so most auto-dialers and recordings never start a session or use the monthly limit
		/// (one that sends "1" still passes; the sender block list and limits still apply to it).
		/// </summary>
		private async Task<ActionResult> DedicatedNumberVoiceAsync(DepartmentPhoneNumber number, string callSid, string fromNumber, bool passedGate)
		{
			var response = new VoiceResponse();
			VoiceIntakeRouting routing;
			try
			{
				routing = await _voiceIntakeService.RouteAsync(number, fromNumber, DateTime.UtcNow);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Voice intake routing failed.");
				routing = new VoiceIntakeRouting { Route = VoiceIntakeRoutes.TextOnly, DepartmentId = number.DepartmentId };
			}

			Framework.Logging.LogInfo($"[Twilio Voice] CallSid={callSid} dedicated number of department {number.DepartmentId}: route {routing.Route} ({routing.Reason}){(passedGate ? " after the gate" : "")}");

			switch (routing.Route)
			{
				case VoiceIntakeRoutes.MemberMenu:
					var profile = await _userProfileService.GetProfileByUserIdAsync(routing.MemberUserId);
					var department = await _departmentsService.GetDepartmentByIdAsync(number.DepartmentId, false);
					if (profile != null && department != null && await _limitsService.CanDepartmentProvisionNumberAsync(department.DepartmentId))
					{
						for (int repeat = 0; repeat < 2; repeat++)
						{
							// The menu is this department's: every action of it stays in this department (see ResolveIvrDepartmentAsync).
							var gatherResponse = new Gather(numDigits: 1, action: IvrActionUri("InboundVoiceAction", profile.UserId, number.DepartmentId), method: "GET")
							{
								BargeIn = true
							};
							await AppendVoicePromptsAsync(gatherResponse, BuildMainMenuPrompts(profile.FirstName, department.Name), department.DepartmentId);
							response.Append(gatherResponse);
						}

						response.Hangup();
						return CreateVoiceContentResult(response);
					}

					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable, number.DepartmentId);
					response.Hangup();
					return CreateVoiceContentResult(response);

				case VoiceIntakeRoutes.Intake:
					// M0326 gate: "press 1" before any session exists. Nothing pressed = the closing prompt and a hang-up, nothing recorded.
					if (Config.IntakeConfig.VoiceGateEnabled && !passedGate)
					{
						var gate = new Gather(
							input: new List<Gather.InputEnum> { Gather.InputEnum.Dtmf },
							action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceIntakeGate"),
							method: "POST",
							timeout: Math.Clamp(Config.IntakeConfig.VoiceGateTimeoutSeconds, 3, 30),
							numDigits: 1)
						{
							ActionOnEmptyResult = false
						};
						await AppendVoicePromptAsync(gate, TwilioVoicePromptCatalog.VoiceIntakeGate, number.DepartmentId);
						response.Append(gate);
						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VoiceIntakeNothingHeard, number.DepartmentId);
						response.Hangup();
						return CreateVoiceContentResult(response);
					}

					// A session store that fails still answers with TwiML: the caller hears the failure prompt (a null turn) and the line hangs up.
					VoiceIntakeTurn turn;
					try
					{
						turn = await _voiceIntakeService.StartAsync(number, callSid, fromNumber, HttpContext?.RequestAborted ?? CancellationToken.None);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						Framework.Logging.LogException(ex, $"[Twilio Voice] CallSid={callSid} voice intake session start failed.");
						turn = null;
					}
					return await RenderVoiceIntakeTurnAsync(turn, number.DepartmentId);

				case VoiceIntakeRoutes.Blocked:
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VoiceIntakeUnavailable, number.DepartmentId);
					response.Hangup();
					return CreateVoiceContentResult(response);

				case VoiceIntakeRoutes.NumberInactive:
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VoiceIntakeNumberInactive, number.DepartmentId);
					response.Hangup();
					return CreateVoiceContentResult(response);

				default:
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VoiceIntakeTextOnly, number.DepartmentId);
					response.Hangup();
					return CreateVoiceContentResult(response);
			}
		}

		/// <summary>
		/// The voice intake gate's &lt;Gather input="dtmf"&gt; action (M0326). Only "1" goes on, and the call is routed again (a block,
		/// the monthly limit or the add-on may have changed meanwhile) before the guided questions start; anything else hangs up.
		/// </summary>
		[HttpGet("VoiceIntakeGate")]
		[HttpPost("VoiceIntakeGate")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceIntakeGate()
		{
			string Field(string name) => Request.HasFormContentType && Request.Form.ContainsKey(name) ? Request.Form[name].ToString() : Request.Query[name].ToString();

			var callSid = Field("CallSid");
			var from = Field("From");
			var to = Field("To");
			if (string.IsNullOrWhiteSpace(callSid) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
				return BadRequest();

			var number = await DedicatedNumberAsync(to);
			if (number == null)
			{
				var gone = new VoiceResponse();
				await AppendVoicePromptAsync(gone, TwilioVoicePromptCatalog.VoiceIntakeNumberInactive);
				gone.Hangup();
				return CreateVoiceContentResult(gone);
			}

			if (!string.Equals(Field("Digits")?.Trim(), "1", StringComparison.Ordinal))
			{
				Framework.Logging.LogInfo($"[Twilio Voice] CallSid={callSid} dedicated number of department {number.DepartmentId}: the caller did not press 1 at the gate.");
				var closing = new VoiceResponse();
				await AppendVoicePromptAsync(closing, TwilioVoicePromptCatalog.VoiceIntakeNothingHeard, number.DepartmentId);
				closing.Hangup();
				return CreateVoiceContentResult(closing);
			}

			return await DedicatedNumberVoiceAsync(number, callSid, from, passedGate: true);
		}

		/// <summary>
		/// Voice intake answers (&lt;Gather input="speech"&gt; action, POST). <paramref name="step"/> is the question the caller was
		/// asked, so a Twilio retry of an older action never answers the wrong question. Sessions are keyed by CallSid.
		/// </summary>
		[HttpGet("VoiceIntakeAnswer")]
		[HttpPost("VoiceIntakeAnswer")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> VoiceIntakeAnswer([FromQuery] int step)
		{
			string Field(string name) => Request.HasFormContentType && Request.Form.ContainsKey(name) ? Request.Form[name].ToString() : Request.Query[name].ToString();

			var callSid = Field("CallSid");
			if (string.IsNullOrWhiteSpace(callSid))
				return BadRequest();

			double? confidence = double.TryParse(Field("Confidence"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null;
			VoiceIntakeTurn turn;
			try
			{
				turn = await _voiceIntakeService.AnswerAsync(new VoiceIntakeAnswer
				{
					ProviderCallId = callSid,
					FromNumber = Field("From"),
					ToNumber = Field("To"),
					Step = step,
					SpeechResult = Field("SpeechResult"),
					Confidence = confidence,
					Digits = Field("Digits")
				}, HttpContext?.RequestAborted ?? CancellationToken.None);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Framework.Logging.LogException(ex, $"[Twilio Voice] CallSid={callSid} voice intake answer failed.");
				turn = new VoiceIntakeTurn { HangUp = true, Prompts = new List<string> { TwilioVoicePromptCatalog.VoiceIntakeFailed } };
			}

			var number = await DedicatedNumberAsync(Field("To"));
			return await RenderVoiceIntakeTurnAsync(turn, number?.DepartmentId);
		}

		/// <summary>Seconds of silence that end a spoken answer when a speech model is set and IntakeConfig.VoiceSpeechTimeoutSeconds is 0.</summary>
		public const int DefaultModelSpeechTimeoutSeconds = 3;

		/// <summary>
		/// The &lt;Gather&gt; speechTimeout. "auto" (Twilio's own end-of-speech detection) is allowed only without a speechModel: with one,
		/// Twilio requires a positive number of seconds (warning 13335), so 0 then means <see cref="DefaultModelSpeechTimeoutSeconds"/>.
		/// </summary>
		public static string VoiceSpeechTimeout(string speechModel, int configuredSeconds)
		{
			if (configuredSeconds > 0)
				return configuredSeconds.ToString(CultureInfo.InvariantCulture);
			return string.IsNullOrWhiteSpace(speechModel) ? "auto" : DefaultModelSpeechTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// TwiML for one voice intake turn: the question inside a speech &lt;Gather&gt; (the callback question also takes digits),
		/// Piper prompts through the voice response service, or the closing prompt and a hang-up.
		/// </summary>
		private async Task<ActionResult> RenderVoiceIntakeTurnAsync(VoiceIntakeTurn turn, int? departmentId)
		{
			var response = new VoiceResponse();
			if (turn?.Gather == true)
			{
				var action = new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceIntakeAnswer?step={(int)turn.Step}");
				var gather = new Gather(
					input: turn.AcceptDigits
						? new List<Gather.InputEnum> { Gather.InputEnum.Speech, Gather.InputEnum.Dtmf }
						: new List<Gather.InputEnum> { Gather.InputEnum.Speech },
					action: action,
					method: "POST")
				{
					SpeechTimeout = VoiceSpeechTimeout(Config.IntakeConfig.VoiceSpeechModel, Config.IntakeConfig.VoiceSpeechTimeoutSeconds),
					SpeechModel = string.IsNullOrWhiteSpace(Config.IntakeConfig.VoiceSpeechModel) ? null : Config.IntakeConfig.VoiceSpeechModel,
					ActionOnEmptyResult = true,
					ProfanityFilter = false,
					BargeIn = true
				};

				if (turn.AcceptDigits)
					gather.FinishOnKey = "#";

				await AppendVoicePromptsAsync(gather, turn.Prompts, departmentId);
				response.Append(gather);

				// Nothing heard and Twilio fell through the gather: post the same step with no answer, which counts a re-ask.
				response.Redirect(action, "POST");
				return CreateVoiceContentResult(response);
			}

			await AppendVoicePromptsAsync(response, turn?.Prompts ?? new List<string> { TwilioVoicePromptCatalog.VoiceIntakeFailed }, departmentId);
			response.Hangup();
			return CreateVoiceContentResult(response);
		}

		protected virtual void ReportVoiceVerificationDecryptionFailure(CryptographicException exception)
		{
			Framework.Logging.LogException(
				exception,
				"Twilio VoiceVerification failed to decrypt a verification code");
		}

		private async Task<ContentResult> GetVoiceVerificationErrorResult()
		{
			var response = new VoiceResponse();
			await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VoiceVerificationFailure);
			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpGet("InboundVoiceAction")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> InboundVoiceAction(string userId, [FromQuery] VoiceRequest twilioRequest, [FromQuery] string retry = null,
			[FromQuery] int? departmentId = null)
		{
			var response = new VoiceResponse();

			var (department, bound) = await ResolveIvrDepartmentAsync(userId, twilioRequest?.To, departmentId);
			if (department == null)
				return await IvrUnavailableAsync();
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			var prompts = new List<string>();
			var isDynamicListing = false;
			Uri gatherAction = IvrActionUri("InboundVoiceAction", userId, bound);
			string gatherFinishOnKey = null;
			int? gatherNumDigits = 1;
			string goBackPrompt = TwilioVoicePromptCatalog.GoBackToMainMenu;

			if (twilioRequest.Digits == "0")
			{
				prompts.AddRange(BuildMainMenuPrompts(profile.FirstName, department.Name));
			}
			else if (twilioRequest.Digits == "1")
			{
				isDynamicListing = true;
				// Group-scoped dispatch (off by default): read out only the calls in the caller's area or that they're on.
				var calls = await _dispatchScopeService.FilterCallsForUserAsync(department.DepartmentId, userId,
					await _callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId));

				if (calls != null && calls.Any())
				{
					// Join the listing into ONE prompt: the TTS layer chunks long text on
					// sentence boundaries and generates the chunks in parallel, whereas one
					// prompt per call would issue that many sequential TTS round-trips and
					// risk running past Twilio's 15-second webhook limit on big lists.
					var lines = new List<string>
					{
						$"There are {calls.Count()} active calls for department {department.Name}."
					};

					foreach (var call in calls)
					{
						lines.Add($"{call.Name}, Priority {call.GetPriorityText()}. Address {call.Address}. Nature {StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall)}.");
					}

					prompts.Add(string.Join(" ", lines));
				}
				else
				{
					prompts.Add($"There are no active calls for department {department.Name}.");
				}
			}
			else if (twilioRequest.Digits == "2")
			{
				isDynamicListing = true;
				var allUsers = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(department.DepartmentId, false, false, false);
				var lastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(department.DepartmentId);
				var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(department.DepartmentId);

				if (allUsers != null && allUsers.Any())
				{
					// One joined prompt (see the active-calls branch above): chunked and
					// generated in parallel by the TTS layer instead of one sequential
					// round-trip per person.
					var lines = new List<string>();
					var defaultStatus = await _customStateService.GetDefaultPersonnelStatusAsync(department.DepartmentId);

					foreach (var user in allUsers)
					{
						var lastActionLog = lastUserActionlogs.FirstOrDefault(x => x.UserId == user.UserId);
						var userState = userStates.FirstOrDefault(x => x.UserId == user.UserId);
						var staffingLevel = await _customStateService.GetCustomPersonnelStaffingAsync(department.DepartmentId, userState);
						// No status inside the auto-available hour reads as the department's Available status.
						var status = lastActionLog != null
							? await _customStateService.GetCustomPersonnelStatusAsync(department.DepartmentId, lastActionLog)
							: defaultStatus;

						// A user with no recorded action log or state resolves to a null detail;
						// dereferencing it here 500s the webhook and Twilio speaks "application error".
						lines.Add($"{user.LastName}, {user.FirstName}, Status {status?.ButtonText ?? "Unknown"} Staffing Level {staffingLevel?.ButtonText ?? "Unknown"}.");
					}

					prompts.Add(string.Join(" ", lines));
				}
			}
			else if (twilioRequest.Digits == "3")
			{
				isDynamicListing = true;
				var units = await _unitsService.GetUnitsForDepartmentUnlimitedAsync(department.DepartmentId);
				var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(department.DepartmentId);
				var unitStatuses = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(department.DepartmentId);

				if (units != null && units.Any())
				{
					// One joined prompt (see the active-calls branch above).
					var lines = new List<string>();

					foreach (var unit in units)
					{
						// Security > View Units, the same filter the unit lists apply.
						if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, userId, department.DepartmentId))
							continue;

						var unitState = states.FirstOrDefault(x => x.UnitId == unit.UnitId);
						var unitStatus = await _customStateService.GetCustomUnitStateAsync(unitState);

						// Units beyond the plan limit (or with a deleted custom state) have no
						// resolvable state; a null dereference here 500s the webhook and Twilio
						// speaks "application error" mid-menu.
						lines.Add($"{unit.Name}, Status {unitStatus?.ButtonText ?? "Unknown"}.");
					}

					// Every unit may be outside what the caller can view; say so rather than speak an empty prompt.
					prompts.Add(lines.Any() ? string.Join(" ", lines) : $"There are no units for department {department.Name}.");
				}
				else
				{
					prompts.Add($"There are no units for department {department.Name}.");
				}
			}
			else if (twilioRequest.Digits == "4")
			{
				isDynamicListing = true;
				var upcomingItems = await _calendarService.GetUpcomingCalendarItemsAsync(department.DepartmentId, DateTime.UtcNow);

				if (upcomingItems != null && upcomingItems.Any())
				{
					// One joined prompt (see the active-calls branch above). Each line gets a
					// terminal period so the joined text chunks on sentence boundaries.
					var lines = new List<string>();

					foreach (var item in upcomingItems)
					{
						lines.Add($"{item.Title}, {item.Start.TimeConverter(department).ToShortDateString()}, {item.Start.TimeConverter(department).ToShortTimeString()}, {item.Location}.");
					}

					prompts.Add(string.Join(" ", lines));
				}
				else
				{
					prompts.Add($"There are no upcoming Calendar events for department {department.Name}.");
				}
			}
			else if (twilioRequest.Digits == "5")
			{
				prompts.Add($"There are no upcoming shifts for department {department.Name}.");
			}
			else if (twilioRequest.Digits == "6") // Set current status
			{
				var options = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(department.DepartmentId);
				int index = 1;

				prompts.Add(TwilioVoicePromptCatalog.StatusSelectionIntro);

				foreach (var option in VoiceSelectableOptions(options))
				{
					prompts.Add(TwilioVoicePromptCatalog.StatusOption(index, option.ButtonText));
					index++;
				}

				gatherAction = IvrActionUri("InboundVoiceActionStatus", userId, bound);
				gatherFinishOnKey = "#";
				gatherNumDigits = null;
				goBackPrompt = TwilioVoicePromptCatalog.GoBackToMainMenuWithPound;
			}
			else if (twilioRequest.Digits == "7") // Set current staffing
			{
				var options = await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(department.DepartmentId);
				int index = 1;

				prompts.Add(TwilioVoicePromptCatalog.StaffingSelectionIntro);

				foreach (var option in VoiceSelectableOptions(options))
				{
					prompts.Add(TwilioVoicePromptCatalog.StaffingOption(index, option.ButtonText));
					index++;
				}

				gatherAction = IvrActionUri("InboundVoiceActionStaffing", userId, bound);
				gatherFinishOnKey = "#";
				gatherNumDigits = null;
				goBackPrompt = TwilioVoicePromptCatalog.GoBackToMainMenuWithPound;
			}

			// Dynamic listings are unique text and therefore almost always a TTS cache
			// miss; cold generation routinely outlives the shared prompt budget, which
			// used to skip the prompt entirely (silence, then hangup). Same pattern as
			// dispatch playback in VoiceCall: wait briefly for the audio, and if it
			// isn't ready, play a pre-warmed "please wait" and redirect back — the
			// generation keeps running server-side, so the re-entry finds it cached.
			if (isDynamicListing && prompts.Count > 0 && !await IsPromptAudioReadyAsync(prompts[0], department.DepartmentId))
			{
				if (!int.TryParse(retry, out var retryCount))
					retryCount = 0;

				if (retryCount < MAX_LISTING_RETRY)
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.PleaseWaitForInformation, department.DepartmentId);
					response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound, $"&Digits={twilioRequest.Digits}&retry={retryCount + 1}"), "GET");
					return CreateVoiceContentResult(response);
				}

				// Retry budget exhausted — fall through to the normal budgeted append,
				// which degrades to <Say> (when enabled) or skips the prompt.
			}

			for (int repeat = 0; repeat < 2; repeat++)
			{
				var gather = new Gather(action: gatherAction, method: "GET", finishOnKey: gatherFinishOnKey, numDigits: gatherNumDigits)
				{
					BargeIn = true
				};
				await AppendVoicePromptsAsync(gather, prompts, department.DepartmentId);
				await AppendVoicePromptAsync(gather, goBackPrompt, department.DepartmentId);
				response.Append(gather);
			}

			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		/// <summary>
		/// Waits up to <see cref="ListingReadinessTimeout"/> for the TTS audio of the
		/// given text to resolve (cache hit or fast generation). On timeout the
		/// generation continues in TwilioVoiceResponseService's URL cache, so a
		/// redirect re-entry gets an instant hit. TTS hard failures return true — the
		/// normal append path already degrades those to &lt;Say&gt; or a skip.
		/// </summary>
		private async Task<bool> IsPromptAudioReadyAsync(string text, int? departmentId)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);

			using var timeoutCts = new CancellationTokenSource(ListingReadinessTimeout);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
				timeoutCts.Token,
				HttpContext?.RequestAborted ?? CancellationToken.None);

			try
			{
				var scratch = new VoiceResponse();
				await _twilioVoiceResponseService.AppendPromptAsync(scratch, text, linkedCts.Token, ttsLanguage);
				return true;
			}
			catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
			{
				return false;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// TwilioVoiceResponseService already degrades TTS faults to <Say>/skip, so
				// nothing should reach here. Enforce the documented contract locally anyway:
				// a readiness probe must never fail the webhook, and redirecting on a hard
				// failure would just loop. Caller-abort cancellation is deliberately not
				// caught — it is control flow, and the next append rethrows it regardless.
				Logging.LogException(ex);
				return true;
			}
		}

		[HttpGet("InboundVoiceActionStatus")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> InboundVoiceActionStatus(string userId, [FromQuery] VoiceRequest twilioRequest, [FromQuery] int? departmentId = null)
		{
			var response = new VoiceResponse();

			var (department, bound) = await ResolveIvrDepartmentAsync(userId, twilioRequest?.To, departmentId);
			if (department == null)
				return await IvrUnavailableAsync();
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var options = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(department.DepartmentId);
			var activeOptions = VoiceSelectableOptions(options);

			if (!String.IsNullOrWhiteSpace(twilioRequest.Digits))
			{
				if (twilioRequest.Digits == "0")
				{
					response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound), "GET");
				}
				else if (int.TryParse(twilioRequest.Digits, out int digit) && digit > 0 && digit <= activeOptions.Count)
				{
					var selectedOption = activeOptions[digit - 1];
					if (selectedOption != null)
					{
						using (StatusWriteActor.Begin(userId, StatusSetOrigins.Voice))
							await _actionLogsService.SetUserActionAsync(userId, department.DepartmentId, selectedOption.CustomStateDetailId);
						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.StatusMarked(selectedOption.ButtonText), department.DepartmentId);
						response.Hangup();
					}
				}
				else
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidStatusSelection, department.DepartmentId);
					response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound), "GET");
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.NoStatusSelection, department.DepartmentId);
				response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound), "GET");
			}

			return CreateVoiceContentResult(response);
		}

		[HttpGet("InboundVoiceActionStaffing")]
		[Produces("application/xml")]
		[ValidateRequest]
		public async Task<ActionResult> InboundVoiceActionStaffing(string userId, [FromQuery] VoiceRequest twilioRequest, [FromQuery] int? departmentId = null)
		{
			var response = new VoiceResponse();
			var (department, bound) = await ResolveIvrDepartmentAsync(userId, twilioRequest?.To, departmentId);
			if (department == null)
				return await IvrUnavailableAsync();
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var options = await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(department.DepartmentId);
			var activeOptions = VoiceSelectableOptions(options);
			if (!String.IsNullOrWhiteSpace(twilioRequest.Digits))
			{
				if (twilioRequest.Digits == "0")
				{
					response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound), "GET");
				}
				else if (int.TryParse(twilioRequest.Digits, out int digit) && digit > 0 && digit <= activeOptions.Count)
				{
					var selectedOption = activeOptions[digit - 1];
					await _userStateService.CreateUserState(userId, department.DepartmentId, selectedOption.CustomStateDetailId);
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.StaffingMarked(selectedOption.ButtonText), department.DepartmentId);
					response.Hangup();
				}
				else
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidStaffingSelection, department.DepartmentId);
					response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound), "GET");
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.NoStaffingSelection, department.DepartmentId);
				response.Redirect(IvrActionUri("InboundVoiceAction", userId, bound), "GET");
			}

			return CreateVoiceContentResult(response);
		}

		/// <summary>
		/// The status or staffing options the phone menu reads out, in order; the digit a caller presses is a position in this
		/// list, so the menu and the handler must build it the same way. Built-in options keep id 0 (Standing By, Available):
		/// skipping id 0 left a department on the built-in statuses with no way to go available by phone.
		/// </summary>
		private static List<CustomStateDetail> VoiceSelectableOptions(IEnumerable<CustomStateDetail> options)
		{
			return (options ?? Enumerable.Empty<CustomStateDetail>()).Where(o => o != null && !o.IsDeleted).ToList();
		}

		private async System.Threading.Tasks.Task AppendVoicePromptAsync(VoiceResponse response, string text, int? departmentId = null)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);

			try
			{
				await _twilioVoiceResponseService.AppendPromptAsync(response, text, GetTtsPromptBudgetToken(), ttsLanguage);
			}
			catch (OperationCanceledException) when (TtsBudgetExpired)
			{
				_twilioVoiceResponseService.AppendSayFallback(response, text);
			}
		}

		private async System.Threading.Tasks.Task AppendVoicePromptAsync(Gather gather, string text, int? departmentId = null)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);

			try
			{
				await _twilioVoiceResponseService.AppendPromptAsync(gather, text, GetTtsPromptBudgetToken(), ttsLanguage);
			}
			catch (OperationCanceledException) when (TtsBudgetExpired)
			{
				_twilioVoiceResponseService.AppendSayFallback(gather, text);
			}
		}

		// The plural variants append prompt-by-prompt (rather than delegating to the
		// service's AppendPromptsAsync) so a budget expiry mid-list only downgrades
		// the remaining prompts to <Say> — prompts already appended are kept.
		private async System.Threading.Tasks.Task AppendVoicePromptsAsync(VoiceResponse response, IEnumerable<string> prompts, int? departmentId = null)
		{
			foreach (var prompt in prompts)
			{
				await AppendVoicePromptAsync(response, prompt, departmentId);
			}
		}

		private async System.Threading.Tasks.Task AppendVoicePromptsAsync(Gather gather, IEnumerable<string> prompts, int? departmentId = null)
		{
			foreach (var prompt in prompts)
			{
				await AppendVoicePromptAsync(gather, prompt, departmentId);
			}
		}

		private async Task<string> GetDepartmentTtsLanguageAsync(int? departmentId)
		{
			if (!departmentId.HasValue || departmentId.Value <= 0)
				return null;

			var cacheKey = $"twilio-tts-language:{departmentId.Value}";

			if (HttpContext?.Items != null && HttpContext.Items.TryGetValue(cacheKey, out var cachedLanguage))
				return cachedLanguage as string;

			var ttsLanguage = await _departmentSettingsService.GetTtsLanguageForDepartmentAsync(departmentId.Value);

			if (HttpContext?.Items != null)
				HttpContext.Items[cacheKey] = ttsLanguage;

			return ttsLanguage;
		}

		// Returns the dispatch text alongside readiness so the caller's retry branch
		// reuses it instead of re-resolving the address (a second geocoder round-trip).
		// DispatchText is null only when a recorded dispatch-audio attachment was played.
		private async System.Threading.Tasks.Task<(bool Ready, string DispatchText)> TryAppendDispatchPlaybackAsync(VoiceResponse response, Call call)
		{
			if (call.Attachments != null)
			{
				var audio = call.Attachments.FirstOrDefault(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);

				if (audio != null)
				{
					var url = await _callsService.GetShortenedAudioUrlAsync(call.CallId, audio.CallAttachmentId);
					if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var audioUri))
					{
						response.Append(new Play
						{
							Url = audioUri
						});
						return (true, null);
					}
				}
			}

			var address = await ResolveCallAddressAsync(call);
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(call.DepartmentId);
			var dispatchText = BuildDispatchPrompt(call, address);

			// Try to get the TTS URL within 3 seconds. If the audio is cached,
			// the URL returns nearly instantly; if it needs generation, we let
			// the caller fall back to the redirect pattern.
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
				timeoutCts.Token,
				HttpContext?.RequestAborted ?? CancellationToken.None);

			try
			{
				// Dispatch text can exceed the TTS chunk limit (long notes/address), so use the
				// multi-chunk-aware AppendPromptAsync (one <Play> per chunk) instead of GetPromptUrlAsync,
				// which only supports single-chunk text and throws ArgumentException otherwise.
				// The dispatch-rate variant: the broadcast worker pre-warms at DispatchSpeed, and
				// speed is part of the TTS cache key.
				await _twilioVoiceResponseService.AppendDispatchPromptAsync(response, dispatchText, linkedCts.Token, ttsLanguage);
				return (true, dispatchText);
			}
			catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
			{
				// TTS generation is taking too long — return false so the caller
				// can pre-warm in the background and redirect.
				return (false, dispatchText);
			}
		}

		// Address resolution and prompt building are shared with the call broadcast
		// worker (which pre-warms the dispatch TTS audio during ring time) via
		// DispatchVoicePromptBuilder — both sides must produce identical text so the
		// pre-warmed audio's cache key matches what this webhook requests.
		private async Task<string> ResolveCallAddressAsync(Call call)
		{
			return await DispatchVoicePromptBuilder.ResolveDispatchAddressAsync(call, _geoLocationProvider,
				HttpContext?.RequestAborted ?? CancellationToken.None);
		}

		private static string BuildDispatchPrompt(Call call, string address)
		{
			return DispatchVoicePromptBuilder.BuildDispatchPrompt(call, address);
		}

		private static ContentResult CreateVoiceContentResult(VoiceResponse response)
		{
			return new ContentResult
			{
				Content = response.ToString(),
				ContentType = "application/xml",
				StatusCode = 200
			};
		}

		private static IReadOnlyCollection<string> BuildVoiceCallResponseOptionPrompts(IEnumerable<DepartmentGroup> stations)
		{
			var prompts = new List<string>
			{
				TwilioVoicePromptCatalog.OutboundResponseSelectionIntro,
				TwilioVoicePromptCatalog.RespondToSceneOption(1)
			};

			prompts.AddRange(BuildStationOptionPrompts(stations));
			prompts.Add(TwilioVoicePromptCatalog.RepeatDispatchWithPound);

			return prompts;
		}

		private static IReadOnlyCollection<string> BuildStationOptionPrompts(IEnumerable<DepartmentGroup> stations)
		{
			var prompts = new List<string>();
			var index = 2;

			foreach (var station in stations)
			{
				prompts.Add(TwilioVoicePromptCatalog.RespondToStationOption(index, station.Name));
				index++;
			}

			return prompts;
		}

		private static IReadOnlyCollection<string> BuildMainMenuPrompts(string firstName, string departmentName)
		{
			return new[]
			{
				TwilioVoicePromptCatalog.MainMenuGreeting(firstName, departmentName),
				TwilioVoicePromptCatalog.MainMenuSelectionIntro,
				TwilioVoicePromptCatalog.MainMenuActiveCalls,
				TwilioVoicePromptCatalog.MainMenuUserStatuses,
				TwilioVoicePromptCatalog.MainMenuUnitStatuses,
				TwilioVoicePromptCatalog.MainMenuCalendarEvents,
				TwilioVoicePromptCatalog.MainMenuShifts,
				TwilioVoicePromptCatalog.MainMenuSetStatus,
				TwilioVoicePromptCatalog.MainMenuSetStaffing
			};
		}

		/// <summary>
		/// Builds the call for an inbound Text-To-Call message. When the department has a
		/// Text-To-Call import format configured the message is run through that call
		/// template, which is what lets the text supply values like the Call Type and Call
		/// Priority. Without a configured format, or when the template can't make a call out
		/// of the message, the text is imported as the nature of the call the way
		/// Text-To-Call has always done it.
		/// </summary>
		private async Task<Call> BuildTextToCallAsync(Department department, TextMessage textMessage,
			List<Resgrid.Model.Identity.IdentityUser> users)
		{
			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(department.DepartmentId);

			// The department default, not a hardcoded High. High is priority identifier 2,
			// which for a department running Custom Call Priorities is either the wrong
			// priority or one the department doesn't own at all, and dispatch then has no
			// priority to resolve for tones, colors and the notify-all flags.
			// The default priority, else the department's High priority, else the built-in High (CallPriorityMatch).
			int defaultPriority = CallPriorityMatch.DefaultForImport(priorities);

			Call call = null;
			var formatType = await _departmentSettingsService.GetTextToCallImportFormatForDepartmentAsync(department.DepartmentId);

			if (formatType.HasValue && Enum.IsDefined(typeof(CallEmailTypes), formatType.Value))
			{
				var callEmail = new CallEmail();
				callEmail.MessageId = textMessage.MessageId;
				callEmail.Subject = String.Format("TTC {0}", DateTime.UtcNow.TimeConverter(department).ToString("g"));
				callEmail.Body = textMessage.Text;
				callEmail.TextBody = textMessage.Text;

				var activeCalls = await _callsService.GetLatest10ActiveCallsByDepartmentAsync(department.DepartmentId);
				var units = await _unitsService.GetUnitsForDepartmentAsync(department.DepartmentId);
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(department.DepartmentId);

				call = await _callsService.GenerateCallFromEmail(formatType.Value, callEmail, department.ManagingUserId, users,
					department, activeCalls, units, defaultPriority, priorities, callTypes);
			}

			if (call == null)
			{
				call = new Call();
				call.Notes = textMessage.Text;
				call.NatureOfCall = textMessage.Text;
				call.ReportingUserId = department.ManagingUserId;
				call.SourceIdentifier = textMessage.MessageId;
				call.Priority = defaultPriority;
				call.Dispatches = new Collection<CallDispatch>();

				foreach (var u in users)
				{
					var cd = new CallDispatch();
					cd.UserId = u.UserId;

					call.Dispatches.Add(cd);
				}
			}

			if (call.LoggedOn == DateTime.MinValue)
				call.LoggedOn = DateTime.UtcNow;

			if (String.IsNullOrWhiteSpace(call.Name))
				call.Name = String.Format("TTC {0}", call.LoggedOn.TimeConverter(department).ToString("g"));

			call.CallSource = (int)CallSources.EmailImport;
			call.DepartmentId = department.DepartmentId;

			return call;
		}

	}

	/// <summary>
	/// An inbound SMS webhook (query string for the master number's GET, form for a dedicated number's POST). Deliberately not a
	/// Twilio.AspNet.Common TwilioRequest: that library marks To/From/AccountSid non-nullable, so [ApiController]'s automatic
	/// validation answered 400 to a dedicated number's form POST (empty query) before IncomingMessage could read the form.
	/// This project has no nullable context, so these fields are optional to model validation; IncomingMessage checks them itself.
	/// </summary>
	[Serializable]
	public class TwilioMessage
	{
		public string AccountSid { get; set; }
		public string From { get; set; }
		public string To { get; set; }
		public string MessageSid { get; set; }
		public string SmsMessageSid { get; set; }
		public string Body { get; set; }
	}
}
