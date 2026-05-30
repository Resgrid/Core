using System;
using System.Collections.Generic;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	public class IntentMapper : IIntentMapper
	{
		private static readonly Dictionary<string, ChatbotIntentType> _intentMap = new(StringComparer.OrdinalIgnoreCase)
		{
			["set_status"] = ChatbotIntentType.SetStatus,
			["set_staffing"] = ChatbotIntentType.SetStaffing,
			["list_calls"] = ChatbotIntentType.ListCalls,
			["call_detail"] = ChatbotIntentType.GetCallDetail,
			["list_units"] = ChatbotIntentType.ListUnits,
			["my_status"] = ChatbotIntentType.GetMyStatus,
			["list_messages"] = ChatbotIntentType.ListMessages,
			["send_message"] = ChatbotIntentType.SendMessage,
			["list_calendar"] = ChatbotIntentType.ListCalendar,
			["calendar_detail"] = ChatbotIntentType.CalendarDetail,
			["list_shifts"] = ChatbotIntentType.ListShifts,
			["shift_detail"] = ChatbotIntentType.ShiftDetail,
			["shift_signup"] = ChatbotIntentType.ShiftSignup,
			["shift_drop"] = ChatbotIntentType.ShiftDrop,
			["help"] = ChatbotIntentType.Help,
			["stop"] = ChatbotIntentType.Stop,
			["dispatch_call"] = ChatbotIntentType.DispatchCall,
			["personnel_lookup"] = ChatbotIntentType.PersonnelLookup,
			["weather_alert"] = ChatbotIntentType.WeatherAlert,
			["emergency_mayday"] = ChatbotIntentType.EmergencyMayday,
			["link_account"] = ChatbotIntentType.LinkAccount,
			["unlink_account"] = ChatbotIntentType.UnlinkAccount,
			["respond_to_call"] = ChatbotIntentType.RespondToCall,
			["close_call"] = ChatbotIntentType.CloseCall,
			["message_detail"] = ChatbotIntentType.MessageDetail,
			["delete_message"] = ChatbotIntentType.DeleteMessage,
			["respond_to_message"] = ChatbotIntentType.RespondToMessage,
			["rsvp_calendar"] = ChatbotIntentType.RsvpCalendar,
			["set_unit_status"] = ChatbotIntentType.SetUnitStatus,
			["list_departments"] = ChatbotIntentType.ListDepartments,
			["get_active_department"] = ChatbotIntentType.GetActiveDepartment,
			["switch_department"] = ChatbotIntentType.SwitchDepartment,
			["unknown"] = ChatbotIntentType.Unknown,
		};

		public ChatbotIntent MapToIntent(NLUResult nluResult)
		{
			if (nluResult == null)
				return new ChatbotIntent { Type = ChatbotIntentType.Unknown, Confidence = 0 };

			if (_intentMap.TryGetValue(nluResult.IntentName ?? "unknown", out var intentType))
			{
				return new ChatbotIntent
				{
					Type = intentType,
					Parameters = nluResult.Parameters ?? new Dictionary<string, string>(),
					Confidence = nluResult.Confidence
				};
			}

			return new ChatbotIntent
			{
				Type = ChatbotIntentType.Unknown,
				Confidence = 0
			};
		}
	}
}
