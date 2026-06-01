using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public class NLUResult
	{
		public string IntentName { get; set; }
		public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
		public double Confidence { get; set; }
		public string RawResponse { get; set; }
		public string ProviderName { get; set; }

		/// <summary>
		/// Token usage from cloud LLM classification (input + output).
		/// </summary>
		public int? TotalTokens { get; set; }

		/// <summary>
		/// Latency in milliseconds for this classification.
		/// </summary>
		public long LatencyMs { get; set; }

		/// <summary>
		/// Whether this result came from a fallback provider (hybrid mode).
		/// </summary>
		public bool IsFallbackResult { get; set; }

		/// <summary>
		/// The model name used (cloud providers only).
		/// </summary>
		public string ModelName { get; set; }
	}
}
