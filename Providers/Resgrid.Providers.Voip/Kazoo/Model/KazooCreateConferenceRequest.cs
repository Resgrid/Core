using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCreateConferenceRequest
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("member")]
		public MemberRequest Member { get; set; }

		[JsonProperty("profile")]
		public ProfileRequest Profile { get; set; }

		[JsonProperty("play_entry_tone")]
		public bool PlayEntryTone { get; set; }

		[JsonProperty("play_exit_tone")]
		public bool PlayExitTone { get; set; }
	}

	public class MemberRequest
	{
		[JsonProperty("pins")]
		public List<string> Pins { get; set; }

		[JsonProperty("join_muted")]
		public bool JoinMuted { get; set; }

		[JsonProperty("join_deaf")]
		public bool JoinDeaf { get; set; }
	}

	public class ProfileRequest
	{
		[JsonProperty("alone-sound")]
		public string AloneSound { get; set; }

		[JsonProperty("enter-sound")]
		public string EnterSound { get; set; }

		[JsonProperty("exit-sound")]
		public string ExitSound { get; set; }
	}
}
