using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooConferencesResult
	{
		[JsonProperty("page_size")]
		public int PageSize { get; set; }

		[JsonProperty("data")]
		public List<KazooConferenceResult> Data { get; set; }

		[JsonProperty("revision")]
		public string Revision { get; set; }

		[JsonProperty("timestamp")]
		public DateTime Timestamp { get; set; }

		[JsonProperty("version")]
		public string Version { get; set; }

		[JsonProperty("node")]
		public string Node { get; set; }

		[JsonProperty("request_id")]
		public string RequestId { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }

		[JsonProperty("auth_token")]
		public string AuthToken { get; set; }
	}

	public class KazooConferenceResult
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("moderator")]
		public Moderator Moderator { get; set; }

		[JsonProperty("member")]
		public Member Member { get; set; }

		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("flags")]
		public List<object> Flags { get; set; }

		[JsonProperty("conference_numbers")]
		public List<object> ConferenceNumbers { get; set; }

		[JsonProperty("_read_only")]
		public ReadOnly ReadOnly { get; set; }
	}

	public class Moderator
	{
		[JsonProperty("pins")]
		public List<string> Pins { get; set; }

		[JsonProperty("numbers")]
		public List<object> Numbers { get; set; }

		[JsonProperty("join_muted")]
		public bool JoinMuted { get; set; }

		[JsonProperty("join_deaf")]
		public bool JoinDeaf { get; set; }
	}

	public class Member
	{
		[JsonProperty("pins")]
		public List<string> Pins { get; set; }

		[JsonProperty("numbers")]
		public List<object> Numbers { get; set; }

		[JsonProperty("join_muted")]
		public bool JoinMuted { get; set; }

		[JsonProperty("join_deaf")]
		public bool JoinDeaf { get; set; }
	}

	public class ReadOnly
	{
		[JsonProperty("moderators")]
		public int Moderators { get; set; }

		[JsonProperty("members")]
		public int Members { get; set; }

		[JsonProperty("is_locked")]
		public bool IsLocked { get; set; }

		[JsonProperty("duration")]
		public int Duration { get; set; }
	}
}
