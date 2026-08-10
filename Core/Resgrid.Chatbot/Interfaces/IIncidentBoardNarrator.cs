using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>
	/// Turns a loaded incident board into the answers an Incident Commander asked for. All read-only:
	/// nothing here mutates the incident. Separated from the action handlers so the reporting logic is
	/// testable on its own and reusable by the synchronous command-board endpoint, the chat pipeline,
	/// and the LLM grounding snapshot.
	/// </summary>
	public interface IIncidentBoardNarrator
	{
		/// <summary>Overall incident snapshot / size-up.</summary>
		Task<string> DescribeStatusAsync(IncidentContext context, ChatbotSession session);

		/// <summary>Personnel accountability (PAR): who is green, warning, and overdue.</summary>
		Task<string> DescribeParAsync(IncidentContext context, ChatbotSession session);

		/// <summary>What is working the incident, optionally scoped to one lane (or the unassigned pool).</summary>
		Task<string> DescribeResourcesAsync(IncidentContext context, ChatbotSession session, string laneName);

		/// <summary>Lanes over or under their configured limits, and lanes running without a lead.</summary>
		Task<string> DescribeSpanOfControlAsync(IncidentContext context, ChatbotSession session);

		/// <summary>Tactical objectives / benchmarks, and the doctrine benchmarks not yet on the board.</summary>
		Task<string> DescribeObjectivesAsync(IncidentContext context, ChatbotSession session);

		/// <summary>Command-level needs (resource orders) and what hasn't been filled.</summary>
		Task<string> DescribeNeedsAsync(IncidentContext context, ChatbotSession session);

		/// <summary>Who holds an ICS position, or which positions this incident type still needs.</summary>
		Task<string> DescribeRolesAsync(IncidentContext context, ChatbotSession session, string roleQuery);

		/// <summary>Recent incident (ICS-201) timeline entries, by count or by time window.</summary>
		Task<string> DescribeTimelineAsync(IncidentContext context, ChatbotSession session, int? minutes, int? count);

		/// <summary>Incident timers: what's running, what's due, what's been acknowledged.</summary>
		Task<string> DescribeTimersAsync(IncidentContext context, ChatbotSession session);

		/// <summary>Operational status notes recorded on the incident.</summary>
		Task<string> DescribeNotesAsync(IncidentContext context, ChatbotSession session);

		/// <summary>A transfer-of-command / ICS-201 style briefing built from the live board.</summary>
		Task<string> DescribeBriefingAsync(IncidentContext context, ChatbotSession session);

		/// <summary>
		/// The incident-type ICS checklist, with the items the board can already prove marked done.
		/// <paramref name="incidentTypeText"/> overrides the type inferred from the call.
		/// </summary>
		Task<string> DescribeChecklistAsync(IncidentContext context, ChatbotSession session, string incidentTypeText);

		/// <summary>Current conditions at the incident (command post coordinates first, then the call's).</summary>
		Task<string> DescribeWeatherAsync(IncidentContext context, ChatbotSession session);

		/// <summary>
		/// Compact, factual snapshot of the board for grounding a language model. Contains only what the
		/// caller is already authorized to see and is capped in size so it fits a modest context window.
		/// </summary>
		Task<string> BuildGroundingSnapshotAsync(IncidentContext context, ChatbotSession session);
	}
}
