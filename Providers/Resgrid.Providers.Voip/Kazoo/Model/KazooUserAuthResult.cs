using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooUserAuthResult
	{
		[JsonProperty("page_size")]
		public int PageSize { get; set; }

		[JsonProperty("data")]
		public KazooUserAuthDataResult Data { get; set; }

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

	public class KazooUserAuthDataResult
	{
		[JsonProperty("owner_id")]
		public string OwnerId { get; set; }

		[JsonProperty("account_id")]
		public string AccountId { get; set; }

		[JsonProperty("ui_config")]
		public UiConfig UiConfig { get; set; }

		[JsonProperty("capabilities")]
		public Capabilities Capabilities { get; set; }

		[JsonProperty("is_master_account")]
		public bool IsMasterAccount { get; set; }

		[JsonProperty("is_reseller")]
		public bool IsReseller { get; set; }

		[JsonProperty("reseller_id")]
		public string ResellerId { get; set; }

		[JsonProperty("cluster_id")]
		public string ClusterId { get; set; }

		[JsonProperty("account_name")]
		public string AccountName { get; set; }

		[JsonProperty("language")]
		public string Language { get; set; }

		[JsonProperty("apps")]
		public List<KazzoUserAuthAppsResult> Apps { get; set; }
	}

	public class KazzoUserAuthAppsResult
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("api_url")]
		public string ApiUrl { get; set; }

		[JsonProperty("label")]
		public string Label { get; set; }
	}

	public class UiConfig
	{
	}

	public class Transcription
	{
		[JsonProperty("default")]
		public bool Default { get; set; }

		[JsonProperty("available")]
		public bool Available { get; set; }
	}

	public class Voicemail
	{
		[JsonProperty("transcription")]
		public Transcription Transcription { get; set; }
	}

	public class Capabilities
	{
		[JsonProperty("voicemail")]
		public Voicemail Voicemail { get; set; }
	}
}
