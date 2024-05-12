using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Voice
{
	public class DepartmentVoiceResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public DepartmentVoiceResultData Data { get; set; }
	}

	public class DepartmentVoiceResultData
	{
		public bool VoiceEnabled { get; set; }

		public int Type { get; set; }

		public string Realm { get; set; }

		public string VoipServerWebsocketSslAddress { get; set; }

		public string CallerIdName { get; set; }

		public string CanConnectApiToken { get; set; }

		public List<DepartmentVoiceChannelResultData> Channels { get; set; }

		public DepartmentVoiceUserInfoResultData UserInfo { get; set; }
	}

	public class DepartmentVoiceChannelResultData
	{
		public string Id { get; set; }

		public string Name { get; set; }

		public int ConferenceNumber { get; set; }

		public bool IsDefault { get; set; }

		public string Token { get; set; }
	}

	public class DepartmentVoiceUserInfoResultData
	{
		public string Username { get; set; }
		public string Password { get; set; }
		public string Pin { get; set; }
	}
}
