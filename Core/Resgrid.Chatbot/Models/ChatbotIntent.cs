using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public enum ChatbotIntentType
	{
		Unknown = 0,
		SetStatus = 1,
		SetStaffing = 2,
		ListCalls = 3,
		GetCallDetail = 4,
		ListUnits = 5,
		GetMyStatus = 6,
		ListMessages = 7,
		SendMessage = 8,
		ListCalendar = 9,
		CalendarDetail = 10,
		ListShifts = 11,
		ShiftDetail = 12,
		Help = 13,
		Stop = 14,
		DispatchCall = 15,
		PersonnelLookup = 16,
		WeatherAlert = 17,
		EmergencyMayday = 18,
		LinkAccount = 19,
		UnlinkAccount = 20,
		RespondToCall = 21,
		CloseCall = 22,
		ShiftSignup = 23,
		RsvpCalendar = 24,
		SetUnitStatus = 25,
		ListDepartments = 26,
		GetActiveDepartment = 27,
		SwitchDepartment = 28
	}

	public class ChatbotIntent
	{
		public ChatbotIntentType Type { get; set; }
		public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
		public double Confidence { get; set; }
		public List<ChatbotEntity> Entities { get; set; } = new List<ChatbotEntity>();
		public bool IsFallbackResult { get; set; }
		public string NluProviderName { get; set; }
		public string NluModelName { get; set; }
		public long? NluLatencyMs { get; set; }

		/// <summary>
		/// When set, indicates a department id for operations like switching.
		/// </summary>
		public int? TargetDepartmentId { get; set; }
	}
}
