using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class ShiftNotificationLogic
	{
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
						await _communicationService.SendNotificationAsync(user.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber,
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

					if (shift.Personnel != null && shift.Personnel.Any())
					{
						foreach (var profile in shift.Personnel)
						{
							if (profiles.ContainsKey(profile.UserId))
								await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name, profiles[profile.UserId]);
							else
								await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, shift.Name);
						}
					}
				}

				_shiftsService = null;
				_communicationService = null;
				_userProfileService = null;
			}

			return true;
		}
	}
}
