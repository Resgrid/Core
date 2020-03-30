using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text;
using System.Web.Http;
using System.Web.Http.Cors;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models;
using Twilio.Mvc;
using Twilio.TwiML;

namespace Resgrid.Web.Services.Controllers
{
	[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
	[EnableCors(origins: Config.ApiConfig.CorsAllowedHostnames, headers: "*", methods: Config.ApiConfig.CorsAllowedMethods, SupportsCredentials = true)]
	public class TwilioProviderController : ApiController
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

		public TwilioProviderController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
			IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService)
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
		}
		#endregion Private Readonly Properties and Constructors

		[HttpGet]
		public HttpResponseMessage IncomingMessage([FromUri]TwilioMessage request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.Body))
				return Request.CreateResponse(HttpStatusCode.BadRequest);

			var response = new TwilioResponse();

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

			try
			{
				var departmentId = _departmentSettingsService.GetDepartmentIdByTextToCallNumber(textMessage.To);

				if (departmentId.HasValue)
				{
					var department = _departmentsService.GetDepartmentById(departmentId.Value);
					var textToCallEnabled = _departmentSettingsService.GetDepartmentIsTextCallImportEnabled(departmentId.Value);
					var textCommandEnabled = _departmentSettingsService.GetDepartmentIsTextCommandEnabled(departmentId.Value);
					var dispatchNumbers = _departmentSettingsService.GetTextToCallSourceNumbersForDepartment(departmentId.Value);
					var authroized = _limitsService.CanDepartmentProvisionNumber(departmentId.Value);
					var customStates = _customStateService.GetAllActiveCustomStatesForDepartment(departmentId.Value);

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

							var users = _departmentsService.GetAllUsersForDepartment(departmentId.Value, true);
							foreach (var u in users)
							{
								var cd = new CallDispatch();
								cd.UserId = u.UserId;

								c.Dispatches.Add(cd);
							}

							var savedCall = _callsService.SaveCall(c);

							var cqi = new CallQueueItem();
							cqi.Call = savedCall;
							cqi.Profiles = _userProfileService.GetSelectedUserProfiles(users.Select(x => x.UserId).ToList());
							cqi.DepartmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(cqi.Call.DepartmentId);

							_queueService.EnqueueCallBroadcast(cqi);

							messageEvent.Processed = true;
						}

						if (!isDispatchSource && textCommandEnabled)
						{
							var profile = _userProfileService.FindProfileByMobileNumber(textMessage.Msisdn);

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
										_actionLogsService.SetUserAction(profile.UserId, department.DepartmentId, (int)payload.GetActionType());
										response.Message(string.Format("Resgrid received your text command. Status changed to: {0}", payload.GetActionType()));
										//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Status", string.Format("Resgrid recieved your text command. Status changed to: {0}", payload.GetActionType()), department.DepartmentId, textMessage.To, profile);
										break;
									case TextCommandTypes.Staffing:
										messageEvent.Processed = true;
										_userStateService.CreateUserState(profile.UserId, department.DepartmentId, (int)payload.GetStaffingType());
										response.Message(string.Format("Resgrid received your text command. Staffing level changed to: {0}", payload.GetStaffingType()));
										//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Staffing", string.Format("Resgrid recieved your text command. Staffing level changed to: {0}", payload.GetStaffingType()), department.DepartmentId, textMessage.To, profile);
										break;
									case TextCommandTypes.Stop:
										messageEvent.Processed = true;
										_userProfileService.DisableTextMessagesForUser(profile.UserId);
										response.Message("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
										break;
									case TextCommandTypes.CustomAction:
										messageEvent.Processed = true;
										_actionLogsService.SetUserAction(profile.UserId, department.DepartmentId, payload.GetCustomActionType());

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
										_userStateService.CreateUserState(profile.UserId, department.DepartmentId, payload.GetCustomStaffingType());

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


										var userStatus = _actionLogsService.GetLastActionLogForUser(profile.UserId);
										var userStaffing = _userStateService.GetLastUserStateByUserId(profile.UserId);

										var customStatusLevel = _customStateService.GetCustomPersonnelStatus(department.DepartmentId, userStatus);
										var customStaffingLevel = _customStateService.GetCustomPersonnelStaffing(department.DepartmentId, userStaffing);

										response.Message($"Hello {profile.FullName.AsFirstNameLastName} at {DateTime.UtcNow.TimeConverterToString(department)} your current status is {customStatusLevel.ButtonText} and your current staffing is {customStaffingLevel.ButtonText}.");
										break;
									case TextCommandTypes.Calls:
										messageEvent.Processed = true;

										var activeCalls = _callsService.GetActiveCallsByDepartment(department.DepartmentId);

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

										var unitStatus = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(department.DepartmentId);

										var unitStatusesText = new StringBuilder();
										unitStatusesText.Append($"Unit Statuses for {department.Name}" + Environment.NewLine);
										unitStatusesText.Append("---------------------" + Environment.NewLine);

										foreach (var unit in unitStatus)
										{
											var unitState = _customStateService.GetCustomUnitState(unit);
											unitStatusesText.Append($"{unit.Unit.Name} is {unitState.ButtonText}" + Environment.NewLine);
										}

										response.Message(unitStatusesText.ToString());
										break;
									case TextCommandTypes.CallDetail:
										messageEvent.Processed = true;

										var call = _callsService.GetCallById(int.Parse(payload.Data));

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
					var profile = _userProfileService.FindProfileByMobileNumber(textMessage.Msisdn);
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
							_userProfileService.DisableTextMessagesForUser(profile.UserId);
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
				_numbersService.SaveInboundMessageEvent(messageEvent);
			}

			//return Request.CreateResponse(HttpStatusCode.OK);

			//var response = new TwilioResponse();

			return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
		}

		[HttpGet]
		public HttpResponseMessage VoiceCall(string userId, int callId)
		{
			var response = new TwilioResponse();
			var call = _callsService.GetCallById(callId);

			if (call == null)
			{
				response.Say("This call has been closed. Goodbye.").Hangup();
				return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
			}

			if (call.State == (int)CallStates.Cancelled || call.State == (int)CallStates.Closed || call.IsDeleted)
			{
				response.Say(string.Format("This call, Id {0} has been closed. Goodbye.", call.Number)).Hangup();
				return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
			}

			var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(call.DepartmentId);

			string address = call.Address;
			if (String.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(call.GeoLocationData))
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
					{
						address = _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
				}
				catch { }
			}

			if (String.IsNullOrWhiteSpace(address) && !String.IsNullOrWhiteSpace(call.Address))
				address = call.Address;

			StringBuilder sb = new StringBuilder();

			if (!String.IsNullOrWhiteSpace(address))
				sb.Append(string.Format("{0}, Priority {1} Address {2} Nature {3}", call.Name, call.GetPriorityText(), call.Address, call.NatureOfCall));
			else
				sb.Append(string.Format("{0}, Priority {1} Nature {2}", call.Name, call.GetPriorityText(), call.NatureOfCall));


			sb.Append(", Press 0 to repeat, Press 1 to respond to the scene");

			for (int i = 0; i < stations.Count; i++)
			{
				if (i >= 8)
					break;

				sb.Append(string.Format(", press {0} to respond to {1}", i + 2, stations[i].Name));
			}

			response.BeginGather(new { numDigits = 1, timeout = 10, method = "GET", action = string.Format("{0}/Twilio/VoiceCallAction/{1}/{2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId) }).Say(sb.ToString()).EndGather().Pause(10).Hangup();

			return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
		}

		[HttpGet]
		public HttpResponseMessage VoiceCallAction(string userId, int callId, [FromUri]TwilioGatherRequest twilioRequest)
		{
			var response = new TwilioResponse();

			if (twilioRequest.Digits == "0")
				response.Redirect(string.Format("{0}/Twilio/VoiceCall?userId={1}&callId={2}", Config.SystemBehaviorConfig.ResgridApiBaseUrl, userId, callId), "GET");
			else if (twilioRequest.Digits == "1")
			{
				var call = _callsService.GetCallById(callId);
				_actionLogsService.SetUserAction(userId, call.DepartmentId, (int)ActionTypes.RespondingToScene, null, call.CallId);

				response.Say("You have been marked responding to the scene, goodbye.").Hangup();
			}
			else
			{
				var call = _callsService.GetCallById(callId);
				var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(call.DepartmentId);

				int index = int.Parse(twilioRequest.Digits) - 2;

				if (index >= 0 && index < 8)
				{
					var station = stations[index];

					if (station != null)
					{
						_actionLogsService.SetUserAction(userId, call.DepartmentId, (int)ActionTypes.RespondingToStation, null,
							station.DepartmentGroupId);

						response.Say(string.Format("You have been marked responding to {0}, goodbye.", station.Name)).Hangup();
					}

				}
			}

			return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
		}

		[HttpGet]
		public HttpResponseMessage InboundVoice([FromUri]TwilioGatherRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.From))
				return Request.CreateResponse(HttpStatusCode.BadRequest);

			var response = new TwilioResponse();
			var departmentId = _departmentSettingsService.GetDepartmentIdByTextToCallNumber(request.To.Replace("+", ""));

			if (departmentId.HasValue)
			{
				var authroized = _limitsService.CanDepartmentProvisionNumber(departmentId.Value);


				request.From.Replace("+", "");
				if (authroized)
				{
					var department = _departmentsService.GetDepartmentById(departmentId.Value, false);

					UserProfile profile = null;
					profile = _userProfileService.FindProfileByMobileNumber(request.From.Replace("+", ""));

					if (profile == null)
						profile = _userProfileService.FindProfileByHomeNumber(request.From.Replace("+", ""));

					if (department != null && profile != null)
					{
						StringBuilder sb = new StringBuilder();
						sb.Append($@"Hello {profile.FirstName}, this is the Automated Voice System for {department.Name}. Please select from the following options. 
											To list current active calls press 1, 
											To list current user statuses press 2, 
											To list current unit statuses press 3, 
											To list upcoming Calendar events press 4,
											To list upcoming Shifts press 5");

						response.Say(sb.ToString());
					}
					else
					{
						response.Say("Thank you for calling Raesgrid, the only complete software solution for first responders, automated personnel system. The number you called is not tied to an active department or the department doesn't have this feature enabled. Goodbye.").Hangup();
					}
				}
				else
				{
					response.Say("Thank you for calling Raesgrid, the only complete software solution for first responders, automated personnel system. The number you called is not tied to an active department or the department doesn't have this feature enabled. Goodbye.").Hangup();
				}
			}
			else
			{
				response.Say("Thank you for calling Raesgrid, the only complete software solution for first responders, automated personnel system. The number you called is not tied to an active department or the department doesn't have this feature enabled. Goodbye.").Hangup();
			}

			return Request.CreateResponse(HttpStatusCode.OK, response.Element, new XmlMediaTypeFormatter());
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
