using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Signs the user up for an open shift day (intent <see cref="ChatbotIntentType.ShiftSignup"/>). The
	/// shift day's parent shift must belong to the active department (anti-IDOR §3). Responses are localized
	/// to the user's culture.
	/// </summary>
	public class ShiftSignupHandler : IChatbotActionHandler
	{
		private readonly IShiftsService _shiftsService;

		public ShiftSignupHandler(IShiftsService shiftsService)
		{
			_shiftsService = shiftsService;
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

				if (await _shiftsService.IsUserSignedUpForShiftDayAsync(targetDay, session.UserId, null))
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_AlreadySignedUp", culture), Processed = true };

				if (await _shiftsService.IsShiftDayFilledAsync(targetDay.ShiftDayId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Shift_Full", culture), Processed = true };

				await _shiftsService.SignupForShiftDayAsync(targetDay.ShiftId, targetDay.Day, 0, session.UserId);

				return new ChatbotResponse { Text = ChatbotResources.Get("Shift_SignedUp", culture, targetDay.Day.ToString("MMM dd, yyyy")), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Shift_ErrorSignup", culture), Processed = false };
			}
		}
	}
}
