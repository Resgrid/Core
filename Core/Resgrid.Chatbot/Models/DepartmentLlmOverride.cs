namespace Resgrid.Chatbot.Models
{
	/// <summary>
	/// A department's own LLM/AI provider settings (decrypted key) used to keep that department's
	/// chatbot processing with their own provider instead of the Resgrid system default.
	/// </summary>
	public class DepartmentLlmOverride
	{
		public string Endpoint { get; set; }
		public string ApiKey { get; set; }
		public string Model { get; set; }
	}
}
