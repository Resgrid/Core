using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooAccountUsersResult
	{
		[JsonProperty("page_size")]
		public int PageSize { get; set; }

		[JsonProperty("data")]
		public List<KazooAccountUserDatumResult> Data { get; set; }

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

	public class KazooAccountUserDatumResult
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("features")]
		public List<string> Features { get; set; }

		[JsonProperty("username")]
		public string Username { get; set; }

		[JsonProperty("email")]
		public string Email { get; set; }

		[JsonProperty("first_name")]
		public string FirstName { get; set; }

		[JsonProperty("last_name")]
		public string LastName { get; set; }

		[JsonProperty("priv_level")]
		public string PrivLevel { get; set; }

		[JsonProperty("flags")]
		public List<object> Flags { get; set; }

		[JsonProperty("timezone")]
		public string Timezone { get; set; }
	}
}
