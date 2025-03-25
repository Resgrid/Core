//using System;
//using System.Collections.ObjectModel;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Text;
//
//
//using Newtonsoft.Json;
//using Resgrid.Model;
//using Resgrid.Model.Helpers;
//using Resgrid.Model.Queue;
//using Resgrid.Model.Services;
//

//namespace Resgrid.Web.Services.Controllers
//{
//	
//	
//	public class TextMessagesController : ControllerBase
//	{
//		#region Private Readonly Properties and Constructors
//		private readonly IDepartmentSettingsService _departmentSettingsService;
//		private readonly INumbersService _numbersService;
//		private readonly ILimitsService _limitsService;
//		private readonly ICallsService _callsService;
//		private readonly IQueueService _queueService;
//		private readonly IDepartmentsService _departmentsService;
//		private readonly IUserProfileService _userProfileService;
//		private readonly ITextCommandService _textCommandService;
//		private readonly IActionLogsService _actionLogsService;
//		private readonly IUserStateService _userStateService;
//		private readonly ICommunicationService _communicationService;

//		public TextMessagesController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
//			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
//			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
//			IUserStateService userStateService, ICommunicationService communicationService)
//		{
//			_departmentSettingsService = departmentSettingsService;
//			_numbersService = numbersService;
//			_limitsService = limitsService;
//			_callsService = callsService;
//			_queueService = queueService;
//			_departmentsService = departmentsService;
//			_userProfileService = userProfileService;
//			_textCommandService = textCommandService;
//			_actionLogsService = actionLogsService;
//			_userStateService = userStateService;
//			_communicationService = communicationService;
//		}
//		#endregion Private Readonly Properties and Constructors

//		[System.Web.Http.HttpGet]
//		//[HttpPost]
//		public ActionResult Process()//([FromBody]TextMessage textMessage2)
//		{
//			var queryValues = Request.RequestUri.ParseQueryString();

//			var textMessage = new TextMessage();
//			textMessage.Type = queryValues["type"];
//			textMessage.To = queryValues["to"];
//			textMessage.Msisdn = queryValues["msisdn"];
//			textMessage.NetworkCode = queryValues["network-code"];
//			textMessage.MessageId = queryValues["messageId"];
//			textMessage.Timestamp = queryValues["message-timestamp"];
//			textMessage.Concat = queryValues["concat"];
//			textMessage.ConcatRef = queryValues["concat-ref"];
//			textMessage.ConcatTotal = queryValues["concat-total"];
//			textMessage.ConcatPart = queryValues["concat-part"];
//			textMessage.Data = queryValues["data"];
//			textMessage.Udh = queryValues["udh"];
//			textMessage.Text = queryValues["text"];

//			var messageEvent = new InboundMessageEvent();
//			messageEvent.MessageType = (int)InboundMessageTypes.TextMessage;
//			messageEvent.RecievedOn = DateTime.UtcNow;
//			messageEvent.Type = typeof(InboundMessageEvent).FullName;
//			messageEvent.Data = JsonConvert.SerializeObject(textMessage);
//			messageEvent.Processed = false;
//			messageEvent.CustomerId = "";

//			try
//			{
//				var departmentId = _departmentSettingsService.GetDepartmentIdByTextToCallNumber(textMessage.To);

//				if (departmentId.HasValue)
//				{
//					var department = _departmentsService.GetDepartmentById(departmentId.Value);
//					var textToCallEnabled = _departmentSettingsService.GetDepartmentIsTextCallImportEnabled(departmentId.Value);
//					var textCommandEnabled = _departmentSettingsService.GetDepartmentIsTextCommandEnabled(departmentId.Value);
//					var dispatchNumbers = _departmentSettingsService.GetTextToCallSourceNumbersForDepartment(departmentId.Value);
//					var authroized = _limitsService.CanDepartmentProvisionNumber(departmentId.Value);

//					messageEvent.CustomerId = departmentId.Value.ToString();

//					if (authroized)
//					{
//						bool isDispatchSource = false;

//						if (!String.IsNullOrWhiteSpace(dispatchNumbers))
//							isDispatchSource = _numbersService.DoesNumberMatchAnyPattern(dispatchNumbers.Split(Char.Parse(",")).ToList(), textMessage.Msisdn);

