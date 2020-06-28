using Autofac;
using MimeKit;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework;
using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Identity;

namespace Resgrid.EmailProcessor
{
	public class SampleMessageStore : MessageStore
	{
		/// <summary>
		/// Save the given message to the underlying storage system.
		/// </summary>
		/// <param name="context">The session context.</param>
		/// <param name="transaction">The SMTP message transaction to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>A unique identifier that represents this message in the underlying message store.</returns>
		public override Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, CancellationToken cancellationToken)
		{
			var textMessage = (ITextMessage)transaction.Message;
			MimeMessage mailMessage = MimeMessage.Load(textMessage.Content);

			var _departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
			var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
			var _callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
			var _unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();
			var _userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
			var _queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
			var _distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();
			var _departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();
			var _messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();

			try
			{

				//if (message.FromFull != null && !String.IsNullOrWhiteSpace(message.FromFull.Email) && message.FromFull.Email.Trim() == "support@postmarkapp.com")
				//	return new HttpResponseMessage(HttpStatusCode.Created);

				if (mailMessage.From == null || mailMessage.From.Count <= 0)
					mailMessage.From.Add(new MailboxAddress("Inbound Email Dispatch", "do-not-reply@resgrid.com"));


				if (String.IsNullOrWhiteSpace(mailMessage.Subject))
					mailMessage.Subject = "Dispatch Email";

				var builder = new BodyBuilder();

				int type = 0; // 1 = dispatch // 2 = email list // 3 = group dispatch // 4 = group message 
				string emailAddress = String.Empty;
				string bounceEmail = String.Empty;
				string name = String.Empty;

				#region Trying to Find What type of email this is
				foreach (var email in mailMessage.To.Mailboxes)
				{
					if (StringHelpers.ValidateEmail(email.Address))
					{
						if (email.Address.Contains($"@{Config.InboundEmailConfig.DispatchDomain}") || email.Address.Contains($"@{Config.InboundEmailConfig.DispatchTestDomain}"))
						{
							type = 1;

							if (email.Address.Contains($"@{Config.InboundEmailConfig.DispatchDomain}"))
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.DispatchDomain}", "");
							else
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.DispatchTestDomain}", "");

							name = email.Name;
							mailMessage.To.Clear();
							mailMessage.To.Add(new MailboxAddress(email.Address, email.Address));

							break;
						}
						else if (email.Address.Contains($"@{Config.InboundEmailConfig.ListsDomain}") || email.Address.Contains($"@{Config.InboundEmailConfig.ListsTestDomain}"))
						{
							type = 2;

							if (email.Address.Contains($"@{Config.InboundEmailConfig.ListsDomain}"))
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.ListsDomain}", "");
							else
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.ListsTestDomain}", "");

							if (emailAddress.Contains("+") && emailAddress.Contains("="))
							{
								var tempBounceEmail = emailAddress.Substring(emailAddress.IndexOf("+") + 1);
								bounceEmail = tempBounceEmail.Replace("=", "@");

								emailAddress = emailAddress.Replace(tempBounceEmail, "");
								emailAddress = emailAddress.Replace("+", "");
							}

							name = email.Name;
							mailMessage.To.Clear();
							mailMessage.To.Add(new MailboxAddress(email.Name, email.Address));

							break;
						}
						else if (email.Address.Contains($"@{Config.InboundEmailConfig.GroupsDomain}") || email.Address.Contains($"@{Config.InboundEmailConfig.GroupsTestDomain}"))
						{
							type = 3;

							if (email.Address.Contains($"@{Config.InboundEmailConfig.GroupsDomain}"))
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.GroupsDomain}", "");
							else
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.GroupsTestDomain}", "");

							name = email.Name;
							mailMessage.To.Clear();
							mailMessage.To.Add(new MailboxAddress(email.Name, email.Address));

							break;
						}
						else if (email.Address.Contains($"@{Config.InboundEmailConfig.GroupMessageDomain}") || email.Address.Contains($"@{Config.InboundEmailConfig.GroupTestMessageDomain}"))
						{
							type = 4;

							if (email.Address.Contains($"@{Config.InboundEmailConfig.GroupMessageDomain}"))
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.GroupMessageDomain}", "");
							else
								emailAddress = email.Address.Replace($"@{Config.InboundEmailConfig.GroupTestMessageDomain}", "");

							name = email.Name;
							mailMessage.To.Clear();
							mailMessage.To.Add(new MailboxAddress(email.Name, email.Address));

							break;
						}
					}
				}

				// Some providers aren't putting email address in the To line, process the CC line
				if (type == 0)
				{
					foreach (var email in mailMessage.Cc.Mailboxes)
					{
						if (StringHelpers.ValidateEmail(email.Address))
						{
							var proccedEmailInfo = ProcessEmailAddress(email.Address);

							if (proccedEmailInfo.Item1 > 0)
							{
								type = proccedEmailInfo.Item1;
								emailAddress = proccedEmailInfo.Item2;

								mailMessage.To.Clear();
								mailMessage.To.Add(new MailboxAddress(email.Name, email.Address));
							}
						}
					}
				}

				// If To and CC didn't work, try the header.
				if (type == 0)
				{
					try
					{
						if (mailMessage.Headers != null && mailMessage.Headers.Count > 0)
						{
							var header = mailMessage.Headers.FirstOrDefault(x => x.Field == "Received-SPF");

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
					var departmentId = _departmentSettingsService.GetDepartmentIdForDispatchEmail(emailAddress);

					if (departmentId.HasValue)
					{
						try
						{
							var emailSettings = _departmentsService.GetDepartmentEmailSettings(departmentId.Value);
							List<IdentityUser> departmentUsers = _departmentsService.GetAllUsersForDepartment(departmentId.Value, true);

							var callEmail = new CallEmail();

							if (!String.IsNullOrWhiteSpace(mailMessage.Subject))
								callEmail.Subject = mailMessage.Subject;

							else
								callEmail.Subject = "Dispatch Email";

							if (!String.IsNullOrWhiteSpace(mailMessage.HtmlBody))
								//callEmail.Body = HttpUtility.HtmlDecode(mailMessage.HtmlBody);
								callEmail.Body = mailMessage.HtmlBody;
							else
								callEmail.Body = mailMessage.TextBody;

							callEmail.TextBody = mailMessage.TextBody;

							foreach (var attachment in mailMessage.Attachments)
							{
								try
								{
									var fileName = attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name;
									//if (fileName.Contains(".mp3") || fileName.Contains(".amr"))
									//{
									//	byte[] filebytes;
									//	using (var memory = new MemoryStream())
									//	{
									//		if (attachment is MimePart)
									//			((MimePart)attachment).ContentBase.DecodeTo(memory);
									//		else
									//			((MessagePart)attachment).Message.WriteTo(memory);

									//		filebytes = memory.ToArray();
									//	}

									//	if (attachment is MessagePart)
									//	{
									//		var rfc822 = (MessagePart)attachment;
									//		rfc822.Message.WriteTo(stream);
									//	}
									//	else
									//	{
									//		var part = (MimePart)attachment;
									//		part.Content.DecodeTo(stream);
									//	}

									//	byte[] filebytes = Convert.FromBase64String(attachment.Content);

									//	callEmail.DispatchAudioFileName = attachment.Name;
									//	callEmail.DispatchAudio = filebytes;
									//}

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

								//return new HttpResponseMessage(HttpStatusCode.Created);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
							//return new HttpResponseMessage(HttpStatusCode.InternalServerError);
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

								//try
								//{
								//	if (message.Attachments != null && message.Attachments.Any())
								//	{
								//		foreach (var attachment in message.Attachments)
								//		{
								//			if (Convert.ToInt32(attachment.ContentLength) > 0)
								//			{
								//				Model.File file = new Model.File();

								//				byte[] filebytes = Convert.FromBase64String(attachment.Content);

								//				file.Data = filebytes;
								//				file.FileName = attachment.Name;
								//				file.DepartmentId = list.DepartmentId;
								//				file.ContentId = attachment.ContentID;
								//				file.FileType = attachment.ContentType;
								//				file.Timestamp = DateTime.UtcNow;

								//				files.Add(_fileService.SaveFile(file));
								//			}
								//		}
								//	}
								//}
								//catch { }

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

								dlqi.Message.Subject = mailMessage.Subject;
								dlqi.Message.HtmlBody = mailMessage.HtmlBody;
								dlqi.Message.TextBody = mailMessage.TextBody;
								dlqi.Message.MessageID = mailMessage.MessageId;

								_queueService.EnqueueDistributionListBroadcast(dlqi);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								//return new HttpResponseMessage(HttpStatusCode.InternalServerError);
							}
						}
						else
						{
							//return new HttpResponseMessage(HttpStatusCode.Created);
						}
					}

					//return new HttpResponseMessage(HttpStatusCode.Created);
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
							callEmail.Subject = mailMessage.Subject;

							if (!String.IsNullOrWhiteSpace(mailMessage.HtmlBody))
								//callEmail.Body = HttpUtility.HtmlDecode(message.HtmlBody);
								callEmail.Body = mailMessage.HtmlBody;
							else
								callEmail.Body = mailMessage.TextBody;

							//foreach (var attachment in mailMessage.Attachments)
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

								//return new HttpResponseMessage(HttpStatusCode.Created);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
							//return new HttpResponseMessage(HttpStatusCode.InternalServerError);
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
							newMessage.Subject = mailMessage.Subject;
							newMessage.SystemGenerated = true;

							if (!String.IsNullOrWhiteSpace(mailMessage.HtmlBody))
								//newMessage.Body = HttpUtility.HtmlDecode(message.HtmlBody);
								newMessage.Body = mailMessage.HtmlBody;
							else
								newMessage.Body = mailMessage.TextBody;

							foreach (var member in departmentGroupUsers)
							{
								if (newMessage.GetRecipients().All(x => x != member.UserId))
									newMessage.AddRecipient(member.UserId);
							}

							var savedMessage = _messageService.SaveMessage(newMessage);
							_messageService.SendMessage(savedMessage, "", departmentGroup.DepartmentId, false);

							//return new HttpResponseMessage(HttpStatusCode.Created);
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
							//return new HttpResponseMessage(HttpStatusCode.InternalServerError);
						}
					}

					#endregion Group Message
				}

				//return new HttpResponseMessage(HttpStatusCode.InternalServerError);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				//throw new HttpResponseException(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(ex.ToString()) });
			}

			return Task.FromResult(SmtpResponse.Ok);
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
