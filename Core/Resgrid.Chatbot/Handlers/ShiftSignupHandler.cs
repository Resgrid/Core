using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Signs the user up for an open shift day (intent <see cref="ChatbotIntentType.ShiftSignup"/>). The
	/// shift day's parent shift must belong to the active department (anti-IDOR §3). The signup goes through
	/// <see cref="IShiftsService.SignupUserForShiftDayAsync"/>, the same path the web and v4 use, so its scheduling
	/// rules (day not over, a group on group-staffed shifts, signup-type shifts only, approval) apply here too; the user's
	/// own group is used when it staffs the shift. Responses are localized to the user's culture.
	/// </summary>
	public class ShiftSignupHandler : IChatbotActionHandler
	{
		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public ShiftSignupHandler(IShiftsService shiftsService, IDepartmentGroupsService departmentGroupsService)
		{
			_shiftsService = shiftsService;
			_departmentGroupsService = departmentGroupsService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ShiftSignup;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("shiftId", out var shiftRef);
				if (string.IsNullOrWhiteSpace(shiftRef) || !int.TryParse(shiftRef.Trim().TrimStart('#'), out var shiftDayId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_SpecifySignup", culture), Processed = false };

				var targetDay = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);
				if (targetDay == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_NotFound", culture, shiftDayId), Processed = true };

				var shift = await _shiftsService.GetShiftByIdAsync(targetDay.ShiftId);
				if (shift == null || shift.DepartmentId != session.DepartmentId)
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_NotFound", culture, shiftDayId), Processed = true };

				if (await _shiftsService.IsShiftDayFilledAsync(targetDay.ShiftDayId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_Full", culture), Processed = true };

				// A group-staffed shift is signed up for through one of its groups. The chat has no group picker, so the
				// user's own group is used when it is one of them; otherwise the service refuses and the user is pointed at
				// the app or web, where they can choose.
				int? groupId = null;
				if (shift.Groups != null && shift.Groups.Any())
				{
					var userGroup = await _departmentGroupsService.GetGroupForUserAsync(session.UserId, session.DepartmentId);

					if (userGroup != null && shift.Groups.Any(g => g.DepartmentGroupId == userGroup.DepartmentGroupId))
						groupId = userGroup.DepartmentGroupId;
				}

				var result = await _shiftsService.SignupUserForShiftDayAsync(targetDay.ShiftDayId, groupId, session.UserId);

				if (!result.Success)
					return new ChatbotResponse { Text = RefusalText(result.Error, shiftDayId, culture), Processed = true };

				var day = targetDay.Day.ToString("MMM dd, yyyy");

				if (result.Item != null && result.Item.ApprovalPending)
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_SignupPendingApproval", culture, day), Processed = true };

				return new ChatbotResponse { Text = ChatbotResources.Get("Shift_SignedUp", culture, day), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Shift_ErrorSignup", culture), Processed = false };
			}
		}

		private static string RefusalText(ShiftActionErrors error, int shiftDayId, string culture)
		{
			switch (error)
			{
				case ShiftActionErrors.NotFound:
					return ChatbotResources.Get("Shift_NotFound", culture, shiftDayId);
				case ShiftActionErrors.AlreadySignedUp:
					return ChatbotResources.Get("Shift_AlreadySignedUp", culture);
				case ShiftActionErrors.DayInPast:
					return ChatbotResources.Get("Shift_DayInPast", culture);
				case ShiftActionErrors.InvalidGroup:
					return ChatbotResources.Get("Shift_GroupRequired", culture);
				case ShiftActionErrors.InvalidRequest:
					return ChatbotResources.Get("Shift_NotOpenForSignup", culture);
				default:
					return ChatbotResources.Get("Shift_ErrorSignup", culture);
			}
		}
	}
}
