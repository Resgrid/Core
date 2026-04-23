using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models;
using Resgrid.Web.Services.Twilio;
using Twilio.AspNet.Common;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Web.Services.Controllers
{
	//[EnableCors("_resgridWebsiteAllowSpecificOrigins")]
	public class TwilioProviderController : ControllerBase
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
		private readonly ICommunicationTestService _communicationTestService;
		private readonly ITwilioVoiceResponseService _twilioVoiceResponseService;

		public TwilioProviderController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
			IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
			ICommunicationTestService communicationTestService, ITwilioVoiceResponseService twilioVoiceResponseService)
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
			_communicationTestService = communicationTestService;
			_twilioVoiceResponseService = twilioVoiceResponseService;
		}
		#endregion Private Readonly Properties and Constructors

		[HttpGet]
		[Produces("application/xml")]
		public async Task<ActionResult> IncomingMessage([FromQuery]TwilioMessage request)
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
				var departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(textMessage.To);

				if (departmentId.HasValue)
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value);
					var textToCallEnabled = await _departmentSettingsService.GetDepartmentIsTextCallImportEnabledAsync(departmentId.Value);
					var textCommandEnabled = await _departmentSettingsService.GetDepartmentIsTextCommandEnabledAsync(departmentId.Value);
					var dispatchNumbers = await _departmentSettingsService.GetTextToCallSourceNumbersForDepartmentAsync(departmentId.Value);
					var authroized = await _limitsService.CanDepartmentProvisionNumberAsync(departmentId.Value);
					var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId.Value);

					messageEvent.CustomerId = departmentId.Value.ToString();

					if (authroized)
					{
						bool isDispatchSource = false;

						if (!String.IsNullOrWhiteSpace(dispatchNumbers))
							isDispatchSource = _numbersService.DoesNumberMatchAnyPattern(dispatchNumbers.Split(Char.Parse(",")).ToList(), textMessage.Msisdn);

						// If we don't have dispatchNumbers and Text Command isn't enabled it's a dispatch text
						if (!isDispatchSource && !textCommandEnabled)
							isDispatchSource = true;

						if (isDispatchSource && textToCallEnabled)
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

							var cqi = new CallQueueItem();
							cqi.Call = savedCall;
							cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(users.Select(x => x.UserId).ToList());
							cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(cqi.Call.DepartmentId);

							_queueService.EnqueueCallBroadcastAsync(cqi);

							messageEvent.Processed = true;
						}

						if (!isDispatchSource && textCommandEnabled)
						{
							var profile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);

							if (profile != null)
							{
								var payload = _textCommandService.DetermineType(textMessage.Text);
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

										if (customActions != null && customActions.IsDeleted == false && customActions.GetActiveDetails() != null && customActions.GetActiveDetails().Any() && customActions.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == payload.GetCustomActionType()) != null)
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

										if (customStaffing != null && customStaffing.IsDeleted == false && customStaffing.GetActiveDetails() != null && customStaffing.GetActiveDetails().Any() && customStaffing.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == payload.GetCustomStaffingType()) != null)
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

										response.Message($"Hello {profile.FullName.AsFirstNameLastName} at {DateTime.UtcNow.TimeConverterToString(department)} your current status is {customStatusLevel.ButtonText} and your current staffing is {customStaffingLevel.ButtonText}.");
										break;
									case TextCommandTypes.Calls:
										messageEvent.Processed = true;

										var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId);

										var activeCallText = new StringBuilder();
										activeCallText.Append($"Active Calls for {department.Name}" + Environment.NewLine);
										activeCallText.Append("---------------------" + Environment.NewLine);

										foreach (var activeCall in activeCalls)
										{
											activeCallText.Append($"CallId: {activeCall.CallId} Name: {activeCall.Name} Nature:{activeCall.NatureOfCall}" + Environment.NewLine);
										}


										response.Message(activeCallText.ToString());
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

										response.Message(unitStatusesText.ToString());
										break;
									case TextCommandTypes.CallDetail:
										messageEvent.Processed = true;

										var call = await _callsService.GetCallByIdAsync(int.Parse(payload.Data));

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
										else if (!string.IsNullOrEmpty(call.GeoLocationData))
										{
											try
											{
												string[] points = call.GeoLocationData.Split(char.Parse(","));

												if (points != null && points.Length == 2)
												{
													callText.Append(_geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1])) + Environment.NewLine);
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
				}
				else if (textMessage.To == "17753765253") // Resgrid master text number
				{
					var profile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);
					var payload = _textCommandService.DetermineType(textMessage.Text);

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
							help.Append("This is the Resgrid system for first responders (https://resgrid.com) automated text system. Your department isn't signed up for inbound text messages, but you can send the following commands." + Environment.NewLine);
							help.Append("---------------------" + Environment.NewLine);
							help.Append("STOP: To turn off all text messages" + Environment.NewLine);
							help.Append("HELP: This help text" + Environment.NewLine);

							response.Message(help.ToString());

							break;
						case TextCommandTypes.Stop:
							messageEvent.Processed = true;
							await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId);
							response.Message("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
							break;
					}
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
			finally
			{
				await _numbersService.SaveInboundMessageEventAsync(messageEvent);
			}

			//Ok();

			//var response = new TwilioResponse();

			//return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
			return Ok(new StringContent(response.ToString(), Encoding.UTF8, "application/xml"));
		}

		[HttpGet]
		[Produces("application/xml")]
		public async Task<ActionResult> VoiceCall(string userId, int callId)
		{
			var response = new VoiceResponse();
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosed);
				response.Hangup();
				//return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
				return CreateVoiceContentResult(response);
			}

			if (call.State == (int)CallStates.Cancelled || call.State == (int)CallStates.Closed || call.IsDeleted)
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.CallClosedByNumber(call.Number), call.DepartmentId);
				response.Hangup();
				//return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
				return CreateVoiceContentResult(response);
			}

			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(call.DepartmentId);

			string address = call.Address;
			if (String.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(call.GeoLocationData))
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
					{
						address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
				}
				catch { }
			}

			if (String.IsNullOrWhiteSpace(address) && !String.IsNullOrWhiteSpace(call.Address))
				address = call.Address;

			var prompts = new List<string>
			{
				!String.IsNullOrWhiteSpace(address)
					? $"{call.Name}, Priority {call.GetPriorityText()} Address {call.Address} Nature {call.NatureOfCall}"
					: $"{call.Name}, Priority {call.GetPriorityText()} Nature {call.NatureOfCall}",
				TwilioVoicePromptCatalog.RepeatAndRespondToScene
			};

			for (int i = 0; i < stations.Count && i < 8; i++)
			{
				prompts.Add(TwilioVoicePromptCatalog.RespondToStationOption(i + 2, stations[i].Name));
			}

			for (int repeat = 0; repeat < 2; repeat++)
			{
				var gather = new Gather(numDigits: 1, action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/TwilioProvider/VoiceCallAction/{userId}/{callId}"), method: "GET")
				{
					BargeIn = true
				};
				await AppendVoicePromptsAsync(gather, prompts, call.DepartmentId);
				response.Append(gather);
			}

			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpGet]
		public async Task<ActionResult> VoiceCallAction(string userId, int callId, [FromQuery]TwilioGatherRequest twilioRequest)
		{
			var response = new VoiceResponse();

			if (twilioRequest.Digits == "0")
				response.Redirect(new Uri(string.Format("{0}/Twilio/VoiceCall?userId={1}&callId={2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId)), "GET");
			else if (twilioRequest.Digits == "1")
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, (int)ActionTypes.RespondingToScene, null, call.CallId);

				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.RespondingToScene, call.DepartmentId);
				response.Hangup();
			}
			else
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(call.DepartmentId);

				int index = int.Parse(twilioRequest.Digits) - 2;

				if (index >= 0 && index < 8)
				{
					var station = stations[index];

					if (station != null)
					{
						await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, (int)ActionTypes.RespondingToStation, null,
							station.DepartmentGroupId);

						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.RespondingToStation(station.Name), call.DepartmentId);
						response.Hangup();
					}

				}
			}

			//return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
			return CreateVoiceContentResult(response);
		}

		[HttpGet]
		public async Task<ActionResult> InboundVoice([FromQuery]TwilioGatherRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.From))
				return BadRequest();

			var response = new VoiceResponse();
			var departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(request.To.Replace("+", ""));

			if (departmentId.HasValue)
			{
				var authroized = await _limitsService.CanDepartmentProvisionNumberAsync(departmentId.Value);


				request.From.Replace("+", "");
				if (authroized)
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value, false);

					UserProfile profile = null;
					profile = await _userProfileService.GetProfileByMobileNumberAsync(request.From.Replace("+", ""));

					if (profile == null)
						profile = await _userProfileService.GetProfileByHomeNumberAsync(request.From.Replace("+", ""));

					if (department != null && profile != null)
					{
						await AppendVoicePromptsAsync(response, new[]
						{
							TwilioVoicePromptCatalog.MainMenuGreeting(profile.FirstName, department.Name),
							TwilioVoicePromptCatalog.MainMenuSelectionIntro,
							TwilioVoicePromptCatalog.MainMenuActiveCalls,
							TwilioVoicePromptCatalog.MainMenuUserStatuses,
							TwilioVoicePromptCatalog.MainMenuUnitStatuses,
							TwilioVoicePromptCatalog.MainMenuCalendarEvents,
							TwilioVoicePromptCatalog.MainMenuShifts
						}, department.DepartmentId);
					}
					else
					{
						await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable, departmentId.Value);
						response.Hangup();
					}
				}
				else
				{
					await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable, departmentId.Value);
					response.Hangup();
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InboundVoiceUnavailable);
				response.Hangup();
			}

			//return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
			return CreateVoiceContentResult(response);
		}

		private async System.Threading.Tasks.Task AppendVoicePromptAsync(VoiceResponse response, string text, int? departmentId = null)
		{
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(departmentId);
			await _twilioVoiceResponseService.AppendPromptAsync(response, text, HttpContext?.RequestAborted ?? CancellationToken.None, ttsLanguage);
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

			var cacheKey = $"twilio-provider-tts-language:{departmentId.Value}";

			if (HttpContext?.Items != null && HttpContext.Items.TryGetValue(cacheKey, out var cachedLanguage))
				return cachedLanguage as string;

			var ttsLanguage = await _departmentSettingsService.GetTtsLanguageForDepartmentAsync(departmentId.Value);

			if (HttpContext?.Items != null)
				HttpContext.Items[cacheKey] = ttsLanguage;

			return ttsLanguage;
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
	}

	//[Serializable]
	//public class TwilioMessage : TwilioRequest
	//{
	//	public string MessageSid { get; set; }
	//	public string SmsMessageSid { get; set; }
	//	public string Body { get; set; }
	//}
}
