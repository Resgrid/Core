using System;
using System.Linq;
using Autofac;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System.Web;
using MimeKit;
using System.IO;

namespace Resgrid.Workers.Framework.Logic
{
	public class DistributionListLogic
	{
		private IQueueService _queueService;
		private QueueClient _client = null;

		public DistributionListLogic()
		{
			while (_client == null)
			{
				try
				{
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueEmailConnectionString, Config.ServiceBusConfig.EmailBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(DistributionListQueueItem item)
		{
			bool success = true;

			if (Config.SystemBehaviorConfig.IsAzure)
			{
				ProcessQueueMessage(_client.Receive());
			}
			//else
			//{
			//	if (item != null && item.Message != null)
			//	{
			//		_queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
			//		var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();

			//		try
			//		{
			//			string name = string.Empty;
			//			if (!String.IsNullOrWhiteSpace(item.Message.SendingUserId))
			//			{
			//				var profile = item.Profiles.FirstOrDefault(x => x.UserId == item.Message.SendingUserId);

			//				if (profile != null)
			//					name = profile.FullName.AsFirstNameLastName;
			//			}

			//			var sendingToProfile = item.Profiles.FirstOrDefault(x => x.UserId == item.Message.ReceivingUserId);

			//			_communicationService.SendMessage(item.Message, name, item.DepartmentTextNumber, sendingToProfile);
			//		}
			//		catch (System.Net.Sockets.SocketException ex)
			//		{
			//		}
			//		catch (Exception ex)
			//		{
			//			Logging.LogException(ex);
			//			_queueService.Requeue(item.QueueItem);

			//			success = false;
			//		}


			//		if (success)
			//			_queueService.SetQueueItemCompleted(item.QueueItem.QueueItemId);

			//		_communicationService = null;
			//	}
			//	else
			//	{
			//		if (item != null)
			//			_queueService.SetQueueItemCompleted(item.QueueItem.QueueItemId);
			//	}
			//}

			_queueService = null;
		}

		public static Tuple<bool, string> ProcessQueueMessage(BrokeredMessage message)
		{
			bool success = true;
			string result = "";

			if (message != null)
			{
				try
				{
					var body = message.GetBody<string>();

					if (!String.IsNullOrWhiteSpace(body))
					{
						var emailService = Bootstrapper.GetKernel().Resolve<IEmailService>();
						var distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();
						var fileService = Bootstrapper.GetKernel().Resolve<IFileService>();

						DistributionListQueueItem dlqi = null;
						try
						{
							dlqi = ObjectSerialization.Deserialize<DistributionListQueueItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							message.Complete();
						}

						if (dlqi != null && dlqi.List != null && dlqi.Message != null)
						{
							// If we didn't get any profiles chances are the message size was too big for Azure, get selected profiles now.
							if (dlqi.Users == null)
							{
								var departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
								dlqi.Users = departmentsService.GetAllUsersForDepartment(dlqi.List.DepartmentId);
							}

							var mailMessage = new MimeMessage();
							var builder = new BodyBuilder();


							if (!String.IsNullOrWhiteSpace(dlqi.Message.HtmlBody))
								builder.HtmlBody = HttpUtility.HtmlDecode(dlqi.Message.HtmlBody);

							if (!String.IsNullOrWhiteSpace(dlqi.Message.TextBody))
								builder.TextBody = dlqi.Message.TextBody;

							mailMessage.Subject = dlqi.Message.Subject;

							//mailMessage.From = new EmailAddress($"{dlqi.List.EmailAddress}@{Config.InboundEmailConfig.ListsDomain}", $"Resgrid ({dlqi.List.Name}) List");

							try
							{
								if (dlqi.FileIds != null && dlqi.FileIds.Any())
								{
									foreach (var fileId in dlqi.FileIds)
									{
										var file = fileService.GetFileById(fileId);

										// create an image attachment for the file located at path
										var attachment = new MimePart(file.FileType)
										{
											ContentObject = new ContentObject(new MemoryStream(file.Data), ContentEncoding.Default),
											ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
											ContentTransferEncoding = ContentEncoding.Base64,
											FileName = file.FileName
										};

										builder.Attachments.Add(attachment);

										//mailMessage.Attachments.Add(file.Data, file.FileName, file.ContentId, file.FileType,
										//	new HeaderCollection(),	NewAttachmentOptions.None, MailTransferEncoding.None);

										fileService.DeleteFile(file);
									}
								}
							}
							catch { }

							mailMessage.Body = builder.ToMessageBody();

							if (dlqi.List.Members == null)
								dlqi.List = distributionListsService.GetDistributionListById(dlqi.List.DistributionListId);

							foreach (var member in dlqi.List.Members)
							{
								try
								{
									var user = dlqi.Users.FirstOrDefault(x => x.UserId == member.UserId);

									if (user != null && !String.IsNullOrWhiteSpace(user.Email))
										emailService.SendDistributionListEmail(mailMessage, user.Email, dlqi.List.Name, dlqi.List.Name, $"{dlqi.List.EmailAddress}@lists.resgrid.com");
								}
								catch (Exception ex)
								{
									Logging.LogException(ex);
								}
							}
						}

						emailService = null;
					}

					try
					{
						if (success)
							message.Complete();
					}
					catch (MessageLockLostException)
					{

					}
				}
				catch (Exception ex)
				{
					result = ex.ToString();
					Logging.LogException(ex);
					message.Abandon();
				}
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
