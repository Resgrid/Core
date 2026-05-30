using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Drops the user's signup for a shift day (intent <see cref="ChatbotIntentType.ShiftDrop"/>).
	/// Self-scoped; the shift day's parent shift must belong to the active department (anti-IDOR §3) and
	/// <see cref="IAuthorizationService.CanUserDeleteShiftSignupAsync"/> is enforced (§2). Responses are
	/// localized to the user's culture.
	/// </summary>
	public class ShiftDropHandler : IChatbotActionHandler
	{
		private readonly IShiftsService _shiftsService;
		private readonly IAuthorizationService _authorizationService;

		public ShiftDropHandler(IShiftsService shiftsService, IAuthorizationService authorizationService)
		{
			_shiftsService = shiftsService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ShiftDrop;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("shiftId", out var shiftRef);
				if (string.IsNullOrWhiteSpace(shiftRef) || !int.TryParse(shiftRef.Trim().TrimStart('#'), out var shiftDayId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_SpecifyDrop", culture), Processed = false };

				var targetDay = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);
				if (targetDay == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_NotFound", culture, shiftDayId), Processed = true };

				var shift = await _shiftsService.GetShiftByIdAsync(targetDay.ShiftId);
				if (shift == null || shift.DepartmentId != session.DepartmentId)
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_NotFound", culture, shiftDayId), Processed = true };

				var signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(shiftDayId);
				var mySignup = signups?.FirstOrDefault(s => s.UserId == session.UserId);
				if (mySignup == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_NotSignedUp", culture), Processed = true };

				if (!await _authorizationService.CanUserDeleteShiftSignupAsync(session.UserId, session.DepartmentId, mySignup.ShiftSignupId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_NoDropPermission", culture), Processed = false };

				await _shiftsService.DeleteShiftSignupAsync(mySignup);

				return new ChatbotResponse { Text = ChatbotResources.Get("Shift_Dropped", culture, targetDay.Day.ToString("MMM dd, yyyy")), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Shift_ErrorDrop", culture), Processed = false };
			}
		}
	}
}
