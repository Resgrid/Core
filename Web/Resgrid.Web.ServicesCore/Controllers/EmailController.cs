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

		public EmailController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IDistributionListsService distributionListsService,
			IUsersService usersService, IEmailService emailService, IDepartmentGroupsService departmentGroupsService, IMessageService messageService,
			IFileService fileService, IUnitsService unitsService, IGeoLocationProvider geoLocationProvider)
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
		/// <returns><br /></returns>
		[HttpPost("Receive")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
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
							try
							{
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

								var call = await _callsService.GenerateCallFromEmail(emailSettings.FormatType, callEmail,
																				 emailSettings.Department.ManagingUserId,
																				 departmentUsers, emailSettings.Department, activeCalls, units,
																				 defaultPriority, priorities, callTypes);

								if (call != null && call.CallId <= 0)
								{
									// New Call Dispatch as normal
									call.DepartmentId = departmentId.Value;

									if (!String.IsNullOrWhiteSpace(call.Address) && (String.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
									{
										call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);
									}

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

									var cqi = new CallQueueItem();
									cqi.Call = savedCall;
									cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(call.DepartmentId)).Select(x => x.Value).ToList();
									cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(cqi.Call.DepartmentId);

									await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

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
										var cqi = new CallQueueItem();
										cqi.Call = savedCall;
										cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(call.DepartmentId)).Select(x => x.Value).ToList();
										cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(cqi.Call.DepartmentId);

										await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
									}

									return CreatedAtAction(nameof(Receive), new { id = savedCall.CallId }, savedCall);
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
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
								}
								catch (Exception ex)
								{
									Logging.LogException(ex);
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
							try
							{
								var users = await _departmentsService.GetAllUsersForDepartmentAsync(departmentGroup.DepartmentId);
								var emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(departmentGroup.DepartmentId);
								var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroupAndChildGroups(departmentGroup);

								var callEmail = new CallEmail();
								callEmail.Subject = message.Subject;

								if (!String.IsNullOrWhiteSpace(message.HtmlBody))
									callEmail.Body = HttpUtility.HtmlDecode(message.HtmlBody);
								else
									callEmail.Body = message.TextBody;

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

								var call = await _callsService.GenerateCallFromEmail(emailSettings.FormatType, callEmail,
																				 emailSettings.Department.ManagingUserId,
																				 users.Where(x => departmentGroupUsers.Select(y => y.UserId).Contains(x.Id)).ToList(),
																				 emailSettings.Department, activeCalls, units, defaultPriority, priorities, callTypes);

								if (call != null)
								{
									call.DepartmentId = departmentGroup.DepartmentId;

									var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

									var cqi = new CallQueueItem();
									cqi.Call = savedCall;
									cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(departmentGroupUsers.Select(x => x.UserId).ToList());
									cqi.DepartmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(cqi.Call.DepartmentId);

									await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

									return CreatedAtAction(nameof(Receive), new { id = savedCall.CallId }, savedCall);
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
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
							try
							{
								var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroupAndChildGroups(departmentGroup);

								var newMessage = new Message();
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
								await _messageService.SendMessageAsync(savedMessage, "", departmentGroup.DepartmentId, false, cancellationToken);

								return CreatedAtAction(nameof(Receive), new { id = savedMessage.MessageId }, savedMessage);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								return BadRequest();
							}
						}

						#endregion Group Message
					}

					return BadRequest();
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
