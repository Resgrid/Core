using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Framework;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Fields every incident-command (ICS) question an Incident Commander asks the assistant while
	/// working a command board — PAR, resources, span of control, objectives, needs, positions, the
	/// incident log, timers, weather, a transfer-of-command briefing, and the incident-type checklist.
	///
	/// One handler rather than a dozen because they all share the same shape: resolve which incident the
	/// question is about, load its board once, then narrate. Everything here is READ-ONLY — the
	/// assistant reports the board and never changes it, so none of these intents needs the confirmation
	/// or security-PIN gates the mutating handlers use.
	/// </summary>
	public class IncidentCommandActionHandler : IChatbotActionHandler
	{
		private static readonly HashSet<ChatbotIntentType> HandledIntents = new HashSet<ChatbotIntentType>
		{
			ChatbotIntentType.IncidentStatus,
			ChatbotIntentType.IncidentPar,
			ChatbotIntentType.IncidentResources,
			ChatbotIntentType.IncidentObjectives,
			ChatbotIntentType.IncidentNeeds,
			ChatbotIntentType.IncidentRoles,
			ChatbotIntentType.IncidentTimeline,
			ChatbotIntentType.IncidentTimers,
			ChatbotIntentType.IncidentSpanOfControl,
			ChatbotIntentType.IncidentBriefing,
			ChatbotIntentType.IncidentChecklist,
			ChatbotIntentType.IncidentWeather,
			ChatbotIntentType.IncidentNotes
		};

		private readonly IIncidentContextResolver _contextResolver;
		private readonly IIncidentBoardNarrator _narrator;

		public IncidentCommandActionHandler(IIncidentContextResolver contextResolver, IIncidentBoardNarrator narrator)
		{
			_contextResolver = contextResolver;
			_narrator = narrator;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.IncidentStatus;

		public bool CanHandle(ChatbotIntentType intentType) => HandledIntents.Contains(intentType);

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session?.Culture;

			try
			{
				// Resource and briefing answers name external (mutual-aid / volunteer) resources, which
				// live outside the board payload — only pay for that read when the answer will use it.
				var needsAdHoc = intent.Type == ChatbotIntentType.IncidentResources
					|| intent.Type == ChatbotIntentType.IncidentBriefing
					|| intent.Type == ChatbotIntentType.IncidentStatus;

				var context = await _contextResolver.ResolveAsync(intent, session, needsAdHoc);

				if (context.IsUnauthorized)
					return new ChatbotResponse { Text = ChatbotResources.Get("CallDetail_NoPermission", culture), Processed = false };

				if (context.IsAmbiguous)
					return new ChatbotResponse { Text = BuildAmbiguityPrompt(context, culture), Processed = false };

				if (context.Call == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Incident_NoActiveCommand", culture), Processed = false };

				if (context.BoardReadFailed)
					return new ChatbotResponse { Text = ChatbotResources.Get("Incident_BoardUnavailable", culture), Processed = false };

				if (context.HasNoCommand)
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Incident_NoCommandOnCall", culture, IncidentBoardNarrator.IncidentLabel(context)),
						Processed = true
					};

				if (!context.IsResolved)
					return new ChatbotResponse { Text = ChatbotResources.Get("Incident_BoardUnavailable", culture), Processed = false };

				var text = await NarrateAsync(intent, session, context);

				return new ChatbotResponse { Text = text, Processed = true };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Incident_Error", culture), Processed = false };
			}
		}

		private Task<string> NarrateAsync(ChatbotIntent intent, ChatbotSession session, IncidentContext context)
		{
			switch (intent.Type)
			{
				case ChatbotIntentType.IncidentPar:
					return _narrator.DescribeParAsync(context, session);

				case ChatbotIntentType.IncidentResources:
					return _narrator.DescribeResourcesAsync(context, session, GetParameter(intent, "laneName"));

				case ChatbotIntentType.IncidentSpanOfControl:
					return _narrator.DescribeSpanOfControlAsync(context, session);

				case ChatbotIntentType.IncidentObjectives:
					return _narrator.DescribeObjectivesAsync(context, session);

				case ChatbotIntentType.IncidentNeeds:
					return _narrator.DescribeNeedsAsync(context, session);

				case ChatbotIntentType.IncidentRoles:
					return _narrator.DescribeRolesAsync(context, session, GetParameter(intent, "roleQuery"));

				case ChatbotIntentType.IncidentTimeline:
					return _narrator.DescribeTimelineAsync(context, session, GetInt(intent, "minutes"), GetInt(intent, "count"));

				case ChatbotIntentType.IncidentTimers:
					return _narrator.DescribeTimersAsync(context, session);

				case ChatbotIntentType.IncidentNotes:
					return _narrator.DescribeNotesAsync(context, session);

				case ChatbotIntentType.IncidentBriefing:
					return _narrator.DescribeBriefingAsync(context, session);

				case ChatbotIntentType.IncidentChecklist:
					return _narrator.DescribeChecklistAsync(context, session, GetParameter(intent, "incidentType"));

				case ChatbotIntentType.IncidentWeather:
					return _narrator.DescribeWeatherAsync(context, session);

				case ChatbotIntentType.IncidentStatus:
				default:
					return _narrator.DescribeStatusAsync(context, session);
			}
		}

		/// <summary>
		/// Several incidents are running and the question didn't say which — list them so the commander
		/// can re-ask with a call number rather than guessing on their behalf.
		/// </summary>
		private static string BuildAmbiguityPrompt(IncidentContext context, string culture)
		{
			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_WhichOne", culture));

			foreach (var call in context.Candidates.Take(10))
				sb.AppendLine($"- {(string.IsNullOrWhiteSpace(call.Number) ? call.CallId.ToString(CultureInfo.InvariantCulture) : call.Number)}: {call.Name}");

			sb.Append(ChatbotResources.Get("Incident_WhichOneHint", culture));
			return sb.ToString();
		}

		private static string GetParameter(ChatbotIntent intent, string key)
			=> intent?.Parameters != null && intent.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
				? value.Trim()
				: null;

		private static int? GetInt(ChatbotIntent intent, string key)
		{
			var raw = GetParameter(intent, key);
			return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : (int?)null;
		}
	}
}
