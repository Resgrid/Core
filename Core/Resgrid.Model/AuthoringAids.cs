using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>The K2 authoring aids (plan §16.4, §24 K2): drafts an author reviews; nothing is saved until they accept it.</summary>
	public enum AuthoringAidKinds
	{
		/// <summary>Quiz questions with explanations from the content.</summary>
		Quiz = 0,

		/// <summary>A course outline (modules and lessons) from the content (an SOP, a protocol, a training).</summary>
		Outline = 1,

		/// <summary>A change summary from a version diff (only the diff is sent).</summary>
		ChangeSummary = 2
	}

	/// <summary>What the draft is grounded in.</summary>
	public enum AuthoringAidSourceKinds
	{
		/// <summary>A protocol version (P1), by ProtocolVersionId.</summary>
		ProtocolVersion = 0,

		/// <summary>A baseline training, by TrainingId, or the editor's unsaved content for a new training (id 0).</summary>
		Training = 1,

		/// <summary>An LMS course version (T2), by LmsCourseVersionId.</summary>
		CourseVersion = 2
	}

	/// <summary>The outcome of one aid request (also AiGenerations.Outcome for the drafted ones).</summary>
	public static class AuthoringAidOutcomes
	{
		/// <summary>A draft to review.</summary>
		public const string Drafted = "Drafted";

		/// <summary>The department has no knowledge answers (Enhanced AI): the aids are not shown at all.</summary>
		public const string NotEntitled = "NotEntitled";

		/// <summary>The member may not author this content.</summary>
		public const string NotAllowed = "NotAllowed";

		public const string NotFound = "NotFound";

		/// <summary>The content is marked "no AI" (licensed content, decision 14): it is never sent to a model.</summary>
		public const string Excluded = "Excluded";

		/// <summary>Advanced Data Protection keeps the department's content local: no model call.</summary>
		public const string KeptLocal = "KeptLocal";

		/// <summary>A change summary with no earlier version, or no changes to summarize.</summary>
		public const string NoChanges = "NoChanges";

		/// <summary>The content is empty or too short to draft from.</summary>
		public const string NoContent = "NoContent";

		/// <summary>The operator's model is not configured, or the exchange could not be audited (fails closed).</summary>
		public const string Unconfigured = "Unconfigured";

		public const string Unavailable = "Unavailable";
		public const string Busy = "Busy";
		public const string BudgetExhausted = "BudgetExhausted";

		/// <summary>The model's output was refused (invalid, or every item contained a number the source does not).</summary>
		public const string Refused = "Refused";

		public const string Invalid = "Invalid";
	}

	/// <summary>One aid request from an editor.</summary>
	public sealed class AuthoringAidRequest
	{
		public const int ContentMaxLength = 200000;
		public const int MinQuestions = 1;
		public const int MaxQuestions = 10;

		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }

		/// <summary><see cref="AuthoringAidKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary><see cref="AuthoringAidSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		public int SourceId { get; set; }

		/// <summary>The training editor's unsaved title and content (the author's own text); ignored for other sources.</summary>
		public string ContentTitle { get; set; }

		public string ContentHtml { get; set; }

		/// <summary>The editor's "exclude from AI" box as it stands (unsaved); a ticked box refuses the aid like the stored flag.</summary>
		public bool ExcludeFromAi { get; set; }

		public int QuestionCount { get; set; } = 5;
	}

	/// <summary>The grounding a source hands the aid: the plain text, or for a change summary only the diff.</summary>
	public sealed class AuthoringAidSource
	{
		public string Title { get; set; }

		/// <summary>The content as plain text (quiz and outline).</summary>
		public string Text { get; set; }

		/// <summary>The version diff as marked plain text (change summary); null when there is no earlier version.</summary>
		public string DiffText { get; set; }

		public string VersionLabel { get; set; }
		public string PreviousVersionLabel { get; set; }

		/// <summary>"protocol-version:12": what the audit row cites.</summary>
		public string ItemKey { get; set; }

		public bool ExcludedFromAi { get; set; }
	}

	/// <summary>A source's answer: the grounding, or why there is none.</summary>
	public sealed class AuthoringAidSourceResult
	{
		public string Outcome { get; set; }
		public AuthoringAidSource Source { get; set; }

		public static AuthoringAidSourceResult Fail(string outcome) => new AuthoringAidSourceResult { Outcome = outcome };

		public static AuthoringAidSourceResult Ok(AuthoringAidSource source) => new AuthoringAidSourceResult { Source = source };
	}

	public sealed class AuthoringAidQuestionDraft
	{
		public string Text { get; set; }
		public List<string> Options { get; set; } = new List<string>();
		public int CorrectIndex { get; set; }
		public string Explanation { get; set; }
	}

	public sealed class AuthoringAidOutlineModule
	{
		public string Title { get; set; }
		public string Objective { get; set; }
		public List<string> Lessons { get; set; } = new List<string>();
	}

	/// <summary>A draft for the author to review. Nothing in it has been saved anywhere but the audit row.</summary>
	public sealed class AuthoringAidDraft
	{
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }
		public int Kind { get; set; }
		public string SourceTitle { get; set; }
		public List<AuthoringAidQuestionDraft> Questions { get; set; } = new List<AuthoringAidQuestionDraft>();
		public List<AuthoringAidOutlineModule> Outline { get; set; } = new List<AuthoringAidOutlineModule>();
		public List<string> Changes { get; set; } = new List<string>();

		/// <summary>Drafted items left out because they held a number the source does not (no invented clinical numbers).</summary>
		public int Dropped { get; set; }

		public string PromptVersion { get; set; }
	}

	public sealed class AuthoringAidStatus
	{
		public bool Available { get; set; }
		public string Reason { get; set; }
	}

	/// <summary>
	/// A version diff as the plain text a change summary is drafted from (K2): only the changed paragraphs, each marked "+" (added), "-"
	/// (removed) or "~" (changed, with [+added] and [-removed] word runs and a few words of unchanged context). Unchanged paragraphs are
	/// never included, so the summary rests on the diff alone.
	/// </summary>
	public static class AuthoringAidDiffText
	{
		public const int MaxLength = 12000;
		private const int ContextWords = 6;

		public static string From(ProtocolDiffResult diff)
		{
			if (diff == null || !diff.HasChanges)
				return null;

			var text = new StringBuilder();
			foreach (var paragraph in diff.Paragraphs.Where(p => p.Changed))
			{
				var segments = paragraph.Segments ?? new List<ProtocolDiffSegment>();
				string line;
				if (segments.All(s => s.Kind == ProtocolDiffKinds.Inserted))
					line = "+ " + Join(segments);
				else if (segments.All(s => s.Kind == ProtocolDiffKinds.Deleted))
					line = "- " + Join(segments);
				else
				{
					var parts = new List<string>();
					for (var i = 0; i < segments.Count; i++)
					{
						var segment = segments[i];
						var words = (segment.Text ?? string.Empty).Trim();
						if (words.Length == 0)
							continue;
						switch (segment.Kind)
						{
							case ProtocolDiffKinds.Inserted:
								parts.Add("[+" + words + "]");
								break;
							case ProtocolDiffKinds.Deleted:
								parts.Add("[-" + words + "]");
								break;
							default:
								parts.Add(Context(words, i > 0, i < segments.Count - 1));
								break;
						}
					}
					line = "~ " + string.Join(" ", parts);
				}

				if (paragraph.IsHeading)
					line += " (heading)";
				if (text.Length + line.Length + 1 > MaxLength)
				{
					text.Append("…");
					break;
				}
				text.Append(line).Append('\n');
			}

			var result = text.ToString().Trim();
			return result.Length == 0 ? null : result;
		}

		private static string Join(IEnumerable<ProtocolDiffSegment> segments) =>
			string.Join(" ", segments.Select(s => (s.Text ?? string.Empty).Trim()).Where(s => s.Length > 0));

		/// <summary>Unchanged words around a change: a few on each side that touches one, the middle elided.</summary>
		private static string Context(string words, bool afterChange, bool beforeChange)
		{
			var list = words.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
			if (list.Length <= ContextWords * 2)
				return words;
			var head = afterChange ? string.Join(" ", list.Take(ContextWords)) : string.Empty;
			var tail = beforeChange ? string.Join(" ", list.Skip(list.Length - ContextWords)) : string.Empty;
			return (head + " … " + tail).Trim();
		}
	}
}
