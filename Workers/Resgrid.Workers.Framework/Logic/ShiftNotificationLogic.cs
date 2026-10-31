using Resgrid.Model;
using Resgrid.Model.Helpers;
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
				// Own scope per message: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var _shiftsService = scope.Resolve<IShiftsService>();
				var _communicationService = scope.Resolve<ICommunicationService>();
				var _userProfileService = scope.Resolve<IUserProfileService>();
				var _departmentService = scope.Resolve<IDepartmentsService>();

				var department = await _departmentService.GetDepartmentByIdAsync(sqi.DepartmentId, false);

				if (sqi.Type == (int)ShiftQueueTypes.TradeRequested)
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var subject = await DescribeTradeAsync(scope, tradeRequest);

					if (subject == null)
						return true;

					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.GetSourceUserId());

					// An open request goes to everyone who could take the slot; a targeted one to the people asked.
					var recipients = tradeRequest.IsOpen
						? await _shiftsService.GetTradeEligibleUserIdsAsync(tradeRequest)
						: (tradeRequest.Users ?? new List<ShiftSignupTradeUser>()).Select(x => x.UserId).ToList();

					var text = tradeRequest.IsOpen
						? $"{sourceUserProfile?.FullName?.AsFirstNameLastName} is looking for someone to take {subject.Name} on {subject.DayText}. Open Shifts to apply."
						: _shiftsService.GenerateShiftTradeNotificationText(sourceUserProfile, tradeRequest);

					var userProfiles = await _userProfileService.GetSelectedUserProfilesAsync(recipients) ?? new List<UserProfile>();
					foreach (var userId in recipients.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
					{
						UserProfile profile = userProfiles.FirstOrDefault(x => String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
						await _communicationService.SendNotificationAsync(userId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, subject.Name, profile);
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeRejected && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var subject = await DescribeTradeAsync(scope, tradeRequest);

					if (subject == null)
						return true;

					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(tradeRequest.GetSourceUserId());
					var targetUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = tradeRequest.IsOpen
						? $"{targetUserProfile?.FullName?.AsFirstNameLastName} withdrew from your trade request for {subject.Name} on {subject.DayText}"
						: _shiftsService.GenerateShiftTradeRejectionText(targetUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(tradeRequest.GetSourceUserId(), sqi.DepartmentId, text, sqi.DepartmentNumber, department,
							subject.Name, sourceUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeProposed && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var subject = await DescribeTradeAsync(scope, tradeRequest);

					if (subject == null)
						return true;

					var sourceUserId = tradeRequest.GetSourceUserId();
					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(sourceUserId);
					var proposedUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var text = tradeRequest.IsOpen
						? $"{proposedUserProfile?.FullName?.AsFirstNameLastName} applied to take your {subject.Name} on {subject.DayText}"
						: _shiftsService.GenerateShiftTradeProposedText(proposedUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(sourceUserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, subject.Name, sourceUserProfile);

					// When the requester may not pick the applicant themselves, the people who can need to know there is one.
					var settings = await scope.Resolve<IDepartmentSettingsService>().GetShiftTradeApproverSettingsAsync(sqi.DepartmentId);

					if (settings != null && !settings.Requester)
					{
						var approverText = $"{proposedUserProfile?.FullName?.AsFirstNameLastName} applied to take {sourceUserProfile?.FullName?.AsFirstNameLastName}'s {subject.Name} on {subject.DayText}. Pick who takes it in Shifts.";

						foreach (var approverId in await GetTradeApproverIdsAsync(scope, sqi.DepartmentId, tradeRequest.GetDepartmentGroupId()))
						{
							if (!String.Equals(approverId, sourceUserId, StringComparison.OrdinalIgnoreCase) && !String.Equals(approverId, sqi.SourceUserId, StringComparison.OrdinalIgnoreCase))
								await _communicationService.SendNotificationAsync(approverId, sqi.DepartmentId, approverText, sqi.DepartmentNumber, department, subject.Name);
						}
					}
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeFilled && !String.IsNullOrWhiteSpace(sqi.SourceUserId))
				{
					var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(sqi.ShiftSignupTradeId);
					var subject = await DescribeTradeAsync(scope, tradeRequest);

					if (subject == null)
						return true;

					var sourceUserId = tradeRequest.GetSourceUserId();
					var sourceUserProfile = await _userProfileService.GetProfileByUserIdAsync(sourceUserId);
					var takerUserProfile = await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId);

					var takerText = tradeRequest.IsWorkshiftTrade()
						? $"You are taking {sourceUserProfile?.FullName?.AsFirstNameLastName}'s {subject.Name} on {subject.DayText}"
						: _shiftsService.GenerateShiftTradeFilledText(sourceUserProfile, tradeRequest);

					await _communicationService.SendNotificationAsync(sqi.SourceUserId, sqi.DepartmentId, takerText, sqi.DepartmentNumber, department, subject.Name, takerUserProfile);

					// The person trading out is told too: with approvers picking, they may not have made the choice.
					var sourceText = $"{takerUserProfile?.FullName?.AsFirstNameLastName} will take your {subject.Name} on {subject.DayText}";
					await _communicationService.SendNotificationAsync(sourceUserId, sqi.DepartmentId, sourceText, sqi.DepartmentNumber, department, subject.Name, sourceUserProfile);
				}
				else if (sqi.Type == (int)ShiftQueueTypes.TradeCancelled)
				{
					var subject = await DescribeCancelledTradeAsync(scope, sqi, department);

					if (subject == null)
						return true;

					var actingProfile = !String.IsNullOrWhiteSpace(sqi.ActingUserId) ? await _userProfileService.GetProfileByUserIdAsync(sqi.ActingUserId) : null;
					var byText = !String.IsNullOrWhiteSpace(sqi.ActingUserId) && !String.Equals(sqi.ActingUserId, sqi.SourceUserId, StringComparison.OrdinalIgnoreCase)
						? $" by {actingProfile?.FullName?.AsFirstNameLastName}"
						: "";

					var recipients = new List<string>();
					if (!String.IsNullOrWhiteSpace(sqi.SourceUserId))
						recipients.Add(sqi.SourceUserId);
					recipients.AddRange(sqi.UserIds ?? new List<string>());

					var sourceProfile = !String.IsNullOrWhiteSpace(sqi.SourceUserId) ? await _userProfileService.GetProfileByUserIdAsync(sqi.SourceUserId) : null;
					var userProfiles = await _userProfileService.GetSelectedUserProfilesAsync(recipients) ?? new List<UserProfile>();

					foreach (var userId in recipients.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
					{
						var isSource = String.Equals(userId, sqi.SourceUserId, StringComparison.OrdinalIgnoreCase);
						var text = isSource
							? $"Your trade request for {subject.Name} on {subject.DayText} was cancelled{byText}"
							: $"{sourceProfile?.FullName?.AsFirstNameLastName}'s trade request for {subject.Name} on {subject.DayText} was cancelled{byText}";

						var profile = userProfiles.FirstOrDefault(x => String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
						await _communicationService.SendNotificationAsync(userId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, subject.Name, profile);
					}
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

							foreach (var supervisorId in await GetShiftSupervisorIdsAsync(scope, sqi.DepartmentId, signup.DepartmentGroupId))
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
					var subject = await DescribeTradeAsync(scope, trade);

					if (subject != null)
					{
						var sourceUserId = trade.GetSourceUserId();
						var takerUserId = trade.GetPickedUserId();
						var sourceProfile = await _userProfileService.GetProfileByUserIdAsync(sourceUserId);
						var takerProfile = !String.IsNullOrWhiteSpace(takerUserId) ? await _userProfileService.GetProfileByUserIdAsync(takerUserId) : null;

						if (sqi.Type == (int)ShiftQueueTypes.TradePendingApproval)
						{
							var text = $"{sourceProfile?.FullName?.AsFirstNameLastName} is trading {subject.Name} on {subject.DayText} to {takerProfile?.FullName?.AsFirstNameLastName} and needs approval";

							foreach (var approverId in await GetTradeApproverIdsAsync(scope, sqi.DepartmentId, trade.GetDepartmentGroupId()))
							{
								if (!String.Equals(approverId, sourceUserId, StringComparison.OrdinalIgnoreCase))
									await _communicationService.SendNotificationAsync(approverId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, subject.Name);
							}
						}
						else
						{
							var note = String.IsNullOrWhiteSpace(trade.ReviewNote) ? "" : $": {trade.ReviewNote}";
							var text = trade.Denied
								? $"The shift trade for {subject.Name} on {subject.DayText} was denied{note}"
								: $"The shift trade for {subject.Name} on {subject.DayText} was approved";

							await _communicationService.SendNotificationAsync(sourceUserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, subject.Name, sourceProfile);

							if (takerProfile != null)
								await _communicationService.SendNotificationAsync(takerProfile.UserId, sqi.DepartmentId, text, sqi.DepartmentNumber, department, subject.Name, takerProfile);
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

		private sealed class TradeSubject
		{
			public string Name { get; set; }

			public string DayText { get; set; }
		}

		/// <summary>The shift (or static shift) name and day a trade is for, or null when the trade or its day is gone.</summary>
		private static async Task<TradeSubject> DescribeTradeAsync(ILifetimeScope scope, ShiftSignupTrade trade)
		{
			if (trade == null)
				return null;

			if (trade.IsWorkshiftTrade())
			{
				var workshift = await scope.Resolve<IWorkShiftsService>().GetWorkshiftByIdAsync(trade.WorkshiftId);

				if (workshift == null || !trade.SourceDay.HasValue)
					return null;

				return new TradeSubject { Name = workshift.Name, DayText = trade.SourceDay.Value.ToShortDateString() };
			}

			if (trade.SourceShiftSignup?.Shift == null)
				return null;

			return new TradeSubject { Name = trade.SourceShiftSignup.Shift.Name, DayText = trade.SourceShiftSignup.ShiftDay.ToShortDateString() };
		}

		/// <summary>A cancelled trade is deleted, so its subject comes from the source signup or static shift day it was for.</summary>
		private static async Task<TradeSubject> DescribeCancelledTradeAsync(ILifetimeScope scope, ShiftQueueItem sqi, Department department)
		{
			if (!String.IsNullOrWhiteSpace(sqi.WorkshiftDayId))
			{
				var workShiftsService = scope.Resolve<IWorkShiftsService>();
				var day = await workShiftsService.GetWorkshiftDayByIdAsync(sqi.WorkshiftDayId);
				var workshift = day != null ? await workShiftsService.GetWorkshiftByIdAsync(day.WorkshiftId) : null;

				if (workshift == null)
					return null;

				// Static shift days are stored in UTC.
				var localDay = department != null ? day.Day.TimeConverter(department) : day.Day;

				return new TradeSubject { Name = workshift.Name, DayText = localDay.ToShortDateString() };
			}

			if (sqi.ShiftSignupId <= 0)
				return null;

			var shiftsService = scope.Resolve<IShiftsService>();
			var signup = await shiftsService.GetShiftSignupByIdAsync(sqi.ShiftSignupId);
			var shift = signup != null ? await shiftsService.GetShiftByIdAsync(signup.ShiftId) : null;

			if (shift == null)
				return null;

			return new TradeSubject { Name = shift.Name, DayText = signup.ShiftDay.ToShortDateString() };
		}

		/// <summary>
		/// Who decides trades for a slot under the department's trade approver settings: department admins, the slot's group
		/// admins (and those above them) when group admins may decide, and members of the approver roles.
		/// </summary>
		private static async Task<List<string>> GetTradeApproverIdsAsync(ILifetimeScope scope, int departmentId, int? departmentGroupId)
		{
			var settings = await scope.Resolve<IDepartmentSettingsService>().GetShiftTradeApproverSettingsAsync(departmentId) ?? ShiftTradeApproverSettings.Default();
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			var admins = await scope.Resolve<IDepartmentsService>().GetAllAdminsForDepartmentAsync(departmentId);
			foreach (var admin in admins ?? new List<IdentityUser>())
				ids.Add(admin.Id);

			if (settings.GroupAdmins && departmentGroupId.HasValue)
			{
				var groupAdmins = await scope.Resolve<IDepartmentGroupsService>().GetAllAdminsForGroupAndAncestorsAsync(departmentGroupId.Value);
				foreach (var groupAdmin in groupAdmins ?? new List<DepartmentGroupMember>())
					ids.Add(groupAdmin.UserId);
			}

			if (settings.HasRoles)
			{
				var rolesService = scope.Resolve<IPersonnelRolesService>();

				foreach (var roleId in settings.RoleIds.Distinct())
				{
					foreach (var member in await rolesService.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
						ids.Add(member.UserId);
				}
			}

			return ids.Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
		}

		/// <summary>
		/// Who approves shift changes for a group: department admins plus the admins of the group and any group above it.
		/// </summary>
		private static async Task<List<string>> GetShiftSupervisorIdsAsync(ILifetimeScope scope, int departmentId, int? departmentGroupId)
		{
			var departmentsService = scope.Resolve<IDepartmentsService>();
			var departmentGroupsService = scope.Resolve<IDepartmentGroupsService>();

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