//						// If we don't have dispatchNumbers and Text Command isn't 
//						// enabled it's a dispatch text
//						if (!isDispatchSource && !textCommandEnabled)
//							isDispatchSource = true;
						
//						if (isDispatchSource && textToCallEnabled)
//						{
//							var c = new Call();
//							c.Notes = textMessage.Text;
//							c.NatureOfCall = textMessage.Text;
//							c.LoggedOn = DateTime.UtcNow;
//							c.Name = string.Format("TTC {0}", c.LoggedOn.TimeConverter(department).ToString("g"));
//							c.Priority = (int)CallPriority.High;
//							c.ReportingUserId = department.ManagingUserId;
//							c.Dispatches = new Collection<CallDispatch>();
//							c.CallSource = (int)CallSources.EmailImport;
//							c.SourceIdentifier = textMessage.MessageId;
//							c.DepartmentId = departmentId.Value;

//							var users = _departmentsService.GetAllUsersForDepartment(departmentId.Value, true);
//							foreach (var u in users)
//							{
//								var cd = new CallDispatch();
//								cd.UserId = u.UserId;

//								c.Dispatches.Add(cd);
//							}

//							var savedCall = _callsService.SaveCall(c);

//							var cqi = new CallQueueItem();
//							cqi.Call = savedCall;
//							cqi.Profiles = _userProfileService.GetSelectedUserProfiles(users.Select(x => x.UserId).ToList());
//							cqi.DepartmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(cqi.Call.DepartmentId);

//							_queueService.EnqueueCallBroadcast(cqi);

//							messageEvent.Processed = true;
//						}

//						if (!isDispatchSource && textCommandEnabled)
//						{
//							var profile = _userProfileService.FindProfileByMobileNumber(textMessage.Msisdn);

//							if (profile != null)
//							{
//								var payload = _textCommandService.DetermineType(textMessage.Text);

//								switch (payload.Type)
//								{
//									case TextCommandTypes.None:
//										break;
//									case TextCommandTypes.Help:
//										messageEvent.Processed = true;

//										var help = new StringBuilder();
//										help.Append("Resgrid Text Commands" + Environment.NewLine);
//										help.Append("---------------------" + Environment.NewLine);
//										help.Append("These are the commands you can text to alter your status and staffing. Text help for help." + Environment.NewLine);
//										help.Append("Status Commands" + Environment.NewLine);
//										help.Append("---------------------" + Environment.NewLine);
//										help.Append("responding or 1: Responding" + Environment.NewLine);
//										help.Append("notresponding or 2: Not Responding" + Environment.NewLine);
//										help.Append("onscene or 3: On Scene" + Environment.NewLine);
//										help.Append("available or 4: Available" + Environment.NewLine);
//										help.Append("Staffing Commands" + Environment.NewLine);
//										help.Append("---------------------" + Environment.NewLine);
//										help.Append("normal or s1: Available Staffing" + Environment.NewLine);
//										help.Append("delayed or s2: Delayed Staffing" + Environment.NewLine);
//										help.Append("unavailable or s3: Unavailable Staffing" + Environment.NewLine);
//										help.Append("committed or s4: Committed Staffing" + Environment.NewLine);
//										help.Append("onshift or s4: On Shift Staffing" + Environment.NewLine);

//										_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Help", help.ToString(), department.DepartmentId, textMessage.To, profile);
//										break;
//									case TextCommandTypes.Action:
//										messageEvent.Processed = true;
//										_actionLogsService.SetUserAction(profile.UserId, department.DepartmentId, (int)payload.GetActionType());
//										_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Status", string.Format("Resgrid received your text command. Status changed to: {0}", payload.GetActionType()), department.DepartmentId, textMessage.To, profile);
//										break;
//									case TextCommandTypes.Staffing:
//										messageEvent.Processed = true;
//										_userStateService.CreateUserState(profile.UserId, department.DepartmentId,(int)payload.GetStaffingType());
//										_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Staffing", string.Format("Resgrid received your text command. Staffing level changed to: {0}", payload.GetStaffingType()), department.DepartmentId, textMessage.To, profile);
//										break;
//								}
//							}
//						}
//					}

//				}
//			}
//			catch (Exception ex)
//			{
//				Framework.Logging.LogException(ex);
//			}
//			finally
//			{
//				_numbersService.SaveInboundMessageEvent(messageEvent);
//			}

//			Ok();
//		}
//	}
//}
