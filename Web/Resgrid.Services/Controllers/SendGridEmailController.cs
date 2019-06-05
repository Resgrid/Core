/*using Microsoft.AspNet.Identity.EntityFramework6;
using MimeKit;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;

namespace Resgrid.Web.Services.Controllers
{
	[EnableCors(origins: "*", headers: "*", methods: "*")]
	[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
	public class SendGridEmailController : ApiController
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

		public SendGridEmailController(IDepartmentSettingsService departmentSettingsService, INumbersService numbersService,
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

		[HttpPost]
		[EnableCors(origins: "*", headers: "*", methods: "*")]
		public async Task<HttpResponseMessage> Receive()
		{

			try
			{
				string root = HttpContext.Current.Server.MapPath("~/App_Data");
				var provider = new MultipartFormDataStreamProvider(root);

				StringBuilder sb = new StringBuilder(); // Holds the response body

				// Read the form data and return an async task.
				await Request.Content.ReadAsMultipartAsync(provider);

				//Bind to incoming email model
				Models.EmailModel message = new Models.EmailModel
				{
					to = provider.FormData.GetValues("to").SingleOrDefault(),
					from = provider.FormData.GetValues("from").SingleOrDefault(),
					subject = provider.FormData.GetValues("subject").SingleOrDefault(),
					html = provider.FormData.GetValues("html").SingleOrDefault(),
					sender_ip = provider.FormData.GetValues("sender_ip").SingleOrDefault(),
					headers = provider.FormData.GetValues("headers").SingleOrDefault(),
					dkim = provider.FormData.GetValues("dkim").SingleOrDefault(),
					text = provider.FormData.GetValues("text").SingleOrDefault(),
					SPF = provider.FormData.GetValues("spf").SingleOrDefault(),
					attachments = provider.FormData.GetValues("attachments").SingleOrDefault(),
					envelope = provider.FormData.GetValues("envelope").SingleOrDefault(),
					charsets = provider.FormData.GetValues("charsets").SingleOrDefault()
				};

				var mailMessage = new MimeMessage();
				int type = 0; // 1 = dispatch // 2 = email list // 3 = group dispatch // 4 = group message 
				string emailAddress = String.Empty;
				string bounceEmail = String.Empty;
				string name = String.Empty;


				#region Trying to Find What type of email this is
				if (StringHelpers.ValidateEmail(message.to))
				{
					if (message.to.Contains($"@{Config.InboundEmailConfig.DispatchDomain}") || message.to.Contains($"@{Config.InboundEmailConfig.DispatchTestDomain}"))
					{
						type = 1;

						if (message.to.Contains($"@{Config.InboundEmailConfig.DispatchDomain}"))
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.DispatchDomain}", "");
						else
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.DispatchTestDomain}", "");

						//name = email.Name;
						name = message.to;
						mailMessage.To.Clear();
						mailMessage.To.Add(new MailboxAddress("", message.to));
					}
					else if (message.to.Contains($"@{Config.InboundEmailConfig.ListsDomain}") || message.to.Contains($"@{Config.InboundEmailConfig.ListsTestDomain}"))
					{
						type = 2;

						if (message.to.Contains($"@{Config.InboundEmailConfig.ListsDomain}"))
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.ListsDomain}", "");
						else
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.ListsTestDomain}", "");

						if (emailAddress.Contains("+") && emailAddress.Contains("="))
						{
							var tempBounceEmail = emailAddress.Substring(emailAddress.IndexOf("+") + 1);
							bounceEmail = tempBounceEmail.Replace("=", "@");

							emailAddress = emailAddress.Replace(tempBounceEmail, "");
							emailAddress = emailAddress.Replace("+", "");
						}

						//name = email.Name;
						name = message.to;
						mailMessage.To.Clear();
						mailMessage.To.Add(new MailboxAddress("", message.to));
					}
					else if (message.to.Contains($"@{Config.InboundEmailConfig.GroupsDomain}") || message.to.Contains($"@{Config.InboundEmailConfig.GroupsTestDomain}"))
					{
						type = 3;

						if (message.to.Contains($"@{Config.InboundEmailConfig.GroupsDomain}"))
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.GroupsDomain}", "");
						else
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.GroupsTestDomain}", "");

						//name = email.Name;
						name = message.to;
						mailMessage.To.Clear();
						mailMessage.To.Add(new MailboxAddress("", message.to));
					}
					else if (message.to.Contains($"@{Config.InboundEmailConfig.GroupMessageDomain}") || message.to.Contains($"@{Config.InboundEmailConfig.GroupTestMessageDomain}"))
					{
						type = 4;

						if (message.to.Contains($"@{Config.InboundEmailConfig.GroupMessageDomain}"))
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.GroupMessageDomain}", "");
						else
							emailAddress = message.to.Replace($"@{Config.InboundEmailConfig.GroupTestMessageDomain}", "");

						//name = email.Name;
						name = message.to;
						mailMessage.To.Clear();
						mailMessage.To.Add(new MailboxAddress("", message.to));
					}
				}
				#endregion Trying to Find What type of email this is

				if (type == 1)  // Dispatch
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

							if (!String.IsNullOrWhiteSpace(message.subject))
								callEmail.Subject = message.subject;

							else
								callEmail.Subject = "Dispatch Email";

							if (!String.IsNullOrWhiteSpace(message.html))
								callEmail.Body = HttpUtility.HtmlDecode(message.html);
							else
								callEmail.Body = message.text;

							callEmail.TextBody = message.text;

							//foreach (var attachment in message.Attachments)
							//{
							//	try
							//	{
							//		if (Convert.ToInt32(attachment.ContentLength) > 0)
							//		{
							//			if (attachment.Name.Contains(".mp3") || attachment.Name.Contains(".amr"))
							//			{
							//				byte[] filebytes = Convert.FromBase64String(attachment.Content);

							//				callEmail.DispatchAudioFileName = attachment.Name;
							//				callEmail.DispatchAudio = filebytes;
							//			}
							//		}
							//	}
							//	catch { }
							//}

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
							int defaultPriority = (int)CallPriority.High;

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
									//if (message.Attachments != null && message.Attachments.Any())
									//{
									//	foreach (var attachment in message.Attachments)
									//	{
									//		if (Convert.ToInt32(attachment.ContentLength) > 0)
									//		{
									//			Model.File file = new Model.File();

									//			byte[] filebytes = Convert.FromBase64String(attachment.Content);

									//			file.Data = filebytes;
									//			file.FileName = attachment.Name;
									//			file.DepartmentId = list.DepartmentId;
									//			file.ContentId = attachment.ContentID;
									//			file.FileType = attachment.ContentType;
									//			file.Timestamp = DateTime.UtcNow;

									//			files.Add(_fileService.SaveFile(file));
									//		}
									//	}
									//}
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

								//if (message.FromFull != null && !String.IsNullOrWhiteSpace(message.FromFull.Email) && !String.IsNullOrWhiteSpace(message.FromFull.Name))
								//{
								//	dlqi.Message.FromEmail = message.FromFull.Email.Trim();
								//	dlqi.Message.FromName = message.FromFull.Name.Trim();
								//}

								dlqi.Message.Subject = message.subject;
								dlqi.Message.HtmlBody = message.html;
								dlqi.Message.TextBody = message.text;
								dlqi.Message.MessageID = message.dkim;

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
				if (type == 3)  // Group Dispatch
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
							callEmail.Subject = message.subject;

							if (!String.IsNullOrWhiteSpace(message.html))
								callEmail.Body = HttpUtility.HtmlDecode(message.html);
							else
								callEmail.Body = message.text;

							//foreach (var attachment in message.Attachments)
							//{
							//	try
							//	{
							//		if (Convert.ToInt32(attachment.ContentLength) > 0)
							//		{
							//			if (attachment.Name.Contains(".mp3") || attachment.Name.Contains(".amr"))
							//			{
							//				byte[] filebytes = Convert.FromBase64String(attachment.Content);

							//				callEmail.DispatchAudioFileName = attachment.Name;
							//				callEmail.DispatchAudio = filebytes;
							//			}
							//		}
							//	}
							//	catch { }
							//}

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
				if (type == 4)  // Group Message
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
							newMessage.Subject = message.subject;
							newMessage.SystemGenerated = true;

							if (!String.IsNullOrWhiteSpace(message.html))
								newMessage.Body = HttpUtility.HtmlDecode(message.html);
							else
								newMessage.Body = message.text;

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

				//return new HttpResponseMessage(HttpStatusCode.OK);

			}
			catch
			{
				throw;
			}
		}
	}
}
*/
