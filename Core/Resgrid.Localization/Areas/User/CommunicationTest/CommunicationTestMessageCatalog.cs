using System.Collections.Generic;

namespace Resgrid.Localization.Areas.User.CommunicationTest
{
	/// <summary>
	/// The exact wording a communication test delivers on each channel, rendered in a caller-supplied
	/// culture. Every message is composed for someone else — a worker builds it for a recipient whose
	/// language has nothing to do with the thread culture — so each method takes the recipient's
	/// culture explicitly rather than reading <c>CultureInfo.CurrentUICulture</c>.
	///
	/// The "what your people will see" preview on the new/edit screens reads from here too, so an
	/// administrator previewing a test sees the real message rather than a copy that can drift.
	/// </summary>
	public static class CommunicationTestMessageCatalog
	{
		/// <summary>Placeholder run code used by the preview; a real run generates its own.</summary>
		public const string SampleRunCode = "CT-A7X3";

		public static string BuildSmsBody(string testName, string runCode, string? culture)
		{
			// A test with no name would otherwise read "communication test ()." — drop the
			// parenthetical entirely rather than send empty brackets.
			return string.IsNullOrWhiteSpace(testName)
				? CommunicationTestResources.Get("MessageSmsBodyNoName", culture, runCode)
				: CommunicationTestResources.Get("MessageSmsBody", culture, testName.Trim(), runCode);
		}

		public static string BuildEmailSubject(string testName, string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailSubject", culture, testName);
		}

		public static string BuildEmailBody(string firstName, string departmentName, string testName, string confirmUrl, string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailBody", culture, firstName, departmentName, testName, confirmUrl);
		}

		public static string BuildPushTitle(string? culture)
		{
			return CommunicationTestResources.Get("MessagePushTitle", culture);
		}

		public static string BuildPushBody(string testName, string? culture)
		{
			return CommunicationTestResources.Get("MessagePushBody", culture, testName);
		}

		/// <summary>
		/// What the automated call speaks, in order. The call repeats the prompt twice before hanging
		/// up, and pressing 1 records the response.
		/// </summary>
		public static IReadOnlyList<string> GetVoicePrompts(string? culture)
		{
			return new[]
			{
				CommunicationTestResources.Get("MessageVoiceGreeting", culture),
				CommunicationTestResources.Get("MessageVoicePressOne", culture)
			};
		}

		public static string BuildVoiceRecorded(string? culture)
		{
			return CommunicationTestResources.Get("MessageVoiceRecorded", culture);
		}

		public static string BuildVoiceNoResponse(string? culture)
		{
			return CommunicationTestResources.Get("MessageVoiceNoResponse", culture);
		}
	}
}
