using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v3.Voice
{
	public class DepartmentVoiceResult
	{
		public bool VoiceEnabled { get; set; }

		public string Realm { get; set; }

		public string VoipServerWebsocketSslAddress { get; set; }

		public string CallerIdName { get; set; }

		public List<DepartmentVoiceChannelResult> Channels { get; set; }

		public DepartmentVoiceUserInfoResult UserInfo { get; set; }
	}

	public class DepartmentVoiceUserInfoResult
	{
		public string Username { get; set; }
		public string Password { get; set; }
		public string Pin { get; set; }
	}
}
