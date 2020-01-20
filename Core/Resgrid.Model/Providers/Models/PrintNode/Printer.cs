using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model.Providers.Models.PrintNode
{
	public partial class Printer
	{
		[JsonProperty("capabilities")]
		public Capabilities Capabilities { get; set; }

		[JsonProperty("computer")]
		public Computer Computer { get; set; }

		[JsonProperty("createTimestamp")]
		public DateTimeOffset CreateTimestamp { get; set; }

		[JsonProperty("default")]
		public bool Default { get; set; }

		[JsonProperty("description")]
		public string Description { get; set; }

		[JsonProperty("id")]
		public long Id { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("state")]
		public string State { get; set; }
	}

	public partial class Capabilities
	{
		[JsonProperty("bins")]
		public List<string> Bins { get; set; }

		[JsonProperty("collate")]
		public bool Collate { get; set; }

		[JsonProperty("color")]
		public bool Color { get; set; }

		[JsonProperty("copies")]
		public long Copies { get; set; }

		[JsonProperty("dpis")]
		public List<string> Dpis { get; set; }

		[JsonProperty("duplex")]
		public bool Duplex { get; set; }

		[JsonProperty("extent")]
		public List<List<long>> Extent { get; set; }

		[JsonProperty("medias")]
		public List<object> Medias { get; set; }

		[JsonProperty("nup")]
		public List<long> Nup { get; set; }

		[JsonProperty("papers")]
		public Dictionary<string, List<long>> Papers { get; set; }

		[JsonProperty("printrate")]
		public Printrate Printrate { get; set; }

		[JsonProperty("supports_custom_paper_size")]
		public bool SupportsCustomPaperSize { get; set; }
	}

	public partial class Printrate
	{
		[JsonProperty("rate")]
		public long Rate { get; set; }

		[JsonProperty("unit")]
		public string Unit { get; set; }
	}
}
