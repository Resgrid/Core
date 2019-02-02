using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Linq;
using Autofac;

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
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueShiftsConnectionString, Config.ServiceBusConfig.ShiftNotificationsQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(ShiftQueueItem item)
		{
			ProcessQueueMessage(_client.Receive());
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
						ShiftQueueItem sqi = null;
						try
						{
							sqi = ObjectSerialization.Deserialize<ShiftQueueItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							message.Complete();
						}

						if (sqi != null)
						{
							var _shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();
							var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
							var _userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

							if (sqi.Type == (int)ShiftQueueTypes.TradeRequested)
							{
								var tradeRequest = _shiftsService.GetShiftTradeById(sqi.ShiftSignupTradeId);
								var sourceUserProfile = _userProfileService.GetProfileByUserId(tradeRequest.SourceShiftSignup.UserId);
								var text = _shiftsService.GenerateShiftTradeNotificationText(sourceUserProfile, tradeRequest);

								var userProfiles = _userProfileService.GetSelectedUserProfiles(tradeRequest.Users.Select(x => x.UserId).ToList());
								foreach (var user in tradeRequest.Users)
								{
									UserProfile profile = userProfiles.FirstOrDefault(x => x.UserId == user.UserId);
									_communicationService.SendNotification(user.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
										tradeRequest.SourceShiftSignup.Shift.Name, profile);
								}
							}
							else if (sqi.Type == (int)ShiftQueueTypes.TradeRejected && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
							{
								var tradeRequest = _shiftsService.GetShiftTradeById(sqi.ShiftSignupTradeId);
								var sourceUserProfile = _userProfileService.GetProfileByUserId(tradeRequest.SourceShiftSignup.UserId);
								var targetUserProfile = _userProfileService.GetProfileByUserId(sqi.SourceUserId);

								var text = _shiftsService.GenerateShiftTradeRejectionText(targetUserProfile, tradeRequest);

								_communicationService.SendNotification(sourceUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
										tradeRequest.SourceShiftSignup.Shift.Name, sourceUserProfile);
							}
							else if (sqi.Type == (int)ShiftQueueTypes.TradeProposed && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
							{
								var tradeRequest = _shiftsService.GetShiftTradeById(sqi.ShiftSignupTradeId);
								var sourceUserProfile = _userProfileService.GetProfileByUserId(tradeRequest.SourceShiftSignup.UserId);
								var proposedUserProfile = _userProfileService.GetProfileByUserId(sqi.SourceUserId);

								var text = _shiftsService.GenerateShiftTradeProposedText(proposedUserProfile, tradeRequest);

								_communicationService.SendNotification(sourceUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
										tradeRequest.SourceShiftSignup.Shift.Name, sourceUserProfile);
							}
							else if (sqi.Type == (int)ShiftQueueTypes.TradeFilled && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
							{
								var tradeRequest = _shiftsService.GetShiftTradeById(sqi.ShiftSignupTradeId);
								var sourceUserProfile = _userProfileService.GetProfileByUserId(tradeRequest.SourceShiftSignup.UserId);
								var proposedUserProfile = _userProfileService.GetProfileByUserId(sqi.SourceUserId);

								var text = _shiftsService.GenerateShiftTradeFilledText(sourceUserProfile, tradeRequest);

								_communicationService.SendNotification(proposedUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
										tradeRequest.SourceShiftSignup.Shift.Name, proposedUserProfile);
							}
							else if (sqi.Type == (int) ShiftQueueTypes.ShiftCreated)
							{
								var shift = _shiftsService.GetShiftById(sqi.ShiftId);
								var profiles = _userProfileService.GetAllProfilesForDepartment(sqi.DepartmentId);

								var text = $"New Shift {shift.Name} has been created";

								foreach (var profile in profiles.Select(x => x.Value))
								{ 
									_communicationService.SendNotification(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, "New Shift", profile);
								}
							}
							else if (sqi.Type == (int)ShiftQueueTypes.ShiftUpdated)
							{
								var shift = _shiftsService.GetShiftById(sqi.ShiftId);
								var profiles = _userProfileService.GetAllProfilesForDepartment(sqi.DepartmentId);

								var text = $"Shift {shift.Name} has been updated";

								foreach (var profile in shift.Personnel)
								{
									if (profiles.ContainsKey(profile.UserId))
										_communicationService.SendNotification(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name, profiles[profile.UserId]);
									else
										_communicationService.SendNotification(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name);
								}
							}
							else if (sqi.Type == (int)ShiftQueueTypes.ShiftDaysAdded)
							{
								var shift = _shiftsService.GetShiftById(sqi.ShiftId);
								var profiles = _userProfileService.GetAllProfilesForDepartment(sqi.DepartmentId);

								var text = $"Shift {shift.Name} has been updated";

								foreach (var profile in shift.Personnel)
								{
									if (profiles.ContainsKey(profile.UserId))
										_communicationService.SendNotification(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name, profiles[profile.UserId]);
									else
										_communicationService.SendNotification(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name);
								}
							}

							_shiftsService = null;
							_communicationService = null;
							_userProfileService = null;
						}
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
					Logging.LogException(ex);
					message.Abandon();
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
