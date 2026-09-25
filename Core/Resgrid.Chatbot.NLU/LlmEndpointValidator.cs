namespace Resgrid.Chatbot.NLU
{
	/// <summary>Compatibility facade for the shared public-only endpoint policy. Department overrides never use operator-private access.</summary>
	public static class LlmEndpointValidator
	{
		public static bool IsValid(string endpoint, out string error) => Resgrid.Llm.PublicEndpointPolicy.IsValid(endpoint, out error);
	}
}
