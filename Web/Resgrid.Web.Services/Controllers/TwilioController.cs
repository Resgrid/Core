using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
	private readonly IChatbotIngressService _chatbotIngressService;

	public TwilioController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
		ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
		IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
		IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
		IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
		IUsersService usersService, ICalendarService calendarService, ICommunicationTestService communicationTestService,
		IEncryptionService encryptionService, ITwilioVoiceResponseService twilioVoiceResponseService,
		IChatbotIngressService chatbotIngressService)
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
		_chatbotIngressService = chatbotIngressService;
	}
		#endregion Private Readonly Properties and Constructors

		private const int MAX_DISPATCH_RETRY = 3;

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
				// Use the chatbot ingress service for all text command processing
				var chatbotMessage = new ChatbotMessage
				{
					MessageId = request.MessageSid ?? Guid.NewGuid().ToString("N"),
					From = request.From?.Replace("+", ""),
					To = request.To?.Replace("+", ""),
					Text = request.Body,
					Platform = ChatbotPlatform.SmsTwilio,
					Timestamp = DateTime.UtcNow
				};

				var chatbotResponse = await _chatbotIngressService.ProcessMessageAsync(chatbotMessage);

				if (chatbotResponse.Processed)
					messageEvent.Processed = true;

				response.Message(chatbotResponse.Text);
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

			return new ContentResult
			{
				Content = response.ToString(),
				ContentType = "application/xml",
				StatusCode = 200
			};
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

			// For outbound calls, allow a brief pause for the audio bridge to
			// stabilize after the callee answers before attempting playback.
			response.Pause(length: 1);

			// For dispatch playback, attempt to fetch (or pre-warm) the TTS URL
			// within a short timeout so that the TwiML response is returned before
			// Twilio's 15-second webhook timeout expires. If the dispatch text is
			// not yet cached, we play a brief "please wait" prompt and redirect
			// back to this endpoint, giving the TTS service time to complete
			// generation in the background.
			var dispatchReady = await TryAppendDispatchPlaybackAsync(response, call);
			if (!dispatchReady)
			{
				// Parse and increment the retry counter from the incoming request.
				if (!int.TryParse(retry, out var retryCount))
					retryCount = 0;

				if (retryCount >= MAX_DISPATCH_RETRY)
				{
					// Exceeded retry cap — fall back to a static error prompt.
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosed);
					response.Hangup();
					return CreateVoiceContentResult(response);
				}

				// Dispatch audio isn't ready yet. Pre-warm it in the background
				// and redirect back — by the time Twilio re-fetches this endpoint
				// the audio should be cached.
				var address = await ResolveCallAddressAsync(call);
				var dispatchText = BuildDispatchPrompt(call, address);
				var ttsLanguage = await GetDepartmentTtsLanguageAsync(call.DepartmentId);

				// Fire off TTS generation in the background. The TTS microservice
				// caches the result, so the redirect will find it once ready.
				_twilioVoiceResponseService.PreWarmPromptAsync(dispatchText, ttsLanguage)
					.ContinueWith(t =>
					{
						if (t.IsFaulted && t.Exception != null)
							Logging.LogException(t.Exception);
					}, TaskContinuationOptions.OnlyOnFaulted);

				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.PleaseWaitForDispatch);
				var nextRetry = retryCount + 1;
				response.Redirect(
					new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/VoiceCall?userId={userId}&callId={callId}&retry={nextRetry}"),
					"GET");
				return CreateVoiceContentResult(response);
			}

			// Dispatch is ready (fast path or retry with cached audio).
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
				Framework.Logging.LogException(ex);
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

		private async Task<ContentResult> GetVoiceVerificationErrorResult()
		{
			var response = new VoiceResponse();
			await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.VoiceVerificationFailure);
			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpGet("InboundVoiceAction")]
		[Produces("application/xml")]
		public async Task<ActionResult> InboundVoiceAction(string userId, [FromQuery] VoiceRequest twilioRequest)
		{
			var response = new VoiceResponse();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			var prompts = new List<string>();
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
				var calls = await _callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId);

				if (calls != null && calls.Any())
				{
					prompts.Add($"There are {calls.Count()} active calls for department {department.Name}.");

					foreach (var call in calls)
					{
						prompts.Add($"{call.Name}, Priority {call.GetPriorityText()} Address {call.Address} Nature {StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall)}.");
					}
				}
				else
				{
					prompts.Add($"There are no active calls for department {department.Name}.");
				}
			}
			else if (twilioRequest.Digits == "2")
			{
				var allUsers = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(department.DepartmentId, false, false, false);
				var lastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(department.DepartmentId);
				var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(department.DepartmentId);

				if (allUsers != null && allUsers.Any())
				{
					foreach (var user in allUsers)
					{
						var lastActionLog = lastUserActionlogs.FirstOrDefault(x => x.UserId == user.UserId);
						var userState = userStates.FirstOrDefault(x => x.UserId == user.UserId);
						var staffingLevel = await _customStateService.GetCustomPersonnelStaffingAsync(department.DepartmentId, userState);
						var status = await _customStateService.GetCustomPersonnelStatusAsync(department.DepartmentId, lastActionLog);

						prompts.Add($"{user.LastName}, {user.FirstName}, Status {status.ButtonText} Staffing Level {staffingLevel.ButtonText}.");
					}
				}
			}
			else if (twilioRequest.Digits == "3")
			{
				var units = await _unitsService.GetUnitsForDepartmentUnlimitedAsync(department.DepartmentId);
				var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(department.DepartmentId);
				var unitStatuses = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(department.DepartmentId);

				if (units != null && units.Any())
				{
					foreach (var unit in units)
					{
						var unitState = states.FirstOrDefault(x => x.UnitId == unit.UnitId);
						var unitStatus = await _customStateService.GetCustomUnitStateAsync(unitState);

						prompts.Add($"{unit.Name}, Status {unitStatus.ButtonText}.");
					}
				}
				else
				{
					prompts.Add($"There are no units for department {department.Name}.");
				}
			}
			else if (twilioRequest.Digits == "4")
			{
				var upcomingItems = await _calendarService.GetUpcomingCalendarItemsAsync(department.DepartmentId, DateTime.UtcNow);

				if (upcomingItems != null && upcomingItems.Any())
				{
					foreach (var item in upcomingItems)
					{
						prompts.Add($"{item.Title}, {item.Start.TimeConverter(department).ToShortDateString()}, {item.Start.TimeConverter(department).ToShortTimeString()}, {item.Location}");
					}
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
			await _twilioVoiceResponseService.AppendPromptAsync(response, text, HttpContext?.RequestAborted ?? CancellationToken.None, ttsLanguage);
		}

		private async System.Threading.Tasks.Task AppendVoicePromptAsync(Gather gather, string text, int? departmentId = null)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);
			await _twilioVoiceResponseService.AppendPromptAsync(gather, text, HttpContext?.RequestAborted ?? CancellationToken.None, ttsLanguage);
		}

		private async System.Threading.Tasks.Task AppendVoicePromptsAsync(VoiceResponse response, IEnumerable<string> prompts, int? departmentId = null)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);
			await _twilioVoiceResponseService.AppendPromptsAsync(response, prompts, HttpContext?.RequestAborted ?? CancellationToken.None, ttsLanguage);
		}

		private async System.Threading.Tasks.Task AppendVoicePromptsAsync(Gather gather, IEnumerable<string> prompts, int? departmentId = null)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);
			await _twilioVoiceResponseService.AppendPromptsAsync(gather, prompts, HttpContext?.RequestAborted ?? CancellationToken.None, ttsLanguage);
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

		private async System.Threading.Tasks.Task<bool> TryAppendDispatchPlaybackAsync(VoiceResponse response, Call call)
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
						return true;
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
				var url = await _twilioVoiceResponseService.GetPromptUrlAsync(dispatchText, ttsLanguage, linkedCts.Token);
				response.Append(new Play { Url = url });
				return true;
			}
			catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
			{
				// TTS generation is taking too long — return false so the caller
				// can pre-warm in the background and redirect.
				return false;
			}
		}

		private async Task<string> ResolveCallAddressAsync(Call call)
		{
			var address = call.Address;

			if (String.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Length > 1)
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
						address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
				}
				catch
				{
				}
			}

			return String.IsNullOrWhiteSpace(address) ? call.Address : address;
		}

		private static string BuildDispatchPrompt(Call call, string address)
		{
			var nature = StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall);
			var prompt = !String.IsNullOrWhiteSpace(address)
				? string.Format("{0}, Priority {1} Address {2} Nature {3}", call.Name, call.GetPriorityText(), address, nature)
				: string.Format("{0}, Priority {1} Nature {2}", call.Name, call.GetPriorityText(), nature);

			return prompt.EndsWith(".", StringComparison.Ordinal) || prompt.EndsWith("!", StringComparison.Ordinal) || prompt.EndsWith("?", StringComparison.Ordinal)
				? prompt
				: $"{prompt}.";
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
