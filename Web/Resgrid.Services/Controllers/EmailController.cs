using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using PostmarkDotNet;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.EmailProvider;
using Microsoft.AspNet.Identity.EntityFramework6;
using MimeKit;

namespace Resgrid.Web.Services.Controllers
{
	[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
	[EnableCors(origins: Config.ApiConfig.CorsAllowedHostnames, headers: "*", methods: Config.ApiConfig.CorsAllowedMethods, SupportsCredentials = true)]
	public class EmailController : ApiController
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

		public EmailController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
			ILimitsService limitsService, ICallsService callsService, IQueueService queueService, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, ITextCommandService textCommandService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IDistributionListsService distributionListsService,
			IUsersService usersService, IEmailService emailService, IDepartmentGroupsService departmentGroupsService, IMessageService messageService,
			IFileService fileService, IUnitsService unitsService)
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
		}
		#endregion Private Readonly Properties and Constructors

		/// <summary>
		/// The receiver API method call
		/// </summary>
		/// <param name="message">A Postmark Inbound message http://developer.postmarkapp.com/developer-inbound-parse.html#example-hook </param>
		/// <returns></returns>
		[HttpPost]
		public HttpResponseMessage Receive(PostmarkInboundMessage message)
		{
			if (message != null)
			{
				try
				{
					var mailMessage = new MimeMessage();

					if (message.FromFull != null && !String.IsNullOrWhiteSpace(message.FromFull.Email) && message.FromFull.Email.Trim() == "support@postmarkapp.com")
						return new HttpResponseMessage(HttpStatusCode.Created);

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

					// Some providers aren't putting email address in the To line, process the CC line
					if (type == 0)
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

					if (type == 1)	// Dispatch
					{
						#region Dispatch Email
						var departmentId = _departmentSettingsService.GetDepartmentIdForDispatchEmail(emailAddress);

						if (departmentId.HasValue)
						{
							try
							{
								var emailSettings = _departmentsService.GetDepartmentEmailSettings(departmentId.Value);
								List<IdentityUser> departmentUsers = _departmentsService.GetAllUsersForDepartment(departmentId.Value, true);

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
									emailSettings.Department = _departmentsService.GetDepartmentById(departmentId.Value, false);
								}
								else if (emailSettings.Department == null)
								{
									emailSettings.Department = _departmentsService.GetDepartmentById(departmentId.Value);
								}

								var activeCalls = _callsService.GetLatest10ActiveCallsByDepartment(emailSettings.Department.DepartmentId);
								var units = _unitsService.GetUnitsForDepartment(emailSettings.Department.DepartmentId);
								var priorities = _callsService.GetActiveCallPrioritesForDepartment(emailSettings.Department.DepartmentId);
								int defaultPriority = (int) CallPriority.High;

								if (priorities != null && priorities.Any())
								{
									var defaultPrio = priorities.FirstOrDefault(x => x.IsDefault && x.IsDeleted == false);

									if (defaultPrio != null)
										defaultPriority = defaultPrio.DepartmentCallPriorityId;
								}

								var call = _callsService.GenerateCallFromEmail(emailSettings.FormatType, callEmail,
																				 emailSettings.Department.ManagingUserId,
																				 departmentUsers, emailSettings.Department, activeCalls, units, defaultPriority);

								if (call != null)
								{
									call.DepartmentId = departmentId.Value;

									var savedCall = _callsService.SaveCall(call);

									var cqi = new CallQueueItem();
									cqi.Call = savedCall;
									cqi.Profiles = _userProfileService.GetAllProfilesForDepartment(call.DepartmentId).Select(x => x.Value).ToList();
									cqi.DepartmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(cqi.Call.DepartmentId);

									_queueService.EnqueueCallBroadcast(cqi);

									return new HttpResponseMessage(HttpStatusCode.Created);
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								return new HttpResponseMessage(HttpStatusCode.InternalServerError);
							}
						}
						#endregion Dispatch
					}
					else if (type == 2) // Email List
					{
						#region Distribution Email
						var list = _distributionListsService.GetDistributionListByAddress(emailAddress);

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

													files.Add(_fileService.SaveFile(file));
												}
											}
										}
									}
									catch { }

									var dlqi = new DistributionListQueueItem();
									dlqi.List = list;
									dlqi.Users = _departmentsService.GetAllUsersForDepartment(list.DepartmentId);

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

									_queueService.EnqueueDistributionListBroadcast(dlqi);
								}
								catch (Exception ex)
								{
									Logging.LogException(ex);
									return new HttpResponseMessage(HttpStatusCode.InternalServerError);
								}
							}
							else
							{
								return new HttpResponseMessage(HttpStatusCode.Created);
							}
						}

						return new HttpResponseMessage(HttpStatusCode.Created);
						#endregion Distribution Email
					}
					if (type == 3)	// Group Dispatch
					{
						#region Group Dispatch Email
						var departmentGroup = _departmentGroupsService.GetGroupByDispatchEmailCode(emailAddress);

						if (departmentGroup != null)
						{
							try
							{
								var emailSettings = _departmentsService.GetDepartmentEmailSettings(departmentGroup.DepartmentId);
								//var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroup(departmentGroup.DepartmentGroupId);
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

									if (departmentGroup.Department != null)
										emailSettings.Department = departmentGroup.Department;
									else
										emailSettings.Department = _departmentsService.GetDepartmentById(departmentGroup.DepartmentId);
								}

								var activeCalls = _callsService.GetActiveCallsByDepartment(emailSettings.Department.DepartmentId);
								var units = _unitsService.GetAllUnitsForGroup(departmentGroup.DepartmentGroupId);

								var priorities = _callsService.GetActiveCallPrioritesForDepartment(emailSettings.Department.DepartmentId);
								int defaultPriority = (int)CallPriority.High;

								if (priorities != null && priorities.Any())
								{
									var defaultPrio = priorities.FirstOrDefault(x => x.IsDefault && x.IsDeleted == false);

									if (defaultPrio != null)
										defaultPriority = defaultPrio.DepartmentCallPriorityId;
								}

								var call = _callsService.GenerateCallFromEmail(emailSettings.FormatType, callEmail,
																				 emailSettings.Department.ManagingUserId,
																				 departmentGroupUsers.Select(x => x.User).ToList(), emailSettings.Department, activeCalls, units, defaultPriority);

								if (call != null)
								{
									call.DepartmentId = departmentGroup.DepartmentId;

									var savedCall = _callsService.SaveCall(call);

									var cqi = new CallQueueItem();
									cqi.Call = savedCall;
									cqi.Profiles = _userProfileService.GetSelectedUserProfiles(departmentGroupUsers.Select(x => x.UserId).ToList());
									cqi.DepartmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(cqi.Call.DepartmentId);

									_queueService.EnqueueCallBroadcast(cqi);

									return new HttpResponseMessage(HttpStatusCode.Created);
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								return new HttpResponseMessage(HttpStatusCode.InternalServerError);
							}
						}
						#endregion Group Dispatch Email
					}
					if (type == 4)	// Group Message
					{
						#region Group Message
						var departmentGroup = _departmentGroupsService.GetGroupByMessageEmailCode(emailAddress);

						if (departmentGroup != null)
						{
							try
							{
								//var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroup(departmentGroup.DepartmentGroupId);
								var departmentGroupUsers = _departmentGroupsService.GetAllMembersForGroupAndChildGroups(departmentGroup);

								var newMessage = new Message();
								newMessage.SentOn = DateTime.UtcNow;
								newMessage.SendingUserId = departmentGroup.Department.ManagingUserId;
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

								var savedMessage = _messageService.SaveMessage(newMessage);
								_messageService.SendMessage(savedMessage, "", departmentGroup.DepartmentId, false);

								return new HttpResponseMessage(HttpStatusCode.Created);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								return new HttpResponseMessage(HttpStatusCode.InternalServerError);
							}
						}

						#endregion Group Message
					}

					return new HttpResponseMessage(HttpStatusCode.InternalServerError);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					throw new HttpResponseException(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(ex.ToString()) });
				}
			}
			else
			{
				// If our message was null, we throw an exception
				throw new HttpResponseException(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("Error parsing Inbound Message, message is null.") });
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
