using System;
using System.Linq;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System.Web;
using MimeKit;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Message = Microsoft.Azure.ServiceBus.Message;

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
					//_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueEmailConnectionString, Config.ServiceBusConfig.EmailBroadcastQueueName);
					_client = new QueueClient(Config.ServiceBusConfig.AzureQueueEmailConnectionString, Config.ServiceBusConfig.EmailBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public async Task<bool> Process(DistributionListQueueItem item)
		{
			bool success = true;

			if (Config.SystemBehaviorConfig.IsAzure)
			{
				//ProcessQueueMessage(_client.Receive());

				var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
				{
					MaxConcurrentCalls = 1,
					AutoComplete = false
				};

				// Register the function that will process messages
				_client.RegisterMessageHandler(ProcessQueueMessage, messageHandlerOptions);
			}
			else
			{
				return await ProcessDistributionListQueueItem(item);
			}

			_queueService = null;
			return false;
		}

		public async Task<Tuple<bool, string>> ProcessQueueMessage(Message message, CancellationToken token)
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
						DistributionListQueueItem dlqi = null;
						try
						{
							dlqi = ObjectSerialization.Deserialize<DistributionListQueueItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							//message.Complete();
							await _client.CompleteAsync(message.SystemProperties.LockToken);
						}

						await ProcessDistributionListQueueItem(dlqi);
					}

					try
					{
						if (success)
							await _client.CompleteAsync(message.SystemProperties.LockToken);
							//message.Complete();
					}
					catch (MessageLockLostException)
					{

					}
				}
				catch (Exception ex)
				{
					result = ex.ToString();
					Logging.LogException(ex);
					//message.Abandon();
					await _client.DeadLetterAsync(message.SystemProperties.LockToken); 
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static async Task<bool> ProcessDistributionListQueueItem(DistributionListQueueItem dlqi)
		{
			var emailService = Bootstrapper.GetKernel().Resolve<IEmailService>();
			var distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();
			var fileService = Bootstrapper.GetKernel().Resolve<IFileService>();

			if (dlqi != null && dlqi.List != null && dlqi.Message != null)
			{
				// If we didn't get any profiles chances are the message size was too big for Azure, get selected profiles now.
				if (dlqi.Users == null)
				{
					var departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
					dlqi.Users = await departmentsService.GetAllUsersForDepartmentAsync(dlqi.List.DepartmentId);
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
							var file = await fileService.GetFileByIdAsync(fileId);

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

							fileService.DeleteFileAsync(file);
						}
					}
				}
				catch { }

				mailMessage.Body = builder.ToMessageBody();

				if (dlqi.List.Members == null)
					dlqi.List = await distributionListsService.GetDistributionListByIdAsync(dlqi.List.DistributionListId);

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
			distributionListsService = null;
			fileService = null;

			return true;
		}

		static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
		{
			//Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
			//var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
			//Console.WriteLine("Exception context for troubleshooting:");
			//Console.WriteLine($"- Endpoint: {context.Endpoint}");
			//Console.WriteLine($"- Entity Path: {context.EntityPath}");
			//Console.WriteLine($"- Executing Action: {context.Action}");
			return Task.CompletedTask;
		}
	}
}
