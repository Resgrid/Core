using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCreateUserResult
	{
		[JsonProperty("auth_token")]
		public string AuthToken { get; set; }

		[JsonProperty("data")]
		public KazooCreateUserDataResult Data { get; set; }

		[JsonProperty("request_id")]
		public string RequestId { get; set; }

		[JsonProperty("revision")]
		public string Revision { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }
	}

	public class KazooCreateUserDataResult
	{
		[JsonProperty("call_restriction")]
		public object CallRestriction { get; set; }

		[JsonProperty("caller_id")]
		public object CallerId { get; set; }

		[JsonProperty("contact_list")]
		public object ContactList { get; set; }

		[JsonProperty("dial_plan")]
		public object DialPlan { get; set; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		[JsonProperty("first_name")]
		public string FirstName { get; set; }

		[JsonProperty("hotdesk")]
		public Hotdesk Hotdesk { get; set; }

		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("last_name")]
		public string LastName { get; set; }

		[JsonProperty("media")]
		public Media Media { get; set; }

		[JsonProperty("music_on_hold")]
		public object MusicOnHold { get; set; }

		[JsonProperty("priv_level")]
		public string PrivLevel { get; set; }

		[JsonProperty("profile")]
		public object Profile { get; set; }

		[JsonProperty("require_password_update")]
		public bool RequirePasswordUpdate { get; set; }

		[JsonProperty("ringtones")]
		public object Ringtones { get; set; }

		[JsonProperty("verified")]
		public bool Verified { get; set; }

		[JsonProperty("vm_to_email_enabled")]
		public bool VmToEmailEnabled { get; set; }
	}

	public class Media
	{
		[JsonProperty("audio")]
		public Audio Audio { get; set; }

		[JsonProperty("encryption")]
		public Encryption Encryption { get; set; }

		[JsonProperty("video")]
		public Video Video { get; set; }
	}

	public class Hotdesk
	{
		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		[JsonProperty("keep_logged_in_elsewhere")]
		public bool KeepLoggedInElsewhere { get; set; }

		[JsonProperty("require_pin")]
		public bool RequirePin { get; set; }
	}

	public class Audio
	{
		[JsonProperty("codecs")]
		public List<string> Codecs { get; set; }
	}

	public class Encryption
	{
		[JsonProperty("enforce_security")]
		public bool EnforceSecurity { get; set; }

		[JsonProperty("methods")]
		public List<object> Methods { get; set; }
	}

	public class Video
	{
		[JsonProperty("codecs")]
		public List<object> Codecs { get; set; }
	}
}
