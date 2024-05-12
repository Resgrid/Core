namespace Resgrid.Web.Services.Models.v4.Voice
{
	/// <summary>
	/// Result of checking if the user can connect to a voice session
	/// </summary>
	public class CanConnectToVoiceSessionResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CanConnectToVoiceSessionResultData Data { get; set; }
	}

	/// <summary>
	/// Data needed to verify the user can connect to a voice session
	/// </summary>
	public class CanConnectToVoiceSessionResultData
	{
		/// <summary>
		/// Can the User Connect to the voice session
		/// </summary>
		public bool CanConnect { get; set; }

		/// <summary>
		/// Current active voice session count
		/// </summary>
		public int CurrentSessions { get; set; }

		/// <summary>
		/// Max session count
		/// </summary>
		public int MaxSessions { get; set; }
	}
}
