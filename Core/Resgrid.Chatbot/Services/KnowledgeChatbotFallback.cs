using System;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// K1 routing in the Assistant chat (web, Omni and the chatbot channels): a question no intent matched goes to the department's
	/// knowledge answers when the department has Ai.Knowledge, before the conversational fallback. Operational commands never reach here
	/// (matched intents are dispatched first), incident-board questions stay with the incident assistant, and Admin Assist has its own
	/// surface and routing, which this does not touch. The reply is plain text (assistant conversations are text-only): the answer with
	/// [S1]-style references and the sources with their version, effective date and link. Anything else falls through to the inner fallback.
	/// </summary>
	public class KnowledgeChatbotFallback : IChatbotConversationalFallback
	{
		private static readonly ResourceManager Text = new ResourceManager(typeof(Resgrid.Localization.Areas.User.Knowledge.Knowledge));
		private static readonly string[] QuestionStarts =
		{
			"what", "how", "when", "where", "which", "who", "why", "can", "should", "do", "does", "is", "are", "may", "must", "explain", "tell me"
		};

		private readonly IChatbotConversationalFallback _inner;
		private readonly IAiKnowledgeService _knowledge;

		public KnowledgeChatbotFallback(IChatbotConversationalFallback inner, IAiKnowledgeService knowledge)
		{
			_inner = inner;
			_knowledge = knowledge;
		}

		public async Task<ChatbotResponse> TryHandleAsync(ChatbotMessage message, ChatbotSession session)
		{
			ChatbotResponse answered = null;
			try
			{
				answered = await TryKnowledgeAsync(message, session);
			}
			catch (Exception ex)
			{
				// Knowledge is an extra; a failure falls through to the usual fallback.
				Logging.LogException(ex, "Knowledge routing in the assistant failed; using the conversational fallback.");
			}

			return answered ?? await _inner.TryHandleAsync(message, session);
		}

		/// <summary>A question rather than a remark: it ends with a question mark, or starts like one.</summary>
		public static bool LooksLikeQuestion(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;
			var trimmed = text.Trim();
			if (trimmed.EndsWith("?", StringComparison.Ordinal) || trimmed.EndsWith("؟", StringComparison.Ordinal))
				return true;
			var lower = trimmed.ToLowerInvariant();
			return trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 3 &&
				QuestionStarts.Any(start => lower.StartsWith(start + " ", StringComparison.Ordinal));
		}

		private async Task<ChatbotResponse> TryKnowledgeAsync(ChatbotMessage message, ChatbotSession session)
		{
			if (message == null || session == null || session.DepartmentId <= 0 || string.IsNullOrWhiteSpace(session.UserId) || !LooksLikeQuestion(message.Text))
				return null;
			// A commander working a board asks about the incident: the incident assistant answers from the board.
			if (session.Context != null && session.Context.ContainsKey(IncidentContextResolver.IncidentCallIdContextKey))
				return null;
			if (!await _knowledge.IsEntitledAsync(session.DepartmentId))
				return null;

			var answer = await _knowledge.AskAsync(new KnowledgeAskRequest
			{
				// Every member holds the Protocols, Training and Documents view claims; each item's own read rule still decides.
				Reader = new KnowledgeReader
				{
					DepartmentId = session.DepartmentId,
					UserId = session.UserId,
					Locale = session.Culture,
					CanViewProtocols = true,
					CanViewTrainings = true,
					CanViewDocuments = true
				},
				Question = message.Text,
				Surface = (int)KnowledgeAskSurfaces.Assistant
			});

			var reply = Format(answer, session.Culture);
			return reply == null ? null : new ChatbotResponse
			{
				Text = reply,
				Processed = true,
				Intent = new ChatbotIntent { Type = ChatbotIntentType.Unknown, Confidence = 0 }
			};
		}

		/// <summary>The answer as plain text, or null when there is nothing to say (the usual fallback then answers).</summary>
		public static string Format(KnowledgeAnswer answer, string culture)
		{
			if (answer == null)
				return null;
			var ci = CultureFor(culture);
			string L(string key) => Text.GetString(key, ci) ?? key;

			var text = new StringBuilder();
			var sources = answer.Citations;
			switch (answer.Outcome)
			{
				case KnowledgeOutcomes.Answered:
					foreach (var part in answer.Parts)
						text.Append(part.Text).Append(' ').Append(string.Join(" ", part.Refs.Select(r => "[" + r + "]"))).Append('\n');
					foreach (var conflict in answer.Conflicts)
						text.Append(L("ConflictLabel")).Append(": ").Append(conflict.Note).Append(' ').Append(string.Join(" ", conflict.Refs.Select(r => "[" + r + "]"))).Append('\n');
					break;
				case KnowledgeOutcomes.NotCovered:
					text.Append(L("NotCovered")).Append('\n');
					sources = answer.Nearest;
					break;
				case KnowledgeOutcomes.Matched:
					text.Append(answer.QuestionKeptLocal ? L("KeptLocalNotice") : L("MatchedNotice")).Append('\n');
					break;
				default:
					// Busy, budget, outage: say nothing here and let the usual assistant answer.
					return null;
			}

			if (answer.QuotesDose)
				text.Append(L("DoseNotice")).Append(string.IsNullOrWhiteSpace(answer.DosingToolUrl) ? string.Empty : " " + answer.DosingToolUrl).Append('\n');

			if (sources != null && sources.Count > 0)
			{
				text.Append('\n').Append(answer.Outcome == KnowledgeOutcomes.NotCovered ? L("NearestLabel") : L("SourcesLabel")).Append(":\n");
				var root = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/');
				foreach (var c in sources)
				{
					text.Append(c.Ref).Append(' ').Append(c.Title);
					if (!string.IsNullOrWhiteSpace(c.HeadingPath))
						text.Append(" - ").Append(c.HeadingPath);
					if (!string.IsNullOrWhiteSpace(c.VersionLabel))
						text.Append(" (").Append(string.Format(L("VersionLabel"), c.VersionLabel)).Append(')');
					if (c.EffectiveOn.HasValue)
						text.Append(", ").Append(string.Format(L("EffectiveLabel"), c.EffectiveOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
					var path = KnowledgeLinks.WebPath(c.SourceKind, c.ItemId, c.VersionId, c.Anchor, Config.SystemBehaviorConfig.AcademyUrl);
					if (!string.IsNullOrWhiteSpace(path))
						text.Append(' ').Append(path.StartsWith("/", StringComparison.Ordinal) ? root + path : path);
					text.Append('\n');
				}
			}

			var result = text.ToString().Trim();
			return result.Length == 0 ? null : result;
		}

		private static CultureInfo CultureFor(string culture)
		{
			try
			{
				return CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(culture) ? "en" : culture.Split('-', '_')[0]);
			}
			catch (CultureNotFoundException)
			{
				return CultureInfo.GetCultureInfo("en");
			}
		}
	}
}
