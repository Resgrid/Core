using System.Collections.Generic;

namespace Resgrid.Model
{
	public static class TwilioVoicePromptCatalog
	{
		public const string CallClosed = "This call has been closed. Goodbye.";
		public const string RespondingToScene = "You have been marked responding to the scene. Goodbye.";
		public const string InvalidSelection = "Sorry, that was not a valid selection.";
		public const string VerificationGreeting = "Hello, this is Resgrid calling with your verification code.";
		public const string VerificationClosing = "That was your Resgrid verification code. Goodbye.";
		public const string InboundVoiceUnavailable = "Thank you for calling the Resgrid automated personnel system. The number you called is not tied to an active department, or the department doesn't have this feature enabled. Goodbye.";
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
		public const string OutboundDispatchMenu = "To hear the dispatch again, press 1. To hear response options, press 2.";
		public const string OutboundResponseSelectionIntro = "To choose a response option, enter the option number, then press pound.";
		public const string RepeatDispatchWithPound = "To hear the dispatch again, enter 0 and press pound.";
		public const string GoBackToMainMenu = "Press 0 to go back to the main menu.";
		public const string GoBackToMainMenuWithPound = "To go back to the main menu, enter 0 and press pound.";
		public const string StatusSelectionIntro = "To set your current status, enter the number of your selection, then press pound.";
		public const string StaffingSelectionIntro = "To set your current staffing, enter the number of your selection, then press pound.";
		public const string InvalidStatusSelection = "Invalid status selection. Goodbye.";
		public const string NoStatusSelection = "No status selection made. Goodbye.";
		public const string InvalidStaffingSelection = "Invalid staffing selection. Returning to the main menu.";
		public const string NoStaffingSelection = "No staffing selection made. Returning to the main menu.";
		public const string CommunicationTestRecorded = "Thank you. Your response has been recorded.";

		public static string CallClosedByNumber(string callNumber) => $"This call, ID {callNumber}, has been closed. Goodbye.";

		public static string RespondingToStation(string stationName) => $"You have been marked responding to {stationName}. Goodbye.";

		public static string RespondToStationOption(int digit, string stationName) => $"To respond to {stationName}, enter {digit} and press pound.";

		public static string RespondToSceneOption(int digit) => $"To respond to the scene, enter {digit} and press pound.";

		public static string VerificationCode(string spokenCode) => $"Your verification code is: {spokenCode}.";

		public static string MainMenuGreeting(string firstName, string departmentName) => $"Hello {firstName}. This is the Resgrid automated voice system for {departmentName}.";

		public static string StatusOption(int digit, string buttonText) => $"For {buttonText}, enter {digit} and press pound.";

		public static string StaffingOption(int digit, string buttonText) => $"For {buttonText}, enter {digit} and press pound.";

		public static string StatusMarked(string buttonText) => $"You have been marked as {buttonText}. Goodbye.";

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
				OutboundDispatchMenu,
				OutboundResponseSelectionIntro,
				RepeatDispatchWithPound,
				GoBackToMainMenu,
				GoBackToMainMenuWithPound,
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
