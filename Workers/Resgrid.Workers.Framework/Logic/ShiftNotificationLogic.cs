using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Message = Microsoft.Azure.ServiceBus.Message;

namespace Resgrid.Workers.Framework.Logic
{
	public class ShiftNotificationLogic
	{
		private QueueClient _client = null;

		public ShiftNotificationLogic()
		{
			while (_client == null)
			{
				try
				{
					//_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueShiftsConnectionString, Config.ServiceBusConfig.ShiftNotificationsQueueName);
					_client = new QueueClient(Config.ServiceBusConfig.AzureQueueShiftsConnectionString, Config.ServiceBusConfig.ShiftNotificationsQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(ShiftQueueItem item)
		{
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
				ProcessShiftQueueItem(item);
			}
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
						ShiftQueueItem sqi = null;
						try
						{
							sqi = ObjectSerialization.Deserialize<ShiftQueueItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							await _client.CompleteAsync(message.SystemProperties.LockToken);
							//message.Complete();
						}

						ProcessShiftQueueItem(sqi);
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
					Logging.LogException(ex);
					await _client.AbandonAsync(message.SystemProperties.LockToken); 
					//message.Abandon();
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static async Task<bool> ProcessShiftQueueItem(ShiftQueueItem sqi)
		{
			if (sqi != null)
			{
				var _shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();
				var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
				var _userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

				if (sqi.Type == (int)ShiftQueueTypes.TradeRequested)
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var text = _shiftsService.GenerateShiftTradeNotificationText(sourceUserProfile, tradeRequest);

					var userProfiles = await _userProfileService.GetSelectedUserProfilesAsync(tradeRequest.Users.Select(x => x.UserId).ToList());
					foreach (var user in tradeRequest.Users)
					{
						UserProfile profile = userProfiles.FirstOrDefault(x => x.UserId == user.UserId);
						_communicationService.SendNotificationAsync(user.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
							tradeRequest.SourceShiftSignup.Shift.Name, profile);
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeRejected && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var targetUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = _shiftsService.GenerateShiftTradeRejectionText(targetUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(sourceUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
							tradeRequest.SourceShiftSignup.Shift.Name, sourceUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeProposed && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var proposedUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = _shiftsService.GenerateShiftTradeProposedText(proposedUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(sourceUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
							tradeRequest.SourceShiftSignup.Shift.Name, sourceUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeFilled && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var proposedUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = _shiftsService.GenerateShiftTradeFilledText(sourceUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(proposedUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
							tradeRequest.SourceShiftSignup.Shift.Name, proposedUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.ShiftCreated)
				{
					var shift = await _shiftsService.GetShiftByIdAsync(sqi.ShiftId);
					var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(sqi.DepartmentId);

					var text = $"New Shift {shift.Name} has been created";

					foreach (var profile in profiles.Select(x => x.Value))
					{
						await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, "New Shift", profile);
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.ShiftUpdated)
				{
					var shift = await _shiftsService.GetShiftByIdAsync(sqi.ShiftId);
					var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(sqi.DepartmentId);

					var text = $"Shift {shift.Name} has been updated";

					foreach (var profile in shift.Personnel)
					{
						if (profiles.ContainsKey(profile.UserId))
							await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name, profiles[profile.UserId]);
						else
							await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name);
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.ShiftDaysAdded)
				{
					var shift = await _shiftsService.GetShiftByIdAsync(sqi.ShiftId);
					var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(sqi.DepartmentId);

					var text = $"Shift {shift.Name} has been updated";

					foreach (var profile in shift.Personnel)
					{
						if (profiles.ContainsKey(profile.UserId))
							await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name, profiles[profile.UserId]);
						else
							await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name);
					}
				}

				_shiftsService = null;
				_communicationService = null;
				_userProfileService = null;
			}

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
