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
	private readonly IUsersService _usersService;

	public SignalWireController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
		ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
		IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
		IUserStateService userStateService, ICommunicationService communicationService, IGeoLocationProvider geoLocationProvider,
		IDepartmentGroupsService departmentGroupsService, ICustomStateService customStateService, IUnitsService unitsService,
		ICommunicationTestService communicationTestService, IChatbotIngressService chatbotIngressService, IUsersService usersService)
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
		_usersService = usersService;
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
				// Parity with TwilioController: the sender's profile identifies them and their ACTIVE
				// department drives routing. The TextToCallNumber lookup is a legacy path used only when no
				// profile matched (dispatch centers sending text-to-call imports, unknown senders).
				UserProfile profile = await _userProfileService.GetProfileByMobileNumberAsync(textMessage.Msisdn);

				// SECURITY: an unverified mobile number may be used for ROUTING (department resolution,
				// plan gate) so the sender lands in their real department, but it must never authorize
				// ACTIONS. Downstream handling blocks commands for unverified senders and prompts them to
				// verify. The single exception is STOP (handled below): opting out must always work.
				bool senderNumberUnverified = profile != null && profile.MobileNumberVerified != true;
				if (senderNumberUnverified)
					Framework.Logging.LogInfo($"[SignalWire SMS] MessageSid={textMessage.MessageId} From={Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} matched a profile but the mobile number is not verified; using profile for routing only, actions blocked until verified.");

				int? departmentId = null;
				if (profile != null)
				{
					var activeDepartment = await _departmentsService.GetActiveSmsDepartmentForUserAsync(profile.UserId);
					if (activeDepartment != null)
						departmentId = activeDepartment.DepartmentId;
				}
				else
				{
					departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(textMessage.To);
				}

				// STOP from an unverified sender: honor the opt-out immediately — turning off messages must
				// always work, regardless of verification, plan or department state.
				if (senderNumberUnverified && _textCommandService.DetermineType(textMessage.Text).Type == TextCommandTypes.Stop)
				{
					Framework.Logging.LogInfo($"[SignalWire SMS] MessageSid={textMessage.MessageId} unverified sender texted STOP; disabling SMS messaging on the matched profile.");
					await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId, cancellationToken);
					messageEvent.Processed = true;
					response = LaMLResponse.Message.Respond("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
				}
				else if (departmentId.HasValue)
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
							if (result != null && !string.IsNullOrWhiteSpace(result.Text))
								response = LaMLResponse.Message.Respond(result.Text);
						}
					}
					else if (senderNumberUnverified)
					{
						// Checked BEFORE the switch offer: switching departments is an action, and an
						// unverified sender may not act as the matched user (parity with TwilioController).
						Framework.Logging.LogInfo($"[SignalWire SMS] DepartmentId={departmentId.Value} not authorized for inbound text (plan gate) and sender is unverified; replying with verify-number message.");
						messageEvent.Processed = true;
						response = LaMLResponse.Message.Respond("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
					}
					else if (profile != null)
					{
						// The user's ACTIVE department doesn't support inbound text. If they belong to other
						// departments that do, process a SWITCH command or offer the switch options;
						// otherwise fall through to the plan message.
						response = await HandleUnsupportedActiveDepartmentAsync(textMessage, messageEvent, departmentId.Value, profile, cancellationToken);
					}
					else
					{
						Framework.Logging.LogInfo($"[SignalWire SMS] DepartmentId={departmentId.Value} not authorized for inbound text (plan gate); replying with unsupported message.");
						messageEvent.Processed = true;
						response = LaMLResponse.Message.Respond("Resgrid: Inbound text messaging isn't available on your department's current plan. Please upgrade to a paid plan to enable text commands.");
					}
				}
				else if (textMessage.To == Config.NumberProviderConfig.SignalWireResgridNumber.Replace("+", "")) // Resgrid master text number
				{
					var payload = _textCommandService.DetermineType(textMessage.Text);

					// A sender whose profile mobile number is unverified can't be identified, so commands
					// can't be acted on — direct them to verify instead of replying "unknown command". HELP
					// and STOP still work below: neither needs a trusted identity, and STOP must always opt out.
					if (profile != null && profile.MobileNumberVerified != true
						&& payload.Type != TextCommandTypes.Help && payload.Type != TextCommandTypes.Stop)
					{
						Framework.Logging.LogInfo($"[SignalWire SMS] Master number: sender {Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} matched a profile but the mobile number is not verified; replying with verify-number message.");
						messageEvent.Processed = true;
						response = LaMLResponse.Message.Respond("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
					}
					else
					{
						switch (payload.Type)
						{
							case TextCommandTypes.None:
								response = LaMLResponse.Message.Respond("Resgrid (https://resgrid.com) Automated Text System. Unknown command, text help for supported commands.");
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

								break;
							case TextCommandTypes.Stop:
								messageEvent.Processed = true;

								if (profile == null)
								{
									response = LaMLResponse.Message.Respond("Unable to locate your profile. Please log in to Resgrid to manage your text message settings.");
									break;
								}

								await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId, cancellationToken);

								response = LaMLResponse.Message.Respond("Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.");
								break;
						}
					}
				}
				else if (senderNumberUnverified)
				{
					// No department resolved and this isn't the master number, but the sender did match a
					// profile with an unverified mobile number — tell them the actionable fix.
					Framework.Logging.LogInfo($"[SignalWire SMS] No department resolved for sender {Framework.StringHelpers.MaskPhoneNumber(textMessage.Msisdn)} (unverified profile match); replying with verify-number message.");
					messageEvent.Processed = true;
					response = LaMLResponse.Message.Respond("Resgrid: This mobile number matches a Resgrid profile but hasn't been verified. Please verify your mobile number on your Resgrid profile page to use text commands.");
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

		// Parity with TwilioController: the identified user's ACTIVE department failed the inbound-text
		// plan gate. A SWITCH command is honored (the normal command path is unreachable while the active
		// department is unsupported), any other text gets the switch options, and with no supported
		// alternatives they get the plan message. Returns the LaML response body.
		private async Task<string> HandleUnsupportedActiveDepartmentAsync(TextMessage textMessage, InboundMessageEvent messageEvent,
			int departmentId, UserProfile profile, CancellationToken cancellationToken)
		{
			var supported = await _departmentsService.GetSmsSupportedMembershipsForUserAsync(profile.UserId);
			if (supported.Count == 0)
			{
				Framework.Logging.LogInfo($"[SignalWire SMS] DepartmentId={departmentId} not authorized for inbound text (plan gate); user {profile.UserId} has no SMS-supported departments; replying with unsupported message.");
				messageEvent.Processed = true;
				return LaMLResponse.Message.Respond("Resgrid: Inbound text messaging isn't available on your department's current plan. Please upgrade to a paid plan to enable text commands.");
			}

			var payload = _textCommandService.DetermineType(textMessage.Text);
			if (payload.Type == TextCommandTypes.SwitchDepartment)
				return await HandleSwitchDepartmentCommandAsync(messageEvent, profile, payload.Data, cancellationToken);

			Framework.Logging.LogInfo($"[SignalWire SMS] DepartmentId={departmentId} not authorized for inbound text (plan gate); user {profile.UserId} has {supported.Count} SMS-supported department(s); replying with switch options.");
			messageEvent.Processed = true;
			return LaMLResponse.Message.Respond(await BuildSwitchOptionsMessageAsync(supported,
				"Resgrid: Your active department's plan doesn't include inbound text messaging, but you belong to other departments that do."));
		}

		// SWITCH [name/number]: changes the user's active department. Mirrors the TwilioController
		// handler — same supported-department list and ordering so numeric picks stay stable across
		// providers. Returns the LaML response body.
		private async Task<string> HandleSwitchDepartmentCommandAsync(InboundMessageEvent messageEvent,
			UserProfile profile, string departmentIdentifier, CancellationToken cancellationToken)
		{
			messageEvent.Processed = true;

			var supported = await _departmentsService.GetSmsSupportedMembershipsForUserAsync(profile.UserId);
			if (supported.Count == 0)
				return LaMLResponse.Message.Respond("Resgrid: None of your departments' current plans include inbound text messaging. Please upgrade to a paid plan to enable text commands.");

			if (string.IsNullOrWhiteSpace(departmentIdentifier))
				return LaMLResponse.Message.Respond(await BuildSwitchOptionsMessageAsync(supported, "Resgrid: Your departments that support text messaging:"));

			DepartmentMember target = null;
			var trimmedId = departmentIdentifier.Trim();

			// A small number is a pick from the displayed list; otherwise try a department id, then name/code.
			if (int.TryParse(trimmedId, out var listIndex) && listIndex >= 1 && listIndex <= supported.Count)
				target = supported[listIndex - 1];

			if (target == null && int.TryParse(trimmedId, out var deptId))
				target = supported.FirstOrDefault(m => m.DepartmentId == deptId);

			if (target == null)
			{
				foreach (var membership in supported)
				{
					var dept = await _departmentsService.GetDepartmentByIdAsync(membership.DepartmentId);
					if (dept == null)
						continue;

					if (string.Equals(dept.Name, trimmedId, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(dept.Code, trimmedId, StringComparison.OrdinalIgnoreCase))
					{
						target = membership;
						break;
					}

					if (target == null && !string.IsNullOrWhiteSpace(dept.Name) && dept.Name.IndexOf(trimmedId, StringComparison.OrdinalIgnoreCase) >= 0)
						target = membership;
				}
			}

			if (target == null)
				return LaMLResponse.Message.Respond(await BuildSwitchOptionsMessageAsync(supported, $"Resgrid: Couldn't find a department matching \"{trimmedId}\"."));

			if (target.IsActive)
			{
				var alreadyActive = await _departmentsService.GetDepartmentByIdAsync(target.DepartmentId);
				return LaMLResponse.Message.Respond($"Resgrid: {alreadyActive?.Name ?? "That department"} is already your active department.");
			}

			var identityUser = _usersService.GetUserById(profile.UserId);
			var success = await _departmentsService.SetActiveDepartmentForUserAsync(profile.UserId, target.DepartmentId, identityUser, cancellationToken);

			var targetDept = await _departmentsService.GetDepartmentByIdAsync(target.DepartmentId);
			if (success)
			{
				Framework.Logging.LogInfo($"[SignalWire SMS] User {profile.UserId} switched active department to {target.DepartmentId} via text command.");
				return LaMLResponse.Message.Respond($"Resgrid: Your active department is now {targetDept?.Name ?? ("department " + target.DepartmentId)}. Text commands now apply to this department.");
			}

			return LaMLResponse.Message.Respond("Resgrid: Unable to switch departments right now. Please try again later.");
		}

		private async Task<string> BuildSwitchOptionsMessageAsync(List<DepartmentMember> supported, string prefix)
		{
			var sb = new StringBuilder();
			sb.Append(prefix + Environment.NewLine);

			for (int i = 0; i < supported.Count; i++)
			{
				var dept = await _departmentsService.GetDepartmentByIdAsync(supported[i].DepartmentId);
				var activeMarker = supported[i].IsActive ? " (active)" : "";
				sb.Append($"{i + 1}: {dept?.Name ?? ("Department " + supported[i].DepartmentId)}{activeMarker}" + Environment.NewLine);
			}

			sb.Append("Reply SWITCH <number or name> to change your active department.");
			return sb.ToString();
		}
	}
}
