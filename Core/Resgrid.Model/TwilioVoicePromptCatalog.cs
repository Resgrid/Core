using System.Collections.Generic;

namespace Resgrid.Model
{
	public static class TwilioVoicePromptCatalog
	{
		public const string CallClosed = "This call has been closed. Goodbye.";
		public const string RespondingToScene = "You have been marked responding to the scene, goodbye.";
		public const string InvalidSelection = "Sorry, that was not a valid selection.";
		public const string VerificationGreeting = "Hello, this is Resgrid calling with your verification code.";
		public const string VerificationClosing = "That was your Resgrid verification code. Goodbye.";
		public const string InboundVoiceUnavailable = "Thank you for calling Resgrid, automated personnel system. The number you called is not tied to an active department or the department doesn't have this feature enabled. Goodbye.";
		public const string VoiceVerificationFailure = "We couldn't complete your verification call. Please request a new code and try again. Goodbye.";
		public const string MainMenuSelectionIntro = "Please select from the following options.";
		public const string MainMenuActiveCalls = "To list current active calls, press 1.";
		public const string MainMenuUserStatuses = "To list current user statuses, press 2.";
		public const string MainMenuUnitStatuses = "To list current unit statuses, press 3.";
		public const string MainMenuCalendarEvents = "To list upcoming calendar events, press 4.";
		public const string MainMenuShifts = "To list upcoming shifts, press 5.";
		public const string MainMenuSetStatus = "To set your current status, press 6.";
		public const string MainMenuSetStaffing = "To set your current staffing level, press 7.";
		public const string RepeatAndRespondToScene = "Press 0 to repeat. Press 1 to respond to the scene.";
		public const string GoBackToMainMenu = "Press 0 to go back to the main menu.";
		public const string StatusSelectionIntro = "To set your current status, please select from the following options.";
		public const string StaffingSelectionIntro = "To set your current staffing, please select from the following options.";
		public const string InvalidStatusSelection = "Invalid status selection, goodbye.";
		public const string NoStatusSelection = "No status selection made, goodbye.";
		public const string InvalidStaffingSelection = "Invalid staffing selection. Returning to the main menu.";
		public const string NoStaffingSelection = "No staffing selection made. Returning to the main menu.";
		public const string CommunicationTestRecorded = "Thank you. Your response has been recorded.";

		public static string CallClosedByNumber(string callNumber) => $"This call, Id {callNumber} has been closed. Goodbye.";

		public static string RespondingToStation(string stationName) => $"You have been marked responding to {stationName}, goodbye.";

		public static string RespondToStationOption(int digit, string stationName) => $"Press {digit} to respond to {stationName}.";

		public static string VerificationCode(string spokenCode) => $"Your verification code is: {spokenCode}.";

		public static string MainMenuGreeting(string firstName, string departmentName) => $"Hello {firstName}, this is the Resgrid automated voice system for {departmentName}.";

		public static string StatusOption(int digit, string buttonText) => $"Press {digit} for {buttonText}.";

		public static string StaffingOption(int digit, string buttonText) => $"Press {digit} for {buttonText}.";

		public static string StatusMarked(string buttonText) => $"You have been marked as {buttonText}, goodbye.";

		public static string StaffingMarked(string buttonText) => $"You have been marked as {buttonText}. Goodbye.";

		public static IReadOnlyCollection<string> GetStaticPrompts()
		{
			return new[]
			{
				CallClosed,
				RespondingToScene,
				InvalidSelection,
				VerificationGreeting,
				VerificationClosing,
				InboundVoiceUnavailable,
				VoiceVerificationFailure,
				MainMenuSelectionIntro,
				MainMenuActiveCalls,
				MainMenuUserStatuses,
				MainMenuUnitStatuses,
				MainMenuCalendarEvents,
				MainMenuShifts,
				MainMenuSetStatus,
				MainMenuSetStaffing,
				RepeatAndRespondToScene,
				GoBackToMainMenu,
				StatusSelectionIntro,
				StaffingSelectionIntro,
				InvalidStatusSelection,
				NoStatusSelection,
				InvalidStaffingSelection,
				NoStaffingSelection,
				CommunicationTestRecorded
			};
		}
	}
}
