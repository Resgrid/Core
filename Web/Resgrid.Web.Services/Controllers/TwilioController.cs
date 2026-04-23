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

		public TwilioController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
			IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
			IUsersService usersService, ICalendarService calendarService, ICommunicationTestService communicationTestService,
			IEncryptionService encryptionService, ITwilioVoiceResponseService twilioVoiceResponseService)
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
		}

		#endregion Private Readonly Properties and Constructors

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
				UserProfile userProfile = null;
				var departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(textMessage.To);

				if (!departmentId.HasValue)
				{
					userProfile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);

					if (userProfile != null)
					{
						var department = await _departmentsService.GetDepartmentByUserIdAsync(userProfile.UserId);

						if (department != null)
							departmentId = department.DepartmentId;
					}
				}

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
				else if (textMessage.To == Config.NumberProviderConfig.TwilioResgridNumber) // Resgrid master text number
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
							help.Append("This is the Resgrid system for first responders (https://resgrid.com) automated text system. Your department isn't signed up for inbound text messages, but you can send the following commands." +
										Environment.NewLine);
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
		public async Task<ActionResult> VoiceCall(string userId, int callId)
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

			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(call.DepartmentId);

			if (call.Attachments != null &&
				call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio) > 0)
			{
				var audio = call.Attachments.FirstOrDefault(x =>
					x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);

				if (audio != null)
				{
					var url = await _callsService.GetShortenedAudioUrlAsync(call.CallId, audio.CallAttachmentId);

					Play playResponse = new Play();
					playResponse.Url = new Uri(url);

					StringBuilder sb1 = new StringBuilder();
					sb1.Append("Press 0 to repeat, Press 1 to respond to the scene");

					for (int i = 0; i < stations.Count; i++)
					{
						if (i >= 8)
							break;

						sb1.Append(string.Format(", press {0} to respond to {1}", i + 2, stations[i].Name));
					}

					for (int repeat = 0; repeat < 2; repeat++)
					{
						var gatherResponse1 = new Gather(numDigits: 1, action: new Uri(string.Format("{0}/api/Twilio/VoiceCallAction?userId={1}&callId={2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId)), method: "GET")
						{
							BargeIn = true
						};
						await AppendVoicePromptAsync(gatherResponse1, TwilioVoicePromptCatalog.RepeatAndRespondToScene, call.DepartmentId);
						await AppendVoicePromptsAsync(gatherResponse1, BuildStationOptionPrompts(stations), call.DepartmentId);
						gatherResponse1.Append(playResponse);
						response.Append(gatherResponse1);
					}

					response.Hangup();

					return CreateVoiceContentResult(response);
				}
			}

			string address = call.Address;
			if (String.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Length > 1)
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
					{
						address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
				}
				catch
				{
				}
			}

			if (String.IsNullOrWhiteSpace(address) && !String.IsNullOrWhiteSpace(call.Address))
				address = call.Address;

			StringBuilder sb = new StringBuilder();

			if (!String.IsNullOrWhiteSpace(address))
				sb.Append(string.Format("{0}, Priority {1} Address {2} Nature {3}", call.Name, call.GetPriorityText(), call.Address, StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall)));
			else
				sb.Append(string.Format("{0}, Priority {1} Nature {2}", call.Name, call.GetPriorityText(), call.NatureOfCall));

			for (int repeat = 0; repeat < 2; repeat++)
			{
				var gatherResponse = new Gather(numDigits: 1, action: new Uri(string.Format("{0}/api/Twilio/VoiceCallAction?userId={1}&callId={2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId)), method: "GET")
				{
					BargeIn = true
				};
				await AppendVoicePromptAsync(gatherResponse, sb.ToString(), call.DepartmentId);
				await AppendVoicePromptAsync(gatherResponse, TwilioVoicePromptCatalog.RepeatAndRespondToScene, call.DepartmentId);
				await AppendVoicePromptsAsync(gatherResponse, BuildStationOptionPrompts(stations), call.DepartmentId);
				response.Append(gatherResponse);
			}

			response.Hangup();

			return CreateVoiceContentResult(response);
		}

		[HttpGet("VoiceCallAction")]
		[Produces("application/xml")]
		public async Task<ActionResult> VoiceCallAction(string userId, int callId, [FromQuery] VoiceRequest twilioRequest)
		{
			var response = new VoiceResponse();

			if (twilioRequest.Digits == "0")
			{
				response.Redirect(new Uri(string.Format("{0}/api/Twilio/VoiceCall?userId={1}&callId={2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId)), "GET");
			}
			else if (twilioRequest.Digits == "1")
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, (int)ActionTypes.RespondingToScene, null, call.CallId);

				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.RespondingToScene, call.DepartmentId);
				response.Hangup();
			}
			else if (int.TryParse(twilioRequest.Digits, out var digit))
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(call.DepartmentId);

				int index = digit - 2;

				if (index >= 0 && index < stations.Count)
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
			else
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.InvalidSelection, call?.DepartmentId);
				response.Redirect(new Uri(string.Format("{0}/api/Twilio/VoiceCall?userId={1}&callId={2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId)), "GET");
			}

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
			Gather gatherResponse = null;

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

				gatherResponse = new Gather(numDigits: 1, action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceActionStatus?userId={userId}"), method: "GET")
				{
					BargeIn = true
				};
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

				gatherResponse = new Gather(numDigits: 1, action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceActionStaffing?userId={userId}"), method: "GET")
				{
					BargeIn = true
				};
			}

			if (gatherResponse == null)
			{
				gatherResponse = new Gather(numDigits: 1, action: new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoiceAction?userId={userId}"), method: "GET")
				{
					BargeIn = true
				};
			}

			for (int repeat = 0; repeat < 2; repeat++)
			{
				var gather = new Gather(numDigits: 1, action: gatherResponse.Action, method: gatherResponse.Method)
				{
					BargeIn = true
				};
				await AppendVoicePromptsAsync(gather, prompts, department.DepartmentId);
				await AppendVoicePromptAsync(gather, TwilioVoicePromptCatalog.GoBackToMainMenu, department.DepartmentId);
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
					response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoice"), "GET");
				}
			}
			else
			{
				await AppendVoicePromptAsync(response, TwilioVoicePromptCatalog.NoStatusSelection, department.DepartmentId);
				response.Redirect(new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/InboundVoice"), "GET");
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

		private static ContentResult CreateVoiceContentResult(VoiceResponse response)
		{
			return new ContentResult
			{
				Content = response.ToString(),
				ContentType = "application/xml",
				StatusCode = 200
			};
		}

		private static IReadOnlyCollection<string> BuildStationOptionPrompts(IEnumerable<DepartmentGroup> stations)
		{
			var prompts = new List<string>();
			var index = 2;

			foreach (var station in stations.Take(8))
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
