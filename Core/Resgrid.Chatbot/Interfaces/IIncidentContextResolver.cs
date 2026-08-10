using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Model;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>
	/// The incident an assistant question is about, resolved and loaded once per message so the handlers
	/// share a single board read.
	/// </summary>
	public class IncidentContext
	{
		/// <summary>The call the command runs on. Null when nothing could be resolved.</summary>
		public Call Call { get; set; }

		/// <summary>Full board snapshot (structure, assignments, objectives, needs, PAR, roles, ...).</summary>
		public IncidentCommandBoard Board { get; set; }

		/// <summary>Convenience accessor for the command row on <see cref="Board"/>.</summary>
		public IncidentCommand Command => Board?.Command;

		/// <summary>Ad-hoc (non-Resgrid) units tracked on the incident.</summary>
		public List<IncidentAdHocUnit> AdHocUnits { get; set; } = new List<IncidentAdHocUnit>();

		/// <summary>Ad-hoc (external / mutual-aid / volunteer) personnel tracked on the incident.</summary>
		public List<IncidentAdHocPersonnel> AdHocPersonnel { get; set; } = new List<IncidentAdHocPersonnel>();

		/// <summary>True when the user is working more than one incident and didn't say which one.</summary>
		public bool IsAmbiguous { get; set; }

		/// <summary>Candidate incidents when <see cref="IsAmbiguous"/> — used to build the disambiguation prompt.</summary>
		public List<Call> Candidates { get; set; } = new List<Call>();

		/// <summary>Set when the caller isn't allowed to see the call they named.</summary>
		public bool IsUnauthorized { get; set; }

		/// <summary>
		/// Set when the call resolved but reading its command board threw — the board's state is
		/// unknown, which is different from "no command established".
		/// </summary>
		public bool BoardReadFailed { get; set; }

		/// <summary>
		/// True when a call resolved but no incident command has been established on it — the assistant
		/// can still say something useful ("no command established"), so it isn't the same as not-found.
		/// A failed board read is excluded: an unreadable board says nothing about whether command exists.
		/// </summary>
		public bool HasNoCommand => Call != null && Board?.Command == null && !BoardReadFailed;

		public bool IsResolved => Call != null && Board?.Command != null;
	}

	/// <summary>Resolves which incident an assistant question applies to, and loads its board.</summary>
	public interface IIncidentContextResolver
	{
		/// <summary>
		/// Resolution order: an explicit call reference in the question ("PAR for 26-1"), then the
		/// incident the client said it has open (<c>incidentCallId</c> in the session context, set by the
		/// IC app's command board), then the department's active commands — preferring one the caller
		/// commands, and asking which when it's still ambiguous.
		/// </summary>
		Task<IncidentContext> ResolveAsync(ChatbotIntent intent, ChatbotSession session, bool includeAdHocResources = false);
	}
}
