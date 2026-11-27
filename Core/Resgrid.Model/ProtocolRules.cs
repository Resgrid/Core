using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/// <summary>
	/// The rules a protocol must meet to be saved, shared by the web editor and the v4 API so they cannot drift: the code, the
	/// description's length (counted on its text, not its markup) and the attachment limits.
	/// </summary>
	public static class ProtocolRules
	{
		/// <summary>Largest single attachment (unchanged from before P0).</summary>
		public const long MaxAttachmentBytes = 30_000_000;

		/// <summary>Most files one save may add.</summary>
		public const int MaxFilesPerSave = 10;

		/// <summary>Most bytes one save may add across its files.</summary>
		public const long MaxBytesPerSave = 60_000_000;

		/// <summary>Most attachments a protocol may carry.</summary>
		public const int MaxAttachmentsPerProtocol = 25;

		/// <summary>Longest removal reason kept on a call's protocol (CallProtocols.RemovedReason).</summary>
		public const int RemovalReasonMaxLength = 500;

		public static readonly IReadOnlyCollection<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "ppt", "pptx", "pps", "ppsx", "odt", "xls", "xlsx", "txt", "mpg", "avi", "mpeg"
		};

		// Letters and digits, then letters, digits, space, dot, dash, underscore or slash: 09-E-01, 26A01, SOG-204, 2.1/A.
		private static readonly Regex CodePattern = new Regex(@"^[A-Z0-9][A-Z0-9 ._/\-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>The code as stored: trimmed and upper case.</summary>
		public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

		public static bool IsValidCode(string normalizedCode) =>
			!string.IsNullOrEmpty(normalizedCode) && normalizedCode.Length <= DispatchProtocol.CodeMaxLength && CodePattern.IsMatch(normalizedCode);

		/// <summary>The description's length as a reader sees it: its text with the markup and entities taken out.</summary>
		public static int DescriptionTextLength(string descriptionHtml) => StringHelpers.HtmlToPlainText(descriptionHtml).Trim().Length;

		public static bool IsAllowedFile(string fileName)
		{
			var extension = FileHelper.GetFileExtensionWithoutDot(fileName ?? string.Empty);
			return !string.IsNullOrWhiteSpace(extension) && AllowedExtensions.Contains(extension);
		}

		/// <summary>
		/// The errors a protocol's own fields have (not its children), as resource keys of the Protocols localization area. Empty
		/// when it can be saved.
		/// </summary>
		public static List<string> Validate(DispatchProtocol protocol)
		{
			var errors = new List<string>();

			if (protocol == null)
			{
				errors.Add("ErrorProtocolMissing");
				return errors;
			}

			if (string.IsNullOrWhiteSpace(protocol.Name))
				errors.Add("ErrorNameRequired");
			else if (protocol.Name.Trim().Length > 100)
				errors.Add("ErrorNameTooLong");

			var code = NormalizeCode(protocol.Code);
			if (string.IsNullOrEmpty(code))
				errors.Add("ErrorCodeRequired");
			else if (!IsValidCode(code))
				errors.Add("ErrorCodeInvalid");

			if (DescriptionTextLength(protocol.Description) > DispatchProtocol.DescriptionTextMaxLength)
				errors.Add("ErrorDescriptionTooLong");

			if (protocol.MinimumWeight < 0 || protocol.MinimumWeight > 1000)
				errors.Add("ErrorMinimumWeightRange");

			foreach (var trigger in protocol.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>())
			{
				if (!Enum.IsDefined(typeof(ProtocolTriggerTypes), trigger.Type))
				{
					errors.Add("ErrorTriggerType");
					break;
				}

				if (trigger.StartsOn.HasValue && trigger.EndsOn.HasValue && trigger.EndsOn.Value < trigger.StartsOn.Value)
				{
					errors.Add("ErrorTriggerWindow");
					break;
				}

				if ((trigger.Type == (int)ProtocolTriggerTypes.CallPriorty || trigger.Type == (int)ProtocolTriggerTypes.CallPriortyAndType) && !trigger.Priority.HasValue)
				{
					errors.Add("ErrorTriggerPriority");
					break;
				}
			}

			foreach (var question in protocol.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>())
			{
				if (string.IsNullOrWhiteSpace(question.Question))
				{
					errors.Add("ErrorQuestionText");
					break;
				}

				if (question.Answers == null || !question.Answers.Any())
				{
					errors.Add("ErrorQuestionAnswers");
					break;
				}

				if (question.Answers.Any(a => string.IsNullOrWhiteSpace(a.Answer)))
				{
					errors.Add("ErrorAnswerText");
					break;
				}
			}

			return errors;
		}

		/// <summary>
		/// The child ids in <paramref name="edited"/> that are not the current protocol's own: a trigger, question or answer id
		/// posted for a row of another protocol (or a question's answer under a different question). An edit naming any is refused.
		/// </summary>
		public static List<string> ForeignChildIds(DispatchProtocol current, DispatchProtocol edited)
		{
			var foreign = new List<string>();
			if (current == null || edited == null)
				return foreign;

			var triggerIds = new HashSet<int>((current.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>()).Select(x => x.DispatchProtocolTriggerId));
			foreach (var trigger in (edited.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>()).Where(x => x.DispatchProtocolTriggerId > 0))
				if (!triggerIds.Contains(trigger.DispatchProtocolTriggerId))
					foreign.Add("trigger:" + trigger.DispatchProtocolTriggerId);

			var questions = (current.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>()).ToDictionary(x => x.DispatchProtocolQuestionId);
			foreach (var question in edited.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>())
			{
				DispatchProtocolQuestion existing = null;
				if (question.DispatchProtocolQuestionId > 0 && !questions.TryGetValue(question.DispatchProtocolQuestionId, out existing))
				{
					foreign.Add("question:" + question.DispatchProtocolQuestionId);
					continue;
				}

				var answerIds = new HashSet<int>((existing?.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).Select(x => x.DispatchProtocolQuestionAnswerId));
				foreach (var answer in (question.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).Where(x => x.DispatchProtocolQuestionAnswerId > 0))
					if (!answerIds.Contains(answer.DispatchProtocolQuestionAnswerId))
						foreign.Add("answer:" + answer.DispatchProtocolQuestionAnswerId);
			}

			return foreign;
		}
	}
}
