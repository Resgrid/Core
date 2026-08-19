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

	public TwilioController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
		ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
		IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
		IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
		IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
		IUsersService usersService, ICalendarService calendarService, ICommunicationTestService communicationTestService,
		IEncryptionService encryptionService, ITwilioVoiceResponseService twilioVoiceResponseService,
		IFeatureToggleService featureToggleService, ITextDepartmentSwitchService textDepartmentSwitchService,
		IDispatchRecommendationService dispatchRecommendationService)
	{
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
		[Produces("application/xml")]
		public async Task<ActionResult> IncomingMessage([FromQuery] TwilioMessage request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.Body))
				return BadRequest();

			var response = new MessagingResponse();

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

					if (isDispatchSource)
					{
						var c = new Call();
						c.Notes = textMessage.Text;
						c.NatureOfCall = textMessage.Text;
						c.LoggedOn = DateTime.UtcNow;
						c.Name = string.Format("TTC {0}", c.LoggedOn.TimeConverter(department).ToString("g"));
						c.Priority = (int)CallPriority.High;
						c.ReportingUserId = department.ManagingUserId;
						c.Dispatches = new Collection<CallDispatch>();
						c.CallSource = (int)CallSources.EmailImport;
						c.SourceIdentifier = textMessage.MessageId;
						c.DepartmentId = departmentId.Value;

						var users = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId.Value, true);
						foreach (var u in users)
						{
							var cd = new CallDispatch();
							cd.UserId = u.UserId;

							c.Dispatches.Add(cd);
						}

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

					if (!isDispatchSource)
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
							var payload = _textCommandService.DetermineType(textMessage.Text);

							// Diagnostic: which command the inbound text resolved to (None still replies with a hint).
							Framework.Logging.LogInfo($"[Twilio SMS] DepartmentId={departmentId.Value} UserId={profile.UserId} resolved text command={payload.Type}");
							var customActions = customStates.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Personnel);
							var customStaffing = customStates.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Staffing);

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
										help.Append("available or 4: Available" + Environment.NewLine);
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
										help.Append("onshift or s4: On Shift Staffing" + Environment.NewLine);
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

									var customStatusLevel = await _customStateService.GetCustomPersonnelStatusAsync(department.DepartmentId, userStatus);
									var customStaffingLevel = await _customStateService.GetCustomPersonnelStaffingAsync(department.DepartmentId, userStaffing);

									response.Message(
										$"Hello {profile.FullName.AsFirstNameLastName} at {DateTime.UtcNow.TimeConverterToString(department)} your current status is {customStatusLevel.ButtonText} and your current staffing is {customStaffingLevel.ButtonText}.");
									break;
								case TextCommandTypes.Calls:
									messageEvent.Processed = true;

									var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId);

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
										var unitState = await _customStateService.GetCustomUnitStateAsync(unit);
										unitStatusesText.Append($"{unit.Unit.Name} is {unitState.ButtonText}" + Environment.NewLine);
									}

									response.Message(unitStatusesText.ToString().Truncate(1200));
									break;
								case TextCommandTypes.CallDetail:
									messageEvent.Processed = true;

									var call = await _callsService.GetCallByIdAsync(int.Parse(payload.Data));

									// Guard against a missing call (NRE) and against reading a call that belongs
									// to another department (cross-department data leakage).
									if (call == null || call.DepartmentId != department.DepartmentId)
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
		public async Task<ActionResult> VoiceCall(string userId, int callId, [FromQuery] string retry = null)
		{
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
					_twilioVoiceResponseService.PreWarmPromptAsync(dispatchText, ttsLanguage)
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
					var nextRetry = retryCount + 1;
					response.Redirect(
						new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}&retry={nextRetry}"),
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
				await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, (int)ActionTypes.RespondingToScene, null, call.CallId);
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
						await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, (int)ActionTypes.RespondingToStation, null, station.DepartmentGroupId);
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

			var response = new VoiceResponse();
			UserProfile profile = null;
			profile = await _userProfileService.GetProfileByMobileNumberAsync(request.From.Replace("+", ""));

			if (profile == null)
				profile = await _userProfileService.GetProfileByHomeNumberAsync(request.From.Replace("+", ""));

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
		public async Task<ActionResult> InboundVoiceAction(string userId, [FromQuery] VoiceRequest twilioRequest, [FromQuery] string retry = null)
		{
			var response = new VoiceResponse();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			var prompts = new List<string>();
			var isDynamicListing = false;
			Uri gatherAction = new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}");
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
				var calls = await _callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId);

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

					foreach (var user in allUsers)
					{
						var lastActionLog = lastUserActionlogs.FirstOrDefault(x => x.UserId == user.UserId);
						var userState = userStates.FirstOrDefault(x => x.UserId == user.UserId);
						var staffingLevel = await _customStateService.GetCustomPersonnelStaffingAsync(department.DepartmentId, userState);
						var status = await _customStateService.GetCustomPersonnelStatusAsync(department.DepartmentId, lastActionLog);

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
						var unitState = states.FirstOrDefault(x => x.UnitId == unit.UnitId);
						var unitStatus = await _customStateService.GetCustomUnitStateAsync(unitState);

						// Units beyond the plan limit (or with a deleted custom state) have no
						// resolvable state; a null dereference here 500s the webhook and Twilio
						// speaks "application error" mid-menu.
						lines.Add($"{unit.Name}, Status {unitStatus?.ButtonText ?? "Unknown"}.");
					}

					prompts.Add(string.Join(" ", lines));
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

				foreach (var option in options)
				{
					if (option.CustomStateDetailId == 0 || option.IsDeleted)
						continue;

					prompts.Add(TwilioVoicePromptCatalog.StatusOption(index, option.ButtonText));
					index++;
				}

				gatherAction = new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceActionStatus?userId={userId}");
				gatherFinishOnKey = "#";
				gatherNumDigits = null;
				goBackPrompt = TwilioVoicePromptCatalog.GoBackToMainMenuWithPound;
			}
			else if (twilioRequest.Digits == "7") // Set current staffing
			{
				var options = await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(department.DepartmentId);
				int index = 1;

				prompts.Add(TwilioVoicePromptCatalog.StaffingSelectionIntro);

				foreach (var option in options)
				{
					if (option.CustomStateDetailId == 0 || option.IsDeleted)
						continue;

					prompts.Add(TwilioVoicePromptCatalog.StaffingOption(index, option.ButtonText));
					index++;
				}

				gatherAction = new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceActionStaffing?userId={userId}");
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
					response.Redirect(
						new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}&Digits={twilioRequest.Digits}&retry={retryCount + 1}"),
						"GET");
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
		}

		[HttpGet("InboundVoiceActionStatus")]
		[Produces("application/xml")]
		public async Task<ActionResult> InboundVoiceActionStatus(string userId, [FromQuery] VoiceRequest twilioRequest)
		{
			var response = new VoiceResponse();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var options = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(department.DepartmentId);
			var activeOptions = options.Where(o => o.CustomStateDetailId > 0 && !o.IsDeleted).ToList();

			if (!String.IsNullOrWhiteSpace(twilioRequest.Digits))
			{
				if (twilioRequest.Digits == "0")
				{
					response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), "GET");
				}
				else if (int.TryParse(twilioRequest.Digits, out int digit) && digit > 0 && digit <= activeOptions.Count)
				{
					var selectedOption = activeOptions[digit - 1];
					if (selectedOption != null && selectedOption.CustomStateDetailId > 0 && !selectedOption.IsDeleted)
					{
						await _actionLogsService.SetUserActionAsync(userId, department.DepartmentId, selectedOption.CustomStateDetailId);
						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.StatusMarked(selectedOption.ButtonText), department.DepartmentId);
						response.Hangup();
					}
				}
				else
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidStatusSelection, department.DepartmentId);
					response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), "GET");
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.NoStatusSelection, department.DepartmentId);
				response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), "GET");
			}

			return CreateVoiceContentResult(response);
		}

		[HttpGet("InboundVoiceActionStaffing")]
		[Produces("application/xml")]
		public async Task<ActionResult> InboundVoiceActionStaffing(string userId, [FromQuery] VoiceRequest twilioRequest)
		{
			var response = new VoiceResponse();
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var options = await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(department.DepartmentId);
			var activeOptions = options.Where(o => o.CustomStateDetailId > 0 && !o.IsDeleted).ToList();
			if (!String.IsNullOrWhiteSpace(twilioRequest.Digits))
			{
				if (twilioRequest.Digits == "0")
				{
					response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), "GET");
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
					response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), "GET");
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.NoStaffingSelection, department.DepartmentId);
				response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), "GET");
			}

			return CreateVoiceContentResult(response);
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
				await _twilioVoiceResponseService.AppendPromptAsync(response, dispatchText, linkedCts.Token, ttsLanguage);
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
	}

	[Serializable]
	public class TwilioMessage : TwilioRequest
	{
		public string MessageSid { get; set; }
		public string SmsMessageSid { get; set; }
		public string Body { get; set; }
	}
}
