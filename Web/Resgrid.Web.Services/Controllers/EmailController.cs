using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
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

		public EmailController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IDistributionListsService distributionListsService,
			IUsersService usersService, IEmailService emailService, IDepartmentGroupsService departmentGroupsService, IMessageService messageService,
			IFileService fileService, IUnitsService unitsService, IGeoLocationProvider geoLocationProvider, ICallDispatchStatusService callDispatchStatusService,
			IDispatchRecommendationService dispatchRecommendationService, IFeatureToggleService featureToggleService,
			Resgrid.Model.AiDispatch.IAiDispatchAdminService aiDispatchAdminService, IInboundEmailReceiptsService inboundEmailReceiptsService)
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
		/// Postmark retries every answer except a 200 for about 10.5 hours, and a 403 stops it: 200/201 when handled (or handled
		/// before), 409 while another delivery of the same email is still being handled, 403 when a retry could not help (no
		/// Resgrid address, unknown address, nothing this department's email format can read), 400 on a failure worth retrying.
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
						return CreatedAtAction(nameof(Receive), new { id = message.MessageID }, message);

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

								var activeCalls = await _callsService.GetLatest10ActiveCallsByDepartmentAsync(emailSettings.Department.DepartmentId);
								var units = await _unitsService.GetUnitsForDepartmentAsync(emailSettings.Department.DepartmentId);
								var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(emailSettings.Department.DepartmentId);
								var callTypes = await _callsService.GetCallTypesForDepartmentAsync(emailSettings.Department.DepartmentId);

								int defaultPriority = (int)CallPriority.High;

								if (priorities != null && priorities.Any())
								{
									var defaultPrio = priorities.FirstOrDefault(x => x.IsDefault && x.IsDeleted == false);

									if (defaultPrio != null)
										defaultPriority = defaultPrio.DepartmentCallPriorityId;
								}

								call = await _callsService.GenerateCallFromEmail(emailSettings.FormatType, callEmail,
																				 emailSettings.Department.ManagingUserId,
																				 departmentUsers, emailSettings.Department, activeCalls, units,
																				 defaultPriority, priorities, callTypes);

								if (call != null && call.CallId <= 0)
								{
									// New Call Dispatch as normal
									isNewCall = true;
									call.DepartmentId = departmentId.Value;
									pendingSteps = InboundEmailPendingSteps.CallBroadcast;

									if (emailSettings.FormatType == (int)CallEmailTypes.AI)
										pendingSteps |= InboundEmailPendingSteps.AiEnrichment;

									if (!String.IsNullOrWhiteSpace(call.Address) && (String.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
									{
										call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);
									}

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
									await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedCall.CallId, pendingSteps);
									saveRecorded = true;

									await QueueCallBroadcastAsync(savedCall, cancellationToken);

									if (emailSettings.FormatType == (int)CallEmailTypes.AI)
										await QueueAiDispatchEnrichmentAsync(savedCall, 1, message.FromFull?.Email, cancellationToken);

									await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
									return CreatedAtAction(nameof(Receive), new { id = savedCall.CallId }, savedCall);
								}
								else if (call != null && call.CallId > 0)
								{
									// Existing Call, just update
									if (!String.IsNullOrWhiteSpace(call.Address) && (String.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
									{
										call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);
									}

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

									// If our Dispatch Count changed, i.e. 2nd Alarm to 3rd Alarm, redispatch
									if (call.DidDispatchCountChange())
									{
										await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedCall.CallId, InboundEmailPendingSteps.CallBroadcast);
										await QueueCallBroadcastAsync(savedCall, cancellationToken);
									}

									await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
									return CreatedAtAction(nameof(Receive), new { id = savedCall.CallId }, savedCall);
								}

								// The department's email format found no call in this email, and would read it the same way again.
								await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
								return NotProcessable();
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
								return CreatedAtAction(nameof(Receive), new { id = message.MessageID }, message);
							}
						}

						return CreatedAtAction(nameof(Receive), new { id = message.MessageID }, message);
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

								var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(emailSettings.Department.DepartmentId);
								var units = await _unitsService.GetAllUnitsForGroupAsync(departmentGroup.DepartmentGroupId);
								var callTypes = await _callsService.GetCallTypesForDepartmentAsync(emailSettings.Department.DepartmentId);
								var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(emailSettings.Department.DepartmentId);
								int defaultPriority = (int)CallPriority.High;

								if (priorities != null && priorities.Any())
								{
									var defaultPrio = priorities.FirstOrDefault(x => x.IsDefault && x.IsDeleted == false);

									if (defaultPrio != null)
										defaultPriority = defaultPrio.DepartmentCallPriorityId;
								}

								call = await _callsService.GenerateCallFromEmail(emailSettings.FormatType, callEmail,
																				 emailSettings.Department.ManagingUserId,
																				 users.Where(x => departmentGroupUsers.Select(y => y.UserId).Contains(x.Id)).ToList(),
																				 emailSettings.Department, activeCalls, units, defaultPriority, priorities, callTypes);

								if (call != null)
								{
									call.DepartmentId = departmentGroup.DepartmentId;

									// As on the department address: an address with no usable coordinates is placed on the map.
									if (!String.IsNullOrWhiteSpace(call.Address) && (String.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
										call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);

									// The incident-number formats hand back an open call for a follow-up page. As on the department address,
									// that updates the call and only alerts again when its dispatch count changed (2nd alarm and so on).
									var isUpdate = call.CallId > 0;
									isNewCall = !isUpdate;

									if (!isUpdate || call.DidDispatchCountChange())
										pendingSteps = InboundEmailPendingSteps.CallBroadcast;

									if (!isUpdate && emailSettings.FormatType == (int)CallEmailTypes.AI)
										pendingSteps |= InboundEmailPendingSteps.AiEnrichment;

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

									if (pendingSteps != InboundEmailPendingSteps.None)
										await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedCall.CallId, pendingSteps);
									saveRecorded = true;

									// Group-scoped: the units and members above were narrowed to this
									// group, so run card enrichment stays out of it.
									if (pendingSteps.HasFlag(InboundEmailPendingSteps.CallBroadcast))
										await QueueCallBroadcastAsync(savedCall, cancellationToken, false);

									if (pendingSteps.HasFlag(InboundEmailPendingSteps.AiEnrichment))
										await QueueAiDispatchEnrichmentAsync(savedCall, 2, message.FromFull?.Email, cancellationToken);

									await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
									return CreatedAtAction(nameof(Receive), new { id = savedCall.CallId }, savedCall);
								}

								// The department's email format found no call in this email, and would read it the same way again.
								await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
								return NotProcessable();
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
								return CreatedAtAction(nameof(Receive), new { id = savedMessage.MessageId }, savedMessage);
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
		/// Finishes a call an earlier delivery of the same email saved and then failed on: queues the broadcast (and AI enrichment)
		/// that never went out, once, instead of creating a second call. A call someone has since closed or deleted is not paged.
		/// </summary>
		private async Task<ActionResult> ResumeCallAsync(InboundEmailReceiptClaim receipt, bool allowRunCardEnrichment, int aiChannel,
			PostmarkInboundMessage message, CancellationToken cancellationToken)
		{
			var call = receipt.EntityId.HasValue ? await _callsService.GetCallByIdAsync(receipt.EntityId.Value) : null;

			if (call == null || call.IsDeleted || call.State != (int)CallStates.Active)
			{
				await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
				return Ok();
			}

			if (receipt.Has(InboundEmailPendingSteps.CallBroadcast))
				await QueueCallBroadcastAsync(call, cancellationToken, allowRunCardEnrichment);

			if (receipt.Has(InboundEmailPendingSteps.AiEnrichment))
				await QueueAiDispatchEnrichmentAsync(call, aiChannel, message.FromFull?.Email, cancellationToken);

			await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
			return CreatedAtAction(nameof(Receive), new { id = call.CallId }, call);
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
			return CreatedAtAction(nameof(Receive), new { id = savedMessage.MessageId }, savedMessage);
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
		/// </summary>
		private async Task QueueAiDispatchEnrichmentAsync(Call savedCall, int channel, string sender, CancellationToken cancellationToken)
		{
			if (!Config.AiDispatchConfig.EnrichEnabled || savedCall == null || savedCall.CallId <= 0)
				return;

			try
			{
				// The sender allowlist is checked here so only a yes/no, never an address, travels on the bus.
				await _queueService.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
				{
					DepartmentId = savedCall.DepartmentId, CallId = savedCall.CallId, Channel = channel, QueuedOnUtc = DateTime.UtcNow,
					SenderNotAllowed = !await _aiDispatchAdminService.IsSenderAllowedAsync(savedCall.DepartmentId, sender)
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
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
