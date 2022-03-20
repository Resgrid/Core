using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCallflowDetailsResult
	{
		[JsonProperty("data")]
		public KazooCallflowDetailDataResult Data { get; set; }

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

	public class KazooCallflowDetailDataResult
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("flow")]
		public Flow Flow { get; set; }

		[JsonProperty("numbers")]
		public List<string> Numbers { get; set; }

		[JsonProperty("ui_metadata")]
		public UiMetadata UiMetadata { get; set; }

		[JsonProperty("patterns")]
		public List<object> Patterns { get; set; }

		[JsonProperty("metadata")]
		public Dictionary<string, CallFlowMetadataValue> Metadata { get; set; }
	}

	public class Flow
	{
		[JsonProperty("data")]
		public FlowData Data { get; set; }

		[JsonProperty("module")]
		public string Module { get; set; }

		[JsonProperty("children")]
		public object Children { get; set; }
	}

	public class FlowData
	{
		[JsonProperty("id")]
		public string Id { get; set; }
	}

	public class UiMetadata
	{
		[JsonProperty("version")]
		public string Version { get; set; }

		[JsonProperty("ui")]
		public string Ui { get; set; }

		[JsonProperty("origin")]
		public string Origin { get; set; }
	}

	public class CallFlowMetadataValue
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("pvt_type")]
		public string PvtType { get; set; }
	}

}
