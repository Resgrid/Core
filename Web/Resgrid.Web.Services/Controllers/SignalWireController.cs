using Newtonsoft.Json;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.NumberProvider;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;

namespace Resgrid.Web.Services.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[Produces("application/xml")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class SignalWireController : ControllerBase
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
	private readonly IChatbotIngressService _chatbotIngressService;

	public SignalWireController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
		ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
		IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
		IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
		IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
		ICommunicationTestService communicationTestService, IChatbotIngressService chatbotIngressService)
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
		_chatbotIngressService = chatbotIngressService;
	}
	#endregion Private Readonly Properties and Constructors

	[HttpGet]
	public ActionResult Test()
	{

		return Ok();
	}


		/// <summary>
		/// Validates the inbound SignalWire webhook signature. SignalWire's Compatibility API uses
		/// the same scheme as Twilio: an X-Twilio-Signature HMAC-SHA1 over the request URL (the
		/// query string carries the params for this GET endpoint), keyed with the project token.
		/// Enforced only when a signing token is configured so unconfigured installs aren't broken;
		/// the URL is reconstructed from ResgridApiBaseUrl to match what SignalWire signed (mirrors
		/// the Twilio middleware's BaseUrlOverride behind the reverse proxy).
		/// </summary>
		private bool ValidateSignalWireRequest()
		{
			var signingToken = Config.NumberProviderConfig.SignalWireApiKey;
			if (string.IsNullOrWhiteSpace(signingToken))
				return true; // Not configured - don't block (see remarks).

			var signature = Request.Headers["X-Twilio-Signature"].ToString();
			if (string.IsNullOrWhiteSpace(signature))
				return false;

			var baseUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			var url = !string.IsNullOrWhiteSpace(baseUrl)
				? baseUrl.TrimEnd('/') + Request.Path + Request.QueryString
				: $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";

			// GET webhook: the signed payload is the URL itself (params live in the query string).
			var validator = new global::Twilio.Security.RequestValidator(signingToken);
			return validator.Validate(url, new Dictionary<string, string>(), signature);
		}

		[HttpGet("Receive")]
		[Produces("application/xml")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult> Receive(CancellationToken cancellationToken)
		{
			if (!ValidateSignalWireRequest())
				return Unauthorized();

			var queryValues = Request.Query.ToDictionary(x => x.Key, y => y.Value.ToString());//.RequestUri.ParseQueryString();

			var textMessage = new TextMessage();
			//textMessage.Type = queryValues["type"];
			textMessage.To = queryValues["To"].Replace("+", "");
			textMessage.Msisdn = queryValues["From"].Replace("+", ""); //queryValues["SmsSid"];
			textMessage.NetworkCode = queryValues["AccountSid"];
			textMessage.MessageId = queryValues["MessageSid"];
			textMessage.Timestamp = DateTime.UtcNow.ToLongDateString();
			//textMessage.Concat = queryValues["concat"];
			//textMessage.ConcatRef = queryValues["concat-ref"];
			//textMessage.ConcatTotal = queryValues["concat-total"];
			//textMessage.ConcatPart = queryValues["concat-part"];
			textMessage.Data = queryValues["Body"];
			//textMessage.Udh = queryValues["udh"];
			textMessage.Text = queryValues["Body"];

			var messageEvent = new InboundMessageEvent();
			messageEvent.MessageType = (int)InboundMessageTypes.TextMessage;
			messageEvent.RecievedOn = DateTime.UtcNow;
			messageEvent.Type = typeof(InboundMessageEvent).FullName;
			messageEvent.Data = JsonConvert.SerializeObject(textMessage);
			messageEvent.Processed = false;
			messageEvent.CustomerId = "";

			string response = "";

			// Check for Communication Test response (CT- prefix)
			if (!string.IsNullOrWhiteSpace(textMessage.Text) && textMessage.Text.Trim().StartsWith("CT-", StringComparison.OrdinalIgnoreCase))
			{
				var runCode = textMessage.Text.Trim().Split(' ')[0].ToUpperInvariant();
				await _communicationTestService.RecordSmsResponseAsync(runCode, textMessage.Msisdn);
				messageEvent.Processed = true;

				response = LaMLResponse.Message.Respond("Resgrid received your communication test response. Thank you.");

				await _numbersService.SaveInboundMessageEventAsync(messageEvent);
				return new ContentResult
				{
					Content = response,
					ContentType = "application/xml",
					StatusCode = 200
				};
			}

			try
			{
				UserProfile profile = null;
				var departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(textMessage.To);

				if (!departmentId.HasValue)
				{
					profile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);

					if (profile != null)
					{
						var department = await _departmentsService.GetDepartmentByUserIdAsync(profile.UserId);

						if (department != null)
							departmentId = department.DepartmentId;
					}
				}

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

							var savedCall = await _callsService.SaveCallAsync(c, cancellationToken);

							var cqi = new CallQueueItem();
							cqi.Call = savedCall;
							cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(users.Select(x => x.UserId).ToList());
							cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(cqi.Call.DepartmentId);

							await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

							messageEvent.Processed = true;
						}

						if (!isDispatchSource && textCommandEnabled && profile != null)
						{
							var request = new ChatbotMessage
							{
								Platform = ChatbotPlatform.SmsSignalWire,
								From = textMessage.Msisdn,
								Text = textMessage.Text,
								MessageId = textMessage.MessageId,
								Timestamp = DateTime.UtcNow
							};

							var result = await _chatbotIngressService.ProcessMessageAsync(request);

							messageEvent.Processed = true;
							response = LaMLResponse.Message.Respond(result.Text);
						}
					}
					else if (textMessage.To == Config.NumberProviderConfig.SignalWireResgridNumber.Replace("+", "")) // Resgrid master text number
					{
						var payload = _textCommandService.DetermineType(textMessage.Text);

						switch (payload.Type)
						{
							case TextCommandTypes.None:
								await _communicationService.SendTextMessageAsync(profile.UserId, "Resgrid TCI Help", "Resgrid (https://resgrid.com) Automated Text System. Unknown command, text help for supported commands.", department.DepartmentId, textMessage.To, profile);
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

								response = LaMLResponse.Message.Respond(help.ToString());
								//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Help", help.ToString(), department.DepartmentId, textMessage.To, profile);

								break;
							case TextCommandTypes.Stop:
								messageEvent.Processed = true;
								await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId, cancellationToken);

								response = LaMLResponse.Message.Respond("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
								//_communicationService.SendTextMessage(profile.UserId, "Resgrid TCI Help", "Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.", department.DepartmentId, textMessage.To, profile);
								break;
						}
					}
				}

				//return Ok(new StringContent(response, Encoding.UTF8, "application/xml"));
				return new ContentResult
				{
					Content = response,
					ContentType = "application/xml",
					StatusCode = 200
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
			finally
			{
				await _numbersService.SaveInboundMessageEventAsync(messageEvent);
			}


			return Ok();
		}

	}
}
