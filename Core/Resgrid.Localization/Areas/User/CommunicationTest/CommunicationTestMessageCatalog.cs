using System.Collections.Generic;
using System.Text;

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

		/// <summary>
		/// The plain text rendering of the email, assembled from the same pieces the HTML template
		/// uses. The sent email is HTML — this is what the "what your people will see" preview shows
		/// and what a client that cannot render HTML falls back to, so it is composed from the
		/// segments rather than from a second copy of the wording that could drift from them.
		/// </summary>
		public static string BuildEmailBody(string firstName, string departmentName, string testName, string confirmUrl, string? culture)
		{
			var builder = new StringBuilder();
			builder.AppendLine(BuildEmailGreeting(firstName, culture));
			builder.AppendLine();
			builder.AppendLine(BuildEmailIntro(departmentName, testName, culture));
			builder.AppendLine();
			builder.AppendLine(BuildEmailDisclaimer(culture));
			builder.AppendLine();
			builder.AppendLine(BuildEmailAction(culture));
			builder.AppendLine();
			builder.AppendLine(confirmUrl);
			builder.AppendLine();
			builder.AppendLine(BuildEmailSignoff(culture));
			builder.Append(BuildEmailTeam(culture));

			return builder.ToString();
		}

		/// <summary>Short summary line email clients show next to the subject.</summary>
		public static string BuildEmailPreheader(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailPreheader", culture);
		}

		public static string BuildEmailGreeting(string firstName, string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailGreeting", culture, firstName);
		}

		public static string BuildEmailIntro(string departmentName, string testName, string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailIntro", culture, departmentName, testName);
		}

		public static string BuildEmailDisclaimer(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailDisclaimer", culture);
		}

		public static string BuildEmailAction(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailAction", culture);
		}

		/// <summary>Label on the confirmation button in the HTML email.</summary>
		public static string BuildEmailButton(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailButton", culture);
		}

		/// <summary>Fallback copy shown under the button for clients that strip it.</summary>
		public static string BuildEmailTrouble(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailTrouble", culture);
		}

		public static string BuildEmailSignoff(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailSignoff", culture);
		}

		public static string BuildEmailTeam(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailTeam", culture);
		}

		public static string BuildEmailDepartmentLabel(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailDepartmentLabel", culture);
		}

		public static string BuildEmailTestLabel(string? culture)
		{
			return CommunicationTestResources.Get("MessageEmailTestLabel", culture);
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
