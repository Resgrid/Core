using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostmarkDotNet;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using MimeKit;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services.Controllers
{
	[Produces("application/json")]
	[ApiController]
	[Route("api/[controller]")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class EmailController : ControllerBase
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
		private readonly IDistributionListsService _distributionListsService;
		private readonly IUsersService _usersService;
		private readonly IEmailService _emailService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IMessageService _messageService;
		private readonly IFileService _fileService;
		private readonly IUnitsService _unitsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IDispatchRecommendationService _dispatchRecommendationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly Resgrid.Model.AiDispatch.IAiDispatchAdminService _aiDispatchAdminService;
		private readonly IInboundEmailReceiptsService _inboundEmailReceiptsService;
		private readonly IIntakeService _intakeService;
		private readonly IEmailSender _emailSender;
		private readonly IDispatcherEmailCallService _dispatcherEmailCallService;
		private readonly IEmailCallUpdateService _emailCallUpdateService;

		public EmailController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IDistributionListsService distributionListsService,
			IUsersService usersService, IEmailService emailService, IDepartmentGroupsService departmentGroupsService, IMessageService messageService,
			IFileService fileService, IUnitsService unitsService, IGeoLocationProvider geoLocationProvider, ICallDispatchStatusService callDispatchStatusService,
			IDispatchRecommendationService dispatchRecommendationService, IFeatureToggleService featureToggleService,
			Resgrid.Model.AiDispatch.IAiDispatchAdminService aiDispatchAdminService, IInboundEmailReceiptsService inboundEmailReceiptsService,
			IIntakeService intakeService, IEmailSender emailSender, IDispatcherEmailCallService dispatcherEmailCallService,
			IEmailCallUpdateService emailCallUpdateService)
		{
			_emailCallUpdateService = emailCallUpdateService;
			_intakeService = intakeService;
			_emailSender = emailSender;
			_dispatcherEmailCallService = dispatcherEmailCallService;
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
			_distributionListsService = distributionListsService;
			_usersService = usersService;
			_emailService = emailService;
			_departmentGroupsService = departmentGroupsService;
			_messageService = messageService;
			_fileService = fileService;
			_unitsService = unitsService;
			_geoLocationProvider = geoLocationProvider;
			_callDispatchStatusService = callDispatchStatusService;
			_dispatchRecommendationService = dispatchRecommendationService;
			_featureToggleService = featureToggleService;
			_aiDispatchAdminService = aiDispatchAdminService;
			_inboundEmailReceiptsService = inboundEmailReceiptsService;
		}
		#endregion Private Readonly Properties and Constructors

		[HttpGet("Test")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult Test()
		{
			return Ok();
		}

		/// <summary>
		/// The receiver API method call
		/// </summary>
		/// <param name="message">A Postmark Inbound message http://developer.postmarkapp.com/developer-inbound-parse.html#example-hook</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>
		/// Postmark retries every answer except a 200 for about 10.5 hours, and a 403 stops it: 200 when handled (or handled
		/// before; never 201, which Postmark's inbound contract does not name as delivered), 409 while another delivery of the
		/// same email is still being handled, 403 when a retry could not help (no Resgrid address, unknown address, nothing this
		/// department's email format can read), 400 on a failure worth retrying.
		/// </returns>
		[HttpPost("Receive")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult> Receive(PostmarkInboundMessage message, CancellationToken cancellationToken)
		{
			if (message != null)
			{
				try
				{
					var mailMessage = new MimeMessage();

					if (message.FromFull != null && !String.IsNullOrWhiteSpace(message.FromFull.Email) && message.FromFull.Email.Trim() == "support@postmarkapp.com")
						return Ok(message);

					if (message.FromFull != null && !String.IsNullOrWhiteSpace(message.FromFull.Email) && !String.IsNullOrWhiteSpace(message.FromFull.Name))
						mailMessage.From.Add(new MailboxAddress(message.FromFull.Name.Trim(), message.FromFull.Email.Trim()));
					else
						mailMessage.From.Add(new MailboxAddress("Inbound Email Dispatch", "do-not-reply@resgrid.com"));

					if (!String.IsNullOrWhiteSpace(message.Subject))
						mailMessage.Subject = message.Subject;
					else
						mailMessage.Subject = "Dispatch Email";

					var builder = new BodyBuilder();

					if (!String.IsNullOrWhiteSpace(message.HtmlBody))
						builder.HtmlBody = HttpUtility.HtmlDecode(message.HtmlBody);

					if (!String.IsNullOrWhiteSpace(message.TextBody))
						builder.TextBody = message.TextBody;

					int type = 0; // 1 = dispatch // 2 = email list // 3 = group dispatch // 4 = group message
					string emailAddress = String.Empty;
					string bounceEmail = String.Empty;
					string name = String.Empty;

					#region Trying to Find What type of email this is
					if (message.ToFull != null)
					{
						foreach (var email in message.ToFull)
						{
							if (StringHelpers.ValidateEmail(email.Email))
							{
								if (email.Email.Contains($"@{Config.InboundEmailConfig.DispatchDomain}") || email.Email.Contains($"@{Config.InboundEmailConfig.DispatchTestDomain}"))
								{
									type = 1;

									if (email.Email.Contains($"@{Config.InboundEmailConfig.DispatchDomain}"))
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.DispatchDomain}", "");
									else
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.DispatchTestDomain}", "");

									name = email.Name;
									mailMessage.To.Clear();
									mailMessage.To.Add(new MailboxAddress(email.Name, email.Email));

									break;
								}
								else if (email.Email.Contains($"@{Config.InboundEmailConfig.ListsDomain}") || email.Email.Contains($"@{Config.InboundEmailConfig.ListsTestDomain}"))
								{
									type = 2;

									if (email.Email.Contains($"@{Config.InboundEmailConfig.ListsDomain}"))
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.ListsDomain}", "");
									else
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.ListsTestDomain}", "");

									if (emailAddress.Contains("+") && emailAddress.Contains("="))
									{
										var tempBounceEmail = emailAddress.Substring(emailAddress.IndexOf("+") + 1);
										bounceEmail = tempBounceEmail.Replace("=", "@");

										emailAddress = emailAddress.Replace(tempBounceEmail, "");
										emailAddress = emailAddress.Replace("+", "");
									}

									name = email.Name;
									mailMessage.To.Clear();
									mailMessage.To.Add(new MailboxAddress(email.Name, email.Email));

									break;
								}
								else if (email.Email.Contains($"@{Config.InboundEmailConfig.GroupsDomain}") || email.Email.Contains($"@{Config.InboundEmailConfig.GroupsTestDomain}"))
								{
									type = 3;

									if (email.Email.Contains($"@{Config.InboundEmailConfig.GroupsDomain}"))
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.GroupsDomain}", "");
									else
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.GroupsTestDomain}", "");

									name = email.Name;
									mailMessage.To.Clear();
									mailMessage.To.Add(new MailboxAddress(email.Name, email.Email));

									break;
								}
								else if (email.Email.Contains($"@{Config.InboundEmailConfig.GroupMessageDomain}") || email.Email.Contains($"@{Config.InboundEmailConfig.GroupTestMessageDomain}"))
								{
									type = 4;

									if (email.Email.Contains($"@{Config.InboundEmailConfig.GroupMessageDomain}"))
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.GroupMessageDomain}", "");
									else
										emailAddress = email.Email.Replace($"@{Config.InboundEmailConfig.GroupTestMessageDomain}", "");

									name = email.Name;
									mailMessage.To.Clear();
									mailMessage.To.Add(new MailboxAddress(email.Name, email.Email));

									break;
								}
							}
						}
					}

					// Some providers aren't putting email address in the To line, process the CC line
					if (type == 0 && message.CcFull != null)
					{
						foreach (var email in message.CcFull)
						{
							if (StringHelpers.ValidateEmail(email.Email))
							{
								var proccedEmailInfo = ProcessEmailAddress(email.Email);

								if (proccedEmailInfo.Item1 > 0)
								{
									type = proccedEmailInfo.Item1;
									emailAddress = proccedEmailInfo.Item2;

									mailMessage.To.Clear();
									mailMessage.To.Add(new MailboxAddress(email.Name, email.Email));
								}
							}
						}
					}

					if (type == 0 && message.BccFull != null)
					{
						foreach (var email in message.BccFull)
						{
							if (StringHelpers.ValidateEmail(email.Email))
							{
								var proccedEmailInfo = ProcessEmailAddress(email.Email);

								if (proccedEmailInfo.Item1 > 0)
								{
									type = proccedEmailInfo.Item1;
									emailAddress = proccedEmailInfo.Item2;

									mailMessage.To.Clear();
									mailMessage.To.Add(new MailboxAddress(email.Name, email.Email));
								}
							}
						}
					}

					// If To and CC didn't work, try the header.
					if (type == 0)
					{
						try
						{
							if (message.Headers != null && message.Headers.Count > 0)
							{
								var header = message.Headers.FirstOrDefault(x => x.Name == "Received-SPF");

								if (header != null)
								{
									var lastValue = header.Value.LastIndexOf(char.Parse("="));
									var newEmail = header.Value.Substring(lastValue + 1, (header.Value.Length - (lastValue + 1)));

									newEmail = newEmail.Trim();

									if (StringHelpers.ValidateEmail(newEmail))
									{
										emailAddress = newEmail;
										var proccedEmailInfo = ProcessEmailAddress(newEmail);

										type = proccedEmailInfo.Item1;
										emailAddress = proccedEmailInfo.Item2;

										mailMessage.To.Clear();
										mailMessage.To.Add(new MailboxAddress("Email Importer", newEmail));
									}
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
					#endregion Trying to Find What type of email this is

					if (type == 1)  // Dispatch
					{
						// An intake channel's address on the dispatch domain (distinct from every department's dispatch address):
						// the email becomes a PENDING call. IntakeMessages is its idempotency claim, so no InboundEmailReceipt is taken.
						var intake = await ReceiveIntakeEmailAsync(message, emailAddress, cancellationToken);
						if (intake != null)
							return intake;

						#region Dispatch Email
						var departmentId = await _departmentSettingsService.GetDepartmentIdForDispatchEmailAsync(emailAddress);

						if (departmentId.HasValue)
						{
							var receipt = await _inboundEmailReceiptsService.ClaimAsync(message.MessageID, InboundEmailKinds.Dispatch, cancellationToken);
							var duplicate = DuplicateDeliveryResult(receipt);
							if (duplicate != null)
								return duplicate;

							Call call = null;
							var isNewCall = false;
							var saveRecorded = false;
							var pendingSteps = InboundEmailPendingSteps.None;

							try
							{
								if (receipt.Outcome == InboundEmailClaimOutcomes.Resume)
									return await ResumeCallAsync(receipt, true, 1, message, cancellationToken);

								var emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(departmentId.Value);
								List<IdentityUser> departmentUsers = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId.Value, true);

								var callEmail = new CallEmail();

								if (!String.IsNullOrWhiteSpace(message.Subject))
									callEmail.Subject = message.Subject;

								else
									callEmail.Subject = "Dispatch Email";

								if (!String.IsNullOrWhiteSpace(message.HtmlBody))
									callEmail.Body = HttpUtility.HtmlDecode(message.HtmlBody);
								else
									callEmail.Body = message.TextBody;

								callEmail.TextBody = message.TextBody;
								callEmail.MessageId = message.MessageID;
								callEmail.From = message.FromFull?.Email?.Trim();
								callEmail.SentOn = CallEmail.ParseSentOn(message.Date);

								foreach (var attachment in message.Attachments)
								{
									try
									{
										if (Convert.ToInt32(attachment.ContentLength) > 0)
										{
											if (attachment.Name.Contains(".mp3") || attachment.Name.Contains(".amr"))
											{
												byte[] filebytes = Convert.FromBase64String(attachment.Content);

												callEmail.DispatchAudioFileName = attachment.Name;
												callEmail.DispatchAudio = filebytes;
											}
										}
									}
									catch { }
								}

								if (emailSettings == null)
								{
									emailSettings = new DepartmentCallEmail();
									emailSettings.FormatType = (int)CallEmailTypes.Generic;
									emailSettings.DepartmentId = departmentId.Value;
									emailSettings.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value, false);
								}
								else if (emailSettings.Department == null)
								{
									emailSettings.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value);
								}

								var units = await _unitsService.GetUnitsForDepartmentAsync(emailSettings.Department.DepartmentId);
								var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(emailSettings.Department.DepartmentId);
								var callTypes = await _callsService.GetCallTypesForDepartmentAsync(emailSettings.Department.DepartmentId);

								// The default priority, else the department's High priority, else the built-in High (CallPriorityMatch).
								int defaultPriority = CallPriorityMatch.DefaultForImport(priorities);

								var parsed = await _emailCallUpdateService.ParseAsync(emailSettings.FormatType, callEmail, emailSettings.Department.ManagingUserId,
									departmentUsers, emailSettings.Department, units, defaultPriority, priorities, callTypes, cancellationToken);

								if (parsed?.Call == null)
								{
									// A close or cancel the format does not read as a call still closes the call carrying its CAD call ID.
									var closeOnly = await CloseWithoutCallAsync(departmentId.Value, emailSettings.Department, emailSettings.FormatType, callEmail,
										defaultPriority, CallEmailUpdateSources.DispatchAddress, cancellationToken);
									if (closeOnly != null)
										return await AnswerUpdateAsync(receipt, closeOnly, null);

									// The department's email format found no call in this email, and would read it the same way again.
									await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
									return NotProcessable();
								}

								// A new call is dispatched as it always was; the CAD update engine decides whether one is needed, or whether
								// this email updates, closes or is noted on the call that already carries its CAD call ID (cad-email-call-updates-plan.md).
								ActionResult createdResult = null;
								var updateRequest = new EmailCallUpdateRequest
								{
									DepartmentId = departmentId.Value,
									Department = emailSettings.Department,
									FormatType = emailSettings.FormatType,
									Email = callEmail,
									Parsed = parsed,
									Source = CallEmailUpdateSources.DispatchAddress
								};
								async Task<Call> createDispatchCall(Call newCall)
								{
									// New Call Dispatch as normal, unless the department's email call mode holds it pending for a dispatcher (D10)
									call = newCall;
									isNewCall = true;
									call.DepartmentId = departmentId.Value;
									var emailCallMode = await _dispatcherEmailCallService.GetModeAsync(departmentId.Value);
									var holdAsPending = emailCallMode != DispatcherEmailCallModes.AutoDispatch;
									var enrich = await _dispatcherEmailCallService.ShouldEnrichAsync(departmentId.Value, emailSettings.FormatType, emailCallMode);
									pendingSteps = holdAsPending ? InboundEmailPendingSteps.CallAddedEvent : InboundEmailPendingSteps.CallBroadcast;

									if (holdAsPending)
										_dispatcherEmailCallService.HoldAsPending(call);

									if (enrich)
										pendingSteps |= InboundEmailPendingSteps.AiEnrichment;

									if (!String.IsNullOrWhiteSpace(call.Address) && (String.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
									{
										call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);
									}

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
									try
									{
										await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedCall.CallId, pendingSteps);
										saveRecorded = true;

										var onDesk = true;
										if (holdAsPending)
											onDesk = await _dispatcherEmailCallService.OnPendingCallSavedAsync(savedCall, message.FromFull?.Email, callEmail.Subject, cancellationToken);
										else
											await QueueCallBroadcastAsync(savedCall, cancellationToken);

										var aiQueued = enrich && await QueueAiDispatchEnrichmentAsync(savedCall, 1, message.FromFull?.Email, cancellationToken, heldForDispatcher: holdAsPending);

										if (!onDesk)
											createdResult = await DeskHandOffOwedAsync(receipt, aiQueued);
										else if (enrich && !aiQueued)
											createdResult = await AiEnrichmentOwedAsync(receipt, pendingSteps, pagedNow: !holdAsPending);
										else
										{
											await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
											createdResult = Ok(savedCall);
										}
									}
									catch
									{
										await RecordBaselineAfterFailureAsync(updateRequest, savedCall);
										throw;
									}

									return savedCall;
								}

								var update = await _emailCallUpdateService.ProcessAsync(updateRequest, createDispatchCall, cancellationToken);

								return await AnswerUpdateAsync(receipt, update, createdResult);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								await RecordPartialCallAsync(receipt, isNewCall, saveRecorded, call, pendingSteps);
								await _inboundEmailReceiptsService.ReleaseAsync(receipt);
								return BadRequest(message);
							}
						}
						#endregion Dispatch
					}
					else if (type == 2) // Email List
					{
						#region Distribution Email
						var list = await _distributionListsService.GetDistributionListByAddressAsync(emailAddress);

						if (list != null)
						{
							if (String.IsNullOrWhiteSpace(bounceEmail))
							{
								var receipt = await _inboundEmailReceiptsService.ClaimAsync(message.MessageID, InboundEmailKinds.DistributionList, cancellationToken);
								var duplicate = DuplicateDeliveryResult(receipt);
								if (duplicate != null)
									return duplicate;

								// Nothing is saved for a list email before its broadcast is queued (attachments aside), so a failed
								// attempt is simply processed again and there is nothing to resume.
								try
								{
									List<Model.File> files = new List<Model.File>();

									try
									{
										if (message.Attachments != null && message.Attachments.Any())
										{
											foreach (var attachment in message.Attachments)
											{
												if (Convert.ToInt32(attachment.ContentLength) > 0)
												{
													Model.File file = new Model.File();

													byte[] filebytes = Convert.FromBase64String(attachment.Content);

													file.Data = filebytes;
													file.FileName = attachment.Name;
													file.DepartmentId = list.DepartmentId;
													file.ContentId = attachment.ContentID;
													file.FileType = attachment.ContentType;
													file.Timestamp = DateTime.UtcNow;

													files.Add(await _fileService.SaveFileAsync(file, cancellationToken));
												}
											}
										}
									}
									catch { }

									var dlqi = new DistributionListQueueItem();
									dlqi.List = list;
									dlqi.Users = await _departmentsService.GetAllUsersForDepartmentAsync(list.DepartmentId);

									if (files != null && files.Any())
									{
										dlqi.FileIds = new List<int>();
										dlqi.FileIds.AddRange(files.Select(x => x.FileId).ToList());
									}

									dlqi.Message = new InboundMessage();
									dlqi.Message.Attachments = new List<InboundMessageAttachment>();

									if (message.FromFull != null && !String.IsNullOrWhiteSpace(message.FromFull.Email) && !String.IsNullOrWhiteSpace(message.FromFull.Name))
									{
										dlqi.Message.FromEmail = message.FromFull.Email.Trim();
										dlqi.Message.FromName = message.FromFull.Name.Trim();
									}

									dlqi.Message.Subject = message.Subject;
									dlqi.Message.HtmlBody = message.HtmlBody;
									dlqi.Message.TextBody = message.TextBody;
									dlqi.Message.MessageID = message.MessageID;

									await _queueService.EnqueueDistributionListBroadcastAsync(dlqi, cancellationToken);
									await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
								}
								catch (Exception ex)
								{
									Logging.LogException(ex);
									await _inboundEmailReceiptsService.ReleaseAsync(receipt);
									return BadRequest(message);
								}
							}
							else
							{
								return Ok(message);
							}
						}

						return Ok(message);
						#endregion Distribution Email
					}
					if (type == 3)  // Group Dispatch
					{
						#region Group Dispatch Email
						var departmentGroup = await _departmentGroupsService.GetGroupByDispatchEmailCodeAsync(emailAddress);

						if (departmentGroup != null)
						{
							var receipt = await _inboundEmailReceiptsService.ClaimAsync(message.MessageID, InboundEmailKinds.GroupDispatch, cancellationToken);
							var duplicate = DuplicateDeliveryResult(receipt);
							if (duplicate != null)
								return duplicate;

							Call call = null;
							var isNewCall = false;
							var saveRecorded = false;
							var pendingSteps = InboundEmailPendingSteps.None;

							try
							{
								if (receipt.Outcome == InboundEmailClaimOutcomes.Resume)
									return await ResumeCallAsync(receipt, false, 2, message, cancellationToken);

								var users = await _departmentsService.GetAllUsersForDepartmentAsync(departmentGroup.DepartmentId);
								var emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(departmentGroup.DepartmentId);
								var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroupAndChildGroups(departmentGroup);

								// Built the same way as the department dispatch email above: the formats that read the plain-text body
								// (Active911, R&R, OttawaCounty, OttawaKingstonToronto) need TextBody, and several need a subject.
								var callEmail = new CallEmail();

								if (!String.IsNullOrWhiteSpace(message.Subject))
									callEmail.Subject = message.Subject;
								else
									callEmail.Subject = "Dispatch Email";

								if (!String.IsNullOrWhiteSpace(message.HtmlBody))
									callEmail.Body = HttpUtility.HtmlDecode(message.HtmlBody);
								else
									callEmail.Body = message.TextBody;

								callEmail.TextBody = message.TextBody;
								callEmail.MessageId = message.MessageID;
								callEmail.From = message.FromFull?.Email?.Trim();
								callEmail.SentOn = CallEmail.ParseSentOn(message.Date);

								foreach (var attachment in message.Attachments)
								{
									try
									{
										if (Convert.ToInt32(attachment.ContentLength) > 0)
										{
											if (attachment.Name.Contains(".mp3") || attachment.Name.Contains(".amr"))
											{
												byte[] filebytes = Convert.FromBase64String(attachment.Content);

												callEmail.DispatchAudioFileName = attachment.Name;
												callEmail.DispatchAudio = filebytes;
											}
										}
									}
									catch { }
								}

								if (emailSettings == null)
								{
									emailSettings = new DepartmentCallEmail();
									emailSettings.FormatType = (int)CallEmailTypes.Generic;
									emailSettings.DepartmentId = departmentGroup.DepartmentId;
								}

								if (departmentGroup.Department != null)
									emailSettings.Department = departmentGroup.Department;
								else
									emailSettings.Department = await _departmentsService.GetDepartmentByIdAsync(departmentGroup.DepartmentId);

								var units = await _unitsService.GetAllUnitsForGroupAsync(departmentGroup.DepartmentGroupId);
								var callTypes = await _callsService.GetCallTypesForDepartmentAsync(emailSettings.Department.DepartmentId);
								var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(emailSettings.Department.DepartmentId);
								// The default priority, else the department's High priority, else the built-in High (CallPriorityMatch).
								int defaultPriority = CallPriorityMatch.DefaultForImport(priorities);

								var parsed = await _emailCallUpdateService.ParseAsync(emailSettings.FormatType, callEmail, emailSettings.Department.ManagingUserId,
									users.Where(x => departmentGroupUsers.Select(y => y.UserId).Contains(x.Id)).ToList(),
									emailSettings.Department, units, defaultPriority, priorities, callTypes, cancellationToken);

								if (parsed?.Call == null)
								{
									// A close or cancel the format does not read as a call still closes the call carrying its CAD call ID.
									var closeOnly = await CloseWithoutCallAsync(departmentGroup.DepartmentId, emailSettings.Department, emailSettings.FormatType, callEmail,
										defaultPriority, CallEmailUpdateSources.GroupDispatchAddress, cancellationToken);
									if (closeOnly != null)
										return await AnswerUpdateAsync(receipt, closeOnly, null);

									// The department's email format found no call in this email, and would read it the same way again.
									await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
									return NotProcessable();
								}

								ActionResult createdResult = null;
								var updateRequest = new EmailCallUpdateRequest
								{
									DepartmentId = departmentGroup.DepartmentId,
									Department = emailSettings.Department,
									FormatType = emailSettings.FormatType,
									Email = callEmail,
									Parsed = parsed,
									Source = CallEmailUpdateSources.GroupDispatchAddress
								};
								async Task<Call> createGroupCall(Call newCall)
								{
									call = newCall;
									isNewCall = true;
									call.DepartmentId = departmentGroup.DepartmentId;

									// As on the department address: an address with no usable coordinates is placed on the map.
									if (!String.IsNullOrWhiteSpace(call.Address) && (String.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
										call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);

									pendingSteps = InboundEmailPendingSteps.CallBroadcast;
									if (emailSettings.FormatType == (int)CallEmailTypes.AI)
										pendingSteps |= InboundEmailPendingSteps.AiEnrichment;

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
									try
									{
										await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedCall.CallId, pendingSteps);
										saveRecorded = true;

										// Group-scoped: the units and members above were narrowed to this
										// group, so run card enrichment stays out of it.
										await QueueCallBroadcastAsync(savedCall, cancellationToken, false);

										if (pendingSteps.HasFlag(InboundEmailPendingSteps.AiEnrichment) &&
											!await QueueAiDispatchEnrichmentAsync(savedCall, 2, message.FromFull?.Email, cancellationToken))
										{
											createdResult = await AiEnrichmentOwedAsync(receipt, pendingSteps, pagedNow: true);
											return savedCall;
										}

										await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
										createdResult = Ok(savedCall);
									}
									catch
									{
										await RecordBaselineAfterFailureAsync(updateRequest, savedCall);
										throw;
									}

									return savedCall;
								}

								// A follow-up page for an open call (same CAD call ID) updates it instead (cad-email-call-updates-plan.md).
								var update = await _emailCallUpdateService.ProcessAsync(updateRequest, createGroupCall, cancellationToken);

								return await AnswerUpdateAsync(receipt, update, createdResult);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								await RecordPartialCallAsync(receipt, isNewCall, saveRecorded, call, pendingSteps);
								await _inboundEmailReceiptsService.ReleaseAsync(receipt);
								return BadRequest();
							}
						}
						#endregion Group Dispatch Email
					}
					if (type == 4)  // Group Message
					{
						#region Group Message
						var departmentGroup = await _departmentGroupsService.GetGroupByMessageEmailCodeAsync(emailAddress);

						if (departmentGroup != null)
						{
							var department = await _departmentsService.GetDepartmentByIdAsync(departmentGroup.DepartmentId);

							var receipt = await _inboundEmailReceiptsService.ClaimAsync(message.MessageID, InboundEmailKinds.GroupMessage, cancellationToken);
							var duplicate = DuplicateDeliveryResult(receipt);
							if (duplicate != null)
								return duplicate;

							Message newMessage = null;
							var saveRecorded = false;

							try
							{
								if (receipt.Outcome == InboundEmailClaimOutcomes.Resume)
									return await ResumeMessageAsync(receipt, departmentGroup.DepartmentId, cancellationToken);

								var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroupAndChildGroups(departmentGroup);

								newMessage = new Message();
								newMessage.SentOn = DateTime.UtcNow;
								newMessage.SendingUserId = department.ManagingUserId;
								newMessage.IsBroadcast = true;
								newMessage.Subject = message.Subject;
								newMessage.SystemGenerated = true;

								if (!String.IsNullOrWhiteSpace(message.HtmlBody))
									newMessage.Body = HttpUtility.HtmlDecode(message.HtmlBody);
								else
									newMessage.Body = message.TextBody;

								foreach (var member in departmentGroupUsers)
								{
									if (newMessage.GetRecipients().All(x => x != member.UserId))
										newMessage.AddRecipient(member.UserId);
								}

								var savedMessage = await _messageService.SaveMessageAsync(newMessage, cancellationToken);
								await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedMessage.MessageId, InboundEmailPendingSteps.MessageSend);
								saveRecorded = true;

								await _messageService.SendMessageAsync(savedMessage, "", departmentGroup.DepartmentId, false, cancellationToken);

								await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
								return Ok(savedMessage);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);

								// As for calls: the message row is written before its recipients, so keep an id a failure left behind.
								if (!saveRecorded && newMessage != null && newMessage.MessageId > 0)
									await _inboundEmailReceiptsService.MarkSavedAsync(receipt, newMessage.MessageId, InboundEmailPendingSteps.MessageSend);

								await _inboundEmailReceiptsService.ReleaseAsync(receipt);
								return BadRequest();
							}
						}

						#endregion Group Message
					}

					// No Resgrid address, or one no department or group owns: a retry would land here again.
					return NotProcessable();
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					return BadRequest(ex.ToString());
				}
			}
			else
			{
				// If our message was null, we throw an exception
				return BadRequest("Error parsing Inbound Message, message is null.");
			}
		}

		/// <summary>
		/// Postmark's answer once the CAD update engine handled a dispatch email: the create path's own answer for a new call; 409 (the
		/// claim goes back, Postmark delivers again) while another email with the same CAD call ID is being handled; otherwise 200.
		/// </summary>
		private async Task<ActionResult> AnswerUpdateAsync(InboundEmailReceiptClaim receipt, EmailCallUpdateResult update, ActionResult createdResult)
		{
			if (update.Outcome == CallEmailUpdateOutcomes.Busy)
			{
				await _inboundEmailReceiptsService.ReleaseAsync(receipt);
				return StatusCode(StatusCodes.Status409Conflict);
			}

			if (update.CreatedNewCall && createdResult != null)
				return createdResult;

			await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);

			// Ok(null) is answered 204, which Postmark retries for hours: a close email that matched no call has no call to return.
			return update.Call != null ? Ok(update.Call) : Ok();
		}

		/// <summary>
		/// A close or cancel email the department's format reads as no call (an Active911 "CLOSED …" page has no "active" in its subject)
		/// still goes to the CAD update engine, as intake does, which closes the open call carrying its CAD call ID or notes the close on a
		/// call already closed. Nothing is ever created from it. Null when the email is not a close the department's CAD update
		/// settings recognise, or carries no CAD call ID they can read without the format (the caller answers 403).
		/// </summary>
		private async Task<EmailCallUpdateResult> CloseWithoutCallAsync(int departmentId, Department department, int formatType, CallEmail callEmail,
			int defaultPriority, CallEmailUpdateSources source, CancellationToken cancellationToken)
		{
			var config = await _departmentSettingsService.GetEmailCallUpdateConfigAsync(departmentId, formatType);
			var parsed = CadEmailText.CloseOnlyParse(config, callEmail, departmentId, defaultPriority);
			if (parsed == null)
				return null;

			var update = await _emailCallUpdateService.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = departmentId,
				Department = department,
				FormatType = formatType,
				Email = callEmail,
				Parsed = parsed,
				Source = source,
				Config = config
			}, unsaved => Task.FromResult<Call>(null), cancellationToken);

			// The create path only runs without a usable ID; it creates nothing here, and the email is answered as unreadable.
			return update.CreatedNewCall ? null : update;
		}

		/// <summary>
		/// The answer to a delivery of an email this API already handled, or is handling right now; null when the delivery should
		/// be processed. A completed email is acknowledged with a 200 so Postmark stops; one still in flight gets a 409 so Postmark
		/// delivers it again later, when it is either finished or its attempt has died and can be taken over.
		/// </summary>
		private ActionResult DuplicateDeliveryResult(InboundEmailReceiptClaim receipt)
		{
			switch (receipt.Outcome)
			{
				case InboundEmailClaimOutcomes.AlreadyProcessed:
					return Ok();
				case InboundEmailClaimOutcomes.InProgress:
					return StatusCode(StatusCodes.Status409Conflict);
				default:
					return null;
			}
		}

		/// <summary>A 403 is the one answer that stops Postmark retrying; used where a retry would fail the same way.</summary>
		private ActionResult NotProcessable()
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		/// <summary>
		/// The call row is written before its dispatch rows and the broadcast, so a failure after that (including the provider
		/// dropping the connection) leaves a new call behind. Recording its id lets the provider's retry finish this call instead of
		/// creating another one.
		/// </summary>
		private async Task RecordPartialCallAsync(InboundEmailReceiptClaim receipt, bool isNewCall, bool saveRecorded, Call call, InboundEmailPendingSteps pendingSteps)
		{
			if (isNewCall && !saveRecorded && call != null && call.CallId > 0)
				await _inboundEmailReceiptsService.MarkSavedAsync(receipt, call.CallId, pendingSteps);
		}

		/// <summary>
		/// The CAD update baseline of a new call whose page, desk entry or receipt step failed after the save. The engine records the
		/// baseline only once the create path returns, and the provider's retry resumes the saved call without the engine, so without
		/// this later CAD emails could not tell a dispatcher's edit from the CAD's. Taken from the call as this delivery saved it (the
		/// CAD's values, before anyone could edit it). Nothing for an email that carries no CAD call ID. Never throws.
		/// </summary>
		private async Task RecordBaselineAfterFailureAsync(EmailCallUpdateRequest request, Call savedCall)
		{
			try
			{
				if (savedCall == null || savedCall.CallId <= 0)
					return;

				var config = await _departmentSettingsService.GetEmailCallUpdateConfigAsync(request.DepartmentId, request.FormatType);
				if (config == null || !config.Enabled)
					return;

				var cadCallId = CadEmailText.ExtractCallId(config, request.Email, request.Parsed?.MatchKey);
				if (cadCallId != null)
					await _emailCallUpdateService.RecordCreatedAsync(request, savedCall, cadCallId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording the CAD update baseline of call {savedCall?.CallId} after its delivery failed.");
			}
		}

		/// <summary>
		/// Finishes a call an earlier delivery of the same email saved and then failed on: queues the broadcast (and AI enrichment)
		/// that never went out, once, instead of creating a second call. A call someone has since closed or deleted is not paged.
		/// </summary>
		private async Task<ActionResult> ResumeCallAsync(InboundEmailReceiptClaim receipt, bool allowRunCardEnrichment, int aiChannel,
			PostmarkInboundMessage message, CancellationToken cancellationToken)
		{
			var call = receipt.EntityId.HasValue ? await _callsService.GetCallByIdAsync(receipt.EntityId.Value) : null;

			if (call == null || call.IsDeleted || (call.State != (int)CallStates.Active && call.State != (int)CallStates.Pending))
			{
				await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
				return Ok();
			}

			// A pending call (email call mode Pending / EnhancedAi) is never paged here: only its desk entry and screen refresh are owed.
			var onDesk = true;
			var paged = false;
			if (call.State == (int)CallStates.Pending)
			{
				if (receipt.Has(InboundEmailPendingSteps.CallAddedEvent))
					onDesk = await _dispatcherEmailCallService.OnPendingCallSavedAsync(call, message.FromFull?.Email, message.Subject, cancellationToken);
			}
			else if (receipt.Has(InboundEmailPendingSteps.CallBroadcast))
			{
				await QueueCallBroadcastAsync(call, cancellationToken, allowRunCardEnrichment);
				paged = true;
			}

			// Held for a dispatcher: the original delivery recorded the desk step only for a call its email call mode held, and a call
			// still pending was held (the email call mode is the only path here that saves one). The pending state counts as well because
			// the desk step is recorded done once the desk entry went out, so an AI job owed after that keeps its held entitlement.
			var enrich = receipt.Has(InboundEmailPendingSteps.AiEnrichment);
			var aiQueued = enrich && await QueueAiDispatchEnrichmentAsync(call, aiChannel, message.FromFull?.Email, cancellationToken,
				heldForDispatcher: receipt.Has(InboundEmailPendingSteps.CallAddedEvent) || call.State == (int)CallStates.Pending);

			if (!onDesk)
				return await DeskHandOffOwedAsync(receipt, aiQueued);

			if (enrich && !aiQueued)
				return await AiEnrichmentOwedAsync(receipt, InboundEmailPendingSteps.CallBroadcast | InboundEmailPendingSteps.CallAddedEvent, paged);

			await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
			return Ok(call);
		}

		/// <summary>
		/// The call is saved and its other steps (<paramref name="done"/>: the page or the desk entry) went out, but the AI enrichment job
		/// could not be queued: only that step stays owed, and the claim goes back with a 400 so Postmark redelivers and the resume queues
		/// the job (finding the saved call, so no second call is made and nobody is paged again).
		/// </summary>
		/// <param name="pagedNow">
		/// This request queued the call's page. If the receipt cannot record that, a redelivery would page the call again, so the AI job
		/// is given up instead (logged; a dispatcher reads the call without it) and the delivery answered as done. A desk entry that could
		/// not be recorded is still retried: the desk takes a call it already has once. An untracked delivery (the receipt store was
		/// unavailable at the claim) is never sent back either: its redelivery would save and page a second call.
		/// </param>
		private async Task<ActionResult> AiEnrichmentOwedAsync(InboundEmailReceiptClaim receipt, InboundEmailPendingSteps done, bool pagedNow)
		{
			done &= ~InboundEmailPendingSteps.AiEnrichment;
			var tracked = receipt != null && receipt.IsTracked;
			var recorded = tracked && (done == InboundEmailPendingSteps.None || await _inboundEmailReceiptsService.CompleteStepAsync(receipt, done));

			if (!recorded && (pagedNow || !tracked))
			{
				Logging.LogError(tracked
					? $"Inbound email receipt {receipt.ReceiptId}: the call was paged but that could not be recorded, so its AI enrichment is not retried (a retry would page it again)."
					: "Inbound email: the AI enrichment of an untracked delivery is not retried (a retry would save and page a second call).");
				await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
				return Ok();
			}

			await _inboundEmailReceiptsService.ReleaseAsync(receipt);
			return BadRequest();
		}

		/// <summary>
		/// A held emailed call is saved but could not be put on the dispatcher desk: that step is still owed (the AI step is recorded
		/// done only when its job was actually queued, so a failed AI enqueue is retried with it), and the claim goes back with a 400 so
		/// Postmark redelivers and the resume retries the desk entry at once instead of waiting for the desk worker's sweep.
		/// </summary>
		private async Task<ActionResult> DeskHandOffOwedAsync(InboundEmailReceiptClaim receipt, bool aiQueued)
		{
			if (aiQueued)
				await _inboundEmailReceiptsService.CompleteStepAsync(receipt, InboundEmailPendingSteps.AiEnrichment);

			await _inboundEmailReceiptsService.ReleaseAsync(receipt);
			return BadRequest();
		}

		/// <summary>Sends a group message an earlier delivery of the same email saved and then failed to send.</summary>
		private async Task<ActionResult> ResumeMessageAsync(InboundEmailReceiptClaim receipt, int departmentId, CancellationToken cancellationToken)
		{
			var savedMessage = receipt.EntityId.HasValue ? await _messageService.GetMessageByIdAsync(receipt.EntityId.Value) : null;

			if (savedMessage == null)
			{
				await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
				return Ok();
			}

			if (receipt.Has(InboundEmailPendingSteps.MessageSend))
				await _messageService.SendMessageAsync(savedMessage, "", departmentId, false, cancellationToken);

			await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
			return Ok(savedMessage);
		}

		/// <param name="allowRunCardEnrichment">
		/// False for calls raised through a group's dispatch address. That path deliberately
		/// builds its dispatch list from one group's units and members, and the recommendation
		/// engine scores against the whole department, so enriching there would pull in other
		/// stations and break the scoping the caller asked for by emailing that address.
		/// </param>
		private async Task QueueCallBroadcastAsync(Call savedCall, CancellationToken cancellationToken, bool allowRunCardEnrichment = true)
		{
			var call = await _callsService.PopulateCallData(savedCall, true, false, false, true, true, true, false, false, false);

			// Run card auto-dispatch for email-originated calls: additively merge
			// recommended resources when the resolved auto-dispatch decision is on.
			try
			{
				if (allowRunCardEnrichment && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, call.DepartmentId))
				{
					var recommendation = await _dispatchRecommendationService.EnrichCallForDispatchAsync(call, 1, true, cancellationToken);

					if (recommendation.MatchedRunCardId.HasValue && recommendation.AutoDispatch && recommendation.HasRecommendations)
					{
						call = await _callsService.SaveCallAsync(call, cancellationToken);
						await _dispatchRecommendationService.RecordActivationAsync(call, recommendation, null, cancellationToken);
					}
				}
			}
			catch (Exception ex)
			{
				// A recommendation failure must never block the email-originated dispatch itself.
				Logging.LogException(ex);
			}

			var cqi = new CallQueueItem();
			cqi.Call = call;

			if ((call.GroupDispatches != null && call.GroupDispatches.Any()) || (call.UnitDispatches != null && call.UnitDispatches.Any()) || (call.RoleDispatches != null && call.RoleDispatches.Any()))
				cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(call.DepartmentId)).Select(x => x.Value).ToList();
			else if (call.Dispatches != null && call.Dispatches.Any())
				cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(call.Dispatches.Select(x => x.UserId).ToList());
			else
				cqi.Profiles = new List<UserProfile>();

			cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(call.DepartmentId);

			if ((call.GroupDispatches != null && call.GroupDispatches.Any()) || (call.UnitDispatches != null && call.UnitDispatches.Any()))
				await _callDispatchStatusService.ApplyDispatchStatusesAsync(call, cancellationToken: cancellationToken);

			await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
		}

		/// <summary>
		/// AI dispatch, Enrich mode: the call above is already saved and its broadcast queued exactly as for GenericTemplate.
		/// Enrichment runs afterwards on the worker, so a disabled host or a failed enqueue never touches the dispatch itself.
		/// <paramref name="heldForDispatcher"/>: the dispatcher desk's email call mode held the call pending (the worker enriches it while
		/// it waits, under that mode's entitlement); otherwise the job keeps the active-call, AI-format rule.
		/// </summary>
		/// <returns>
		/// True when the job was queued or is not wanted (AI dispatch off on this host, no call): the step is done. False when queueing
		/// it failed (logged), so a delivery that is retried anyway can keep the step owed.
		/// </returns>
		private async Task<bool> QueueAiDispatchEnrichmentAsync(Call savedCall, int channel, string sender, CancellationToken cancellationToken, bool heldForDispatcher = false)
		{
			if (!Config.AiDispatchConfig.EnrichEnabled || savedCall == null || savedCall.CallId <= 0)
				return true;

			try
			{
				// The sender allowlist is checked here so only a yes/no, never an address, travels on the bus.
				await _queueService.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
				{
					DepartmentId = savedCall.DepartmentId, CallId = savedCall.CallId, Channel = channel, QueuedOnUtc = DateTime.UtcNow,
					SenderNotAllowed = !await _aiDispatchAdminService.IsSenderAllowedAsync(savedCall.DepartmentId, sender),
					HeldForDispatcher = heldForDispatcher
				}, cancellationToken);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		/// <summary>
		/// Email intake (call-intake-and-ai-destinations-plan.md): null when <paramref name="localPart"/> is not an intake channel's
		/// address (the dispatch path continues). Otherwise the result Postmark gets: 200 for every outcome a retry cannot change
		/// (call created, duplicate, refused, sender not on the channel's list), 400 when creating the call failed so Postmark
		/// redelivers and intake takes the failed claim over.
		/// </summary>
		private async Task<ActionResult> ReceiveIntakeEmailAsync(PostmarkInboundMessage message, string localPart, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(localPart))
				return null;

			var body = !String.IsNullOrWhiteSpace(message.TextBody)
				? message.TextBody
				: String.IsNullOrWhiteSpace(message.HtmlBody) ? null : StringHelpers.StripHtmlTagsCharArray(HttpUtility.HtmlDecode(message.HtmlBody));

			var result = await _intakeService.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = IntakeChannelTypes.Email,
				ChannelAddress = localPart,
				ProviderMessageId = message.MessageID,
				Sender = message.FromFull?.Email?.Trim(),
				SenderName = message.FromFull?.Name?.Trim(),
				Subject = message.Subject,
				Body = body,
				ReceivedOnUtc = DateTime.UtcNow,
				SentOnUtc = CallEmail.ParseSentOn(message.Date)
			}, cancellationToken);

			if (result == null || result.State == IntakeMessageStates.Refused && (result.Reason == "NoChannel" || result.Reason == "IntakeDisabled"))
				return null;

			if (result.State == IntakeMessageStates.Failed)
				return BadRequest();

			// Another delivery of this email is still creating its call (or retrying a failed attempt): 409, as the dispatch path does
			// for an in-flight receipt, so Postmark delivers it again instead of treating an attempt that may still fail as done.
			if (result.State == IntakeMessageStates.Duplicate && result.Reason == IntakeResultReasons.DuplicateInProgress)
				return StatusCode(StatusCodes.Status409Conflict);

			if (result.State == IntakeMessageStates.CallCreated && !string.IsNullOrWhiteSpace(result.Acknowledgement))
				await SendIntakeAcknowledgementAsync(message, result.Acknowledgement);

			return Ok(new { result.State, result.CallId });
		}

		/// <summary>The receipt reply's subject (fixed: nothing from the incoming email is repeated, its subject included).</summary>
		public const string IntakeAcknowledgementSubject = "Received: your request";

		/// <summary>
		/// The receipt reply, from do-not-reply and marked auto-replied (RFC 3834), never to automated senders or in answer to an
		/// automatic message, so two systems can never bounce acknowledgements back and forth.
		/// </summary>
		private async Task SendIntakeAcknowledgementAsync(PostmarkInboundMessage message, string text)
		{
			try
			{
				var to = message.FromFull?.Email?.Trim();
				if (string.IsNullOrWhiteSpace(to) || !StringHelpers.ValidateEmail(to) || IsAutomatedSender(to))
					return;

				var autoSubmitted = message.Headers?.FirstOrDefault(h => string.Equals(h.Name, "Auto-Submitted", StringComparison.OrdinalIgnoreCase))?.Value;
				var precedence = message.Headers?.FirstOrDefault(h => string.Equals(h.Name, "Precedence", StringComparison.OrdinalIgnoreCase))?.Value;
				if (!string.IsNullOrWhiteSpace(autoSubmitted) && !string.Equals(autoSubmitted.Trim(), "no", StringComparison.OrdinalIgnoreCase))
					return;
				if (!string.IsNullOrWhiteSpace(precedence) && Regex.IsMatch(precedence, "bulk|junk|list|auto_reply", RegexOptions.IgnoreCase))
					return;

				using var mail = new System.Net.Mail.MailMessage();
				mail.To.Add(to);
				mail.From = new System.Net.Mail.MailAddress("do-not-reply@resgrid.com", "Resgrid");
				// A fixed subject: the incoming subject may carry incident or patient detail, and the reply never repeats message content.
				mail.Subject = IntakeAcknowledgementSubject;
				mail.Body = text;
				mail.IsBodyHtml = false;
				mail.Headers.Add("Auto-Submitted", "auto-replied");
				await _emailSender.SendEmail(mail);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Intake email acknowledgement failed.");
			}
		}

		private static bool IsAutomatedSender(string address)
		{
			var local = address.Split('@')[0].ToLowerInvariant();
			return local.Contains("noreply") || local.Contains("no-reply") || local.Contains("donotreply") || local.Contains("do-not-reply") ||
				local == "mailer-daemon" || local == "postmaster" || local.Contains("bounce");
		}

		private static Tuple<int, string> ProcessEmailAddress(string email)
		{
			if (string.IsNullOrWhiteSpace(email))
				return new Tuple<int, string>(0, String.Empty);

			int type = 0;
			string emailAddress = String.Empty;

			if (email.Contains($"@{Config.InboundEmailConfig.DispatchDomain}") || email.Contains($"@{Config.InboundEmailConfig.DispatchTestDomain}"))
			{
				type = 1;

				if (email.Contains($"@{Config.InboundEmailConfig.DispatchDomain}"))
					emailAddress = email.Replace($"@{Config.InboundEmailConfig.DispatchDomain}", "").Trim();
				else
					emailAddress = email.Replace($"@{Config.InboundEmailConfig.DispatchTestDomain}", "").Trim();
			}
			else if (email.Contains($"@{Config.InboundEmailConfig.ListsDomain}"))
			{
				type = 2;
				emailAddress = email.Replace($"@{Config.InboundEmailConfig.ListsDomain}", "").Trim();
			}
			else if (email.Contains($"@{Config.InboundEmailConfig.GroupsDomain}") || email.Contains($"@{Config.InboundEmailConfig.GroupsTestDomain}"))
			{
				type = 3;

				if (email.Contains($"@{Config.InboundEmailConfig.GroupsDomain}"))
					emailAddress = email.Replace($"@{Config.InboundEmailConfig.GroupsDomain}", "").Trim();
				else
					emailAddress = email.Replace($"@{Config.InboundEmailConfig.GroupsTestDomain}", "").Trim();
			}

			return new Tuple<int, string>(type, emailAddress);
		}
	}
}
