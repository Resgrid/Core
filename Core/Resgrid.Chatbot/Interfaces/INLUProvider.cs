using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface INLUProvider
	{
		/// <summary>
		/// Unique provider name for identification and logging.
		/// </summary>
		string ProviderName { get; }

		/// <summary>
		/// Priority for hybrid pipeline ordering. Lower values run first.
		/// 0 = keyword (fast/offline), 100 = cloud LLM.
		/// </summary>
		int Priority { get; }

		/// <summary>
		/// Classify raw input text into an NLU result.
		/// </summary>
		/// <param name="text">User input text</param>
		/// <param name="context">Optional conversation context string</param>
		/// <param name="departmentId">
		/// The department the message belongs to (0 when unknown). Cloud providers use this to honor a
		/// department's own LLM provider override; local providers ignore it.
		/// </param>
		Task<NLUResult> ClassifyAsync(string text, string context = null, int departmentId = 0);

		/// <summary>
		/// Whether this provider is available to classify.
		/// Cloud providers return false when no API key is configured.
		/// </summary>
		Task<bool> IsAvailableAsync();
	}
}
