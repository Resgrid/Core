using System;
using System.Linq;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// The fixed texts Resgrid sends on an intake SMS conversation (plan §8 D15/D16) and the call note prefixes that make each message
	/// read well on any call screen. English only, like the rest of the outbound SMS text (outbound localization gap): the texter is a
	/// member of the public with no profile language. Em dashes are kept here and in the notes; the SMS path normalizes them to GSM.
	/// </summary>
	public static class IntakeConversationTexts
	{
		public const string CallerNotePrefix = "Text from caller: ";
		public const string ResgridNotePrefix = "Text to caller (Resgrid): ";
		public const string AssistantNotePrefix = "Text to caller (Enhanced AI): ";
		public const string DispatcherNoteFormat = "Text to caller ({0}): ";
		public const string OptedOutNote = "The caller texted STOP: no more texts will be sent to them and the text conversation was closed.";
		public const string FallbackFirstName = "a dispatcher";

		/// <summary>D15 hand-off welcome, from the master number; Unassisted adds that a dispatcher will reply.</summary>
		public static string Welcome(string departmentName, bool unassisted)
		{
			var name = Department(departmentName);
			var text = $"{name}: You texted the dispatch number for {name}. We'll continue on this number — reply here. " +
				$"If this is a life-threatening emergency, call {EmergencyNumber()}.";
			return unassisted ? text + " A dispatcher will reply here." : text;
		}

		/// <summary>D16: a dispatcher connected (desk accept, "Take conversation", v4).</summary>
		public static string Connected(string firstName) =>
			$"You are now connected with {(string.IsNullOrWhiteSpace(firstName) ? FallbackFirstName : firstName.Trim())}. " +
			"Please describe the situation you are having and your location.";

		/// <summary>D16: Enhanced AI finished (done, turn limit, wants a dispatcher, or the model is unavailable).</summary>
		public static string AssistantFinished(string departmentName) =>
			$"Thanks — {(string.IsNullOrWhiteSpace(departmentName) ? "the department's" : departmentName.Trim())} dispatch has your information. A dispatcher may text you here.";

		/// <summary>A dispatcher closed the conversation and chose to tell the texter.</summary>
		public static string Closed(string departmentName) =>
			$"{Department(departmentName)}: This text conversation has ended. If this is a life-threatening emergency, call {EmergencyNumber()}.";

		public static string DispatcherNotePrefix(string firstName) =>
			string.Format(DispatcherNoteFormat, string.IsNullOrWhiteSpace(firstName) ? "Dispatcher" : firstName.Trim());

		/// <summary>
		/// The SMS opt-out words: STOP and UNSUBSCRIBE only, whole message, any case (as TextCommandService). END, QUIT and CANCEL carry
		/// other meanings in a conversation ("cancel, it was a false alarm") and never opt out.
		/// </summary>
		public static bool IsOptOut(string text)
		{
			var word = text?.Trim();
			return string.Equals(word, "stop", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "unsubscribe", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The message text of a conversation note (prefix removed), or the note unchanged when it has none of ours.</summary>
		public static string StripPrefix(string note)
		{
			if (string.IsNullOrEmpty(note))
				return note;

			foreach (var prefix in new[] { CallerNotePrefix, ResgridNotePrefix, AssistantNotePrefix })
				if (note.StartsWith(prefix, StringComparison.Ordinal))
					return note.Substring(prefix.Length);

			// "Text to caller ({name}): "
			const string start = "Text to caller (";
			if (note.StartsWith(start, StringComparison.Ordinal))
			{
				var end = note.IndexOf("): ", start.Length, StringComparison.Ordinal);
				if (end > 0 && end - start.Length <= 120)
					return note.Substring(end + 3);
			}

			return note;
		}

		/// <summary>A profile first name fit for a text: control characters removed, at most 40 characters; null when unusable.</summary>
		public static string SafeFirstName(string firstName)
		{
			if (string.IsNullOrWhiteSpace(firstName) || ProtectedDataEnvelope.IsEnveloped(firstName) ||
				string.Equals(firstName.Trim(), ProtectedDataEnvelope.RedactionValue, StringComparison.Ordinal))
				return null;

			var clean = new string(firstName.Where(c => !char.IsControl(c)).ToArray()).Trim();
			if (clean.Length == 0)
				return null;
			return clean.Length <= 40 ? clean : clean.Substring(0, 40).Trim();
		}

		private static string Department(string departmentName) => string.IsNullOrWhiteSpace(departmentName) ? "Dispatch" : departmentName.Trim();

		private static string EmergencyNumber() => string.IsNullOrWhiteSpace(IntakeConfig.EmergencyNumber) ? "911" : IntakeConfig.EmergencyNumber.Trim();
	}
}
