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
		SwitchDepartment = 28,
		MessageDetail = 29,
		DeleteMessage = 30,
		RespondToMessage = 31,
		ShiftDrop = 32,
		WhoIsAvailable = 33,
		UnitsAvailable = 34,
		CallResponders = 35,
		CallDispatched = 36,
		MyCalls = 37,
		UnitCalls = 38,
		CreatePoll = 39,
		MySchedule = 40,

		// === Incident Command (ICS) — the questions an Incident Commander asks while working a
		// command board. Every one is READ-ONLY: the assistant reports the board, it never mutates
		// it (mutations stay with the explicit command-board actions and their confirmation gates).

		/// <summary>Overall incident snapshot / size-up ("what's the status of this incident").</summary>
		IncidentStatus = 41,

		/// <summary>Personnel accountability report ("PAR", "who's overdue for check-in").</summary>
		IncidentPar = 42,

		/// <summary>What's working the incident, optionally scoped to a lane ("who's in Division A").</summary>
		IncidentResources = 43,

		/// <summary>Tactical objectives / benchmarks and what's still open.</summary>
		IncidentObjectives = 44,

		/// <summary>Command-level needs (ordered resources/logistics) and what hasn't been filled.</summary>
		IncidentNeeds = 45,

		/// <summary>ICS position assignments — who holds which role, and which key roles are unfilled.</summary>
		IncidentRoles = 46,

		/// <summary>Recent incident (ICS-201) timeline entries.</summary>
		IncidentTimeline = 47,

		/// <summary>Incident timers — what's running, what's due.</summary>
		IncidentTimers = 48,

		/// <summary>Span-of-control check: lanes over/under their configured resource limits.</summary>
		IncidentSpanOfControl = 49,

		/// <summary>Transfer-of-command / ICS-201 style briefing built from the live board.</summary>
		IncidentBriefing = 50,

		/// <summary>Incident-type ICS checklist ("what am I missing on a structure fire").</summary>
		IncidentChecklist = 51,

		/// <summary>Weather at the incident (ICP coordinates first, then the call's).</summary>
		IncidentWeather = 52,

		/// <summary>Operational status notes recorded on the incident.</summary>
		IncidentNotes = 53
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
