namespace Resgrid.Web.Services.Models.v4.Voice
{
	/// <summary>
	/// Result of connecting to a voice session
	/// </summary>
	public class VoiceSessionConnectionResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public VoiceSessionConnectionResultData Data { get; set; }
	}

	/// <summary>
	/// Data needed to connect to an active voice session
	/// </summary>
	public class VoiceSessionConnectionResultData
	{
		/// <summary>
		/// Id used to connect to the session
		/// </summary>
		public string Token { get; set; }
	}
}
