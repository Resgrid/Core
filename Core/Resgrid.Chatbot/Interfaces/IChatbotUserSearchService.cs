using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>
	/// Department-scoped fuzzy name search over personnel. Shared by personnel lookup (P3.7),
	/// message recipient resolution (SendMessage), and inbound identity resolution (P3.17) so the
	/// name-matching logic lives in exactly one place. All results are constrained to the supplied
	/// department, and ambiguity is surfaced (rather than guessed) so callers can stay non-enumerable.
	/// </summary>
	public interface IChatbotUserSearchService
	{
		/// <summary>Returns department members whose name matches the query (empty query returns the roster).</summary>
		Task<List<ChatbotUserMatch>> SearchPersonnelAsync(int departmentId, string query, int max = 15);

		/// <summary>Best single match within the department, or null when there is no match or it's ambiguous.</summary>
		Task<ChatbotUserMatch> ResolveSingleAsync(int departmentId, string query);
	}
}
