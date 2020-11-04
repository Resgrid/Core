using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Newtonsoft.Json;
using Message = Microsoft.Azure.ServiceBus.Message;

namespace Resgrid.Workers.Framework.Logic
{
	public class BroadcastMessageLogic
	{
		private IQueueService _queueService;
		private QueueClient _client = null;

		public BroadcastMessageLogic()
		{
			while (_client == null)
			{
				try
				{
					_client = new QueueClient(Config.ServiceBusConfig.AzureQueueMessageConnectionString, Config.ServiceBusConfig.MessageBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public async Task<bool> Process(MessageQueueItem item)
		{
			bool success = true;

			if (Config.SystemBehaviorConfig.IsAzure)
			{
				var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
				{
					MaxConcurrentCalls = 1,
					AutoComplete = false
				};

				// Register the function that will process messages
				_client.RegisterMessageHandler(ProcessQueueMessage, messageHandlerOptions);

				//await ProcessQueueMessage(_client.ReceiveAsync());
			}
			else
			{
				return await ProcessMessageQueueItem(item);
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
				MessageQueueItem mqi = null;

				try
				{
					var body = message.GetBody<string>();

					if (!String.IsNullOrWhiteSpace(body))
					{
						try
						{
							mqi = ObjectSerialization.Deserialize<MessageQueueItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							//message.Complete();
							await _client.CompleteAsync(message.SystemProperties.LockToken);
						}

						await ProcessMessageQueueItem(mqi);
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

					if (mqi != null)
					{
						ex.Data.Add("DepartmentId", mqi.DepartmentId);

						if (mqi.Message != null)
						{
							ex.Data.Add("MessageId", mqi.Message.MessageId);
							ex.Data.Add("SendingUserId", mqi.Message.SendingUserId);
							ex.Data.Add("RecievingUserId", mqi.Message.ReceivingUserId);
						}
					}

					ex.Data.Add("MQI", JsonConvert.SerializeObject(mqi));

					Logging.LogException(ex);
					await _client.AbandonAsync(message.SystemProperties.LockToken); 
					//message.Abandon();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static async Task<bool> ProcessMessageQueueItem(MessageQueueItem mqi)
		{
			var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();

			if (mqi != null && mqi.Message == null && mqi.MessageId != 0)
			{
				var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
				mqi.Message = await messageService.GetMessageByIdAsync(mqi.MessageId);
			}

			if (mqi != null && mqi.Message != null)
			{
				if (mqi.Message.MessageRecipients == null || mqi.Message.MessageRecipients.Count <= 0)
				{
					var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
					mqi.Message = await messageService.GetMessageByIdAsync(mqi.Message.MessageId);
				}

				// If we didn't get any profiles chances are the message size was too big for Azure, get selected profiles now.
				if (mqi.Profiles == null)
				{
					var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

					if (mqi.Message.MessageRecipients != null && mqi.Message.MessageRecipients.Any())
					{
						mqi.Profiles = await userProfileService.GetSelectedUserProfilesAsync(mqi.Message.MessageRecipients.Select(x => x.UserId).ToList());
					}
					else
					{
						mqi.Profiles = (await userProfileService.GetAllProfilesForDepartmentAsync(mqi.DepartmentId)).Select(x => x.Value).ToList();
					}
				}

				string name = string.Empty;
				if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId))
				{
					var profile = mqi.Profiles.FirstOrDefault(x => x.UserId == mqi.Message.SendingUserId);

					if (profile != null)
						name = profile.FullName.AsFirstNameLastName;
				}

				if (mqi.Message.ReceivingUserId != null && (mqi.Message.Recipients == null || !mqi.Message.Recipients.Any()))
				{
					if (mqi.Profiles != null)
					{
						var sendingToProfile = mqi.Profiles.FirstOrDefault(x => x.UserId == mqi.Message.ReceivingUserId);

						if (sendingToProfile != null)
						{
							await _communicationService.SendMessageAsync(mqi.Message, name, mqi.DepartmentTextNumber, mqi.DepartmentId, sendingToProfile);
						}
						else
						{
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
							var sender = await userProfileService.GetProfileByUserIdAsync(mqi.Message.SendingUserId);

							if (sender != null)
								name = sender.FullName.AsFirstNameLastName;
						}
					}
				}
				else if (mqi.Message.MessageRecipients != null && mqi.Message.MessageRecipients.Any())
				{
					foreach (var recipient in mqi.Message.MessageRecipients)
					{
						var sendingToProfile = mqi.Profiles.FirstOrDefault(x => x.UserId == recipient.UserId);
						mqi.Message.ReceivingUserId = recipient.UserId;

						if (sendingToProfile != null)
						{
							await _communicationService.SendMessageAsync(mqi.Message, name, mqi.DepartmentTextNumber, mqi.DepartmentId, sendingToProfile);
						}
					}
				}
			}

			_communicationService = null;
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
