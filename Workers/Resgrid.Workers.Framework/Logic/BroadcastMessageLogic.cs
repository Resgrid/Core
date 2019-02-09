using System;
using System.Linq;
using Autofac;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Newtonsoft.Json;

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
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueMessageConnectionString, Config.ServiceBusConfig.MessageBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(MessageQueueItem item)
		{
			bool success = true;

			if (Config.SystemBehaviorConfig.IsAzure)
			{
				ProcessQueueMessage(_client.Receive());
			}
			else
			{
				ProcessMessageQueueItem(item);
			}

			_queueService = null;
		}

		public static Tuple<bool, string> ProcessQueueMessage(BrokeredMessage message)
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
							message.Complete();
						}

						ProcessMessageQueueItem(mqi);
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
					message.Abandon();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static void ProcessMessageQueueItem(MessageQueueItem mqi)
		{
			var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();

			if (mqi != null && mqi.Message == null && mqi.MessageId != 0)
			{
				var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
				mqi.Message = messageService.GetMessageById(mqi.MessageId);
			}

			if (mqi != null && mqi.Message != null)
			{
				if (mqi.Message.MessageRecipients == null || mqi.Message.MessageRecipients.Count <= 0)
				{
					var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
					mqi.Message = messageService.GetMessageById(mqi.Message.MessageId);
				}

				// If we didn't get any profiles chances are the message size was too big for Azure, get selected profiles now.
				if (mqi.Profiles == null)
				{
					var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

					if (mqi.Message.MessageRecipients != null && mqi.Message.MessageRecipients.Any())
					{
						mqi.Profiles = userProfileService.GetSelectedUserProfiles(mqi.Message.MessageRecipients.Select(x => x.UserId).ToList());
					}
					else
					{
						mqi.Profiles = userProfileService.GetAllProfilesForDepartment(mqi.DepartmentId).Select(x => x.Value).ToList();
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
							_communicationService.SendMessage(mqi.Message, name, mqi.DepartmentTextNumber, mqi.DepartmentId, sendingToProfile);
						}
						else
						{
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
							var sender = userProfileService.GetProfileByUserId(mqi.Message.SendingUserId);

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
							_communicationService.SendMessage(mqi.Message, name, mqi.DepartmentTextNumber, mqi.DepartmentId, sendingToProfile);
						}
					}
				}
			}

			_communicationService = null;
		}
	}
}
