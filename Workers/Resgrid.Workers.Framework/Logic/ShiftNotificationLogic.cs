using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
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
				var _departmentService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();

				var department = await _departmentService.GetDepartmentByIdAsync(sqi.DepartmentId, false);

				if (sqi.Type == (int)ShiftQueueTypes.TradeRequested)
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);

					if (tradeRequest?.SourceShiftSignup?.Shift == null)
						return true;

					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var text = _shiftsService.GenerateShiftTradeNotificationText(sourceUserProfile, tradeRequest);

					var userProfiles = await _userProfileService.GetSelectedUserProfilesAsync(tradeRequest.Users.Select(x => x.UserId).ToList());
					foreach (var user in tradeRequest.Users)
					{
						UserProfile profile = userProfiles.FirstOrDefault(x => x.UserId == user.UserId);
						await _communicationService.SendNotificationAsync(user.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber, department,
							tradeRequest.SourceShiftSignup.Shift.Name, profile);
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeRejected && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);

					if (tradeRequest?.SourceShiftSignup?.Shift == null)
						return true;

					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var targetUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = _shiftsService.GenerateShiftTradeRejectionText(targetUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(sourceUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber, department,
							tradeRequest.SourceShiftSignup.Shift.Name, sourceUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeProposed && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);

					if (tradeRequest?.SourceShiftSignup?.Shift == null)
						return true;

					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var proposedUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = _shiftsService.GenerateShiftTradeProposedText(proposedUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(sourceUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber, department,
							tradeRequest.SourceShiftSignup.Shift.Name, sourceUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeFilled && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);

					if (tradeRequest?.SourceShiftSignup?.Shift == null)
						return true;

					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.SourceShiftSignup.UserId);
					var proposedUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = _shiftsService.GenerateShiftTradeFilledText(sourceUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(proposedUserProfile.UserId, tradeRequest.SourceShiftSignup.Shift.DepartmentId, text, sqi.DepartmentNumber, department,
							tradeRequest.SourceShiftSignup.Shift.Name, proposedUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.SignupPendingApproval ||
				         sqi.Type == (int)ShiftQueueTypes.SignupReviewed ||
				         sqi.Type == (int)ShiftQueueTypes.DayAssigned ||
				         sqi.Type == (int)ShiftQueueTypes.DayRemoved)
				{
					var signup = await _shiftsService.GetShiftSignupByIdAsync(sqi.ShiftSignupId);
					var shift = signup != null ? await _shiftsService.GetShiftByIdAsync(signup.ShiftId) : null;

					if (signup != null && shift != null)
					{
						var day = signup.ShiftDay.ToShortDateString();
						var signupProfile = await _userProfileService.GetProfileByUserIdAsync(signup.UserId);
						var note = String.IsNullOrWhiteSpace(signup.ReviewNote) ? "" : $": {signup.ReviewNote}";

						if (sqi.Type == (int)ShiftQueueTypes.SignupPendingApproval)
						{
							var text = $"{signupProfile?.FullName?.AsFirstNameLastName} signed up for {shift.Name} on {day} and needs approval";

							foreach (var supervisorId in await GetShiftSupervisorIdsAsync(sqi.DepartmentId, signup.DepartmentGroupId))
							{
								if (supervisorId != signup.UserId)
									await _communicationService.SendNotificationAsync(supervisorId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, shift.Name);
							}
						}
						else
						{
							string text;

							if (sqi.Type == (int)ShiftQueueTypes.SignupReviewed)
								text = signup.Denied ? $"Your signup for {shift.Name} on {day} was denied{note}" : $"Your signup for {shift.Name} on {day} was approved";
							else if (sqi.Type == (int)ShiftQueueTypes.DayAssigned)
								text = $"You have been added to {shift.Name} on {day}";
							else
								text = $"You have been removed from {shift.Name} on {day}{note}";

							// The person acting on their own record does not need to be told about it.
							if (signup.UserId != sqi.SourceUserId)
								await _communicationService.SendNotificationAsync(signup.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, shift.Name, signupProfile);
						}
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradePendingApproval || sqi.Type == (int)ShiftQueueTypes.TradeReviewed)
				{
					var trade = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var source = trade?.SourceShiftSignup;

					if (source?.Shift != null)
					{
						var takerUserId = !String.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : trade.TargetShiftSignup?.UserId;
						var sourceProfile = await _userProfileService.GetProfileByUserIdAsync(source.UserId);
						var takerProfile = !String.IsNullOrWhiteSpace(takerUserId) ? await _userProfileService.GetProfileByUserIdAsync(takerUserId) : null;
						var day = source.ShiftDay.ToShortDateString();

						if (sqi.Type == (int)ShiftQueueTypes.TradePendingApproval)
						{
							var text = $"{sourceProfile?.FullName?.AsFirstNameLastName} is trading {source.Shift.Name} on {day} to {takerProfile?.FullName?.AsFirstNameLastName} and needs approval";

							foreach (var supervisorId in await GetShiftSupervisorIdsAsync(sqi.DepartmentId, source.DepartmentGroupId))
							{
								if (supervisorId != source.UserId)
									await _communicationService.SendNotificationAsync(supervisorId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, source.Shift.Name);
							}
						}
						else
						{
							var note = String.IsNullOrWhiteSpace(trade.ReviewNote) ? "" : $": {trade.ReviewNote}";
							var text = trade.Denied
								? $"The shift trade for {source.Shift.Name} on {day} was denied{note}"
								: $"The shift trade for {source.Shift.Name} on {day} was approved";

							await _communicationService.SendNotificationAsync(source.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, source.Shift.Name, sourceProfile);

							if (takerProfile != null)
								await _communicationService.SendNotificationAsync(takerProfile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, source.Shift.Name, takerProfile);
						}
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.ShiftCreated)
				{
					var shift = await _shiftsService.GetShiftByIdAsync(sqi.ShiftId);
					var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(sqi.DepartmentId);

					var text = $"New Shift {shift.Name} has been created";

					foreach (var profile in profiles.Select(x => x.Value))
					{
						await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, "New Shift", profile);
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
							await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, shift.Name, profiles[profile.UserId]);
						else
							await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, shift.Name);
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
								await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, shift.Name, profiles[profile.UserId]);
							else
								await _communicationService.SendNotificationAsync(profile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, shift.Name);
						}
					}
				}

				_shiftsService = null;
				_communicationService = null;
				_userProfileService = null;
			}

			return true;
		}

		/// <summary>
		/// Who approves shift changes for a group: department admins plus the admins of the group and any group above it.
		/// </summary>
		private static async Task<List<string>> GetShiftSupervisorIdsAsync(int departmentId, int? departmentGroupId)
		{
			var departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
			var departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();

			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			var admins = await departmentsService.GetAllAdminsForDepartmentAsync(departmentId);
			foreach (var admin in admins ?? new List<IdentityUser>())
				ids.Add(admin.Id);

			if (departmentGroupId.HasValue)
			{
				var groupAdmins = await departmentGroupsService.GetAllAdminsForGroupAndAncestorsAsync(departmentGroupId.Value);
				foreach (var groupAdmin in groupAdmins ?? new List<DepartmentGroupMember>())
					ids.Add(groupAdmin.UserId);
			}

			return ids.Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
		}
	}
}
