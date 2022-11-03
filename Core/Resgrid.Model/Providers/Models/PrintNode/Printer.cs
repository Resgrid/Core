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
		public Papers Papers { get; set; }

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

	public class Papers
	{
		public List<int> A3 { get; set; }
		public List<int> A4 { get; set; }
		public List<int> A5 { get; set; }
		public List<int> B4 { get; set; }
		public List<int> B5 { get; set; }

		[JsonProperty("Custom [Name Fixed]")]
		public List<int> CustomNameFixed { get; set; }

		[JsonProperty("Envelope C5")]
		public List<int> EnvelopeC5 { get; set; }

		[JsonProperty("Envelope DL")]
		public List<int> EnvelopeDL { get; set; }

		[JsonProperty("Envelope Monarch")]
		public List<int> EnvelopeMonarch { get; set; }

		[JsonProperty("Envelope NAGAGATA 3")]
		public List<int> EnvelopeNAGAGATA3 { get; set; }

		[JsonProperty("Envelope No. 10 (COM10)")]
		public List<int> EnvelopeNo10COM10 { get; set; }

		[JsonProperty("Envelope YOUGATANAGA 3")]
		public List<int> EnvelopeYOUGATANAGA3 { get; set; }
		public List<int> Executive { get; set; }

		[JsonProperty("Index Card")]
		public List<int> IndexCard { get; set; }
		public List<int> Legal { get; set; }
		public List<int> Letter { get; set; }
		public List<int> Postcard { get; set; }

		[JsonProperty("Reply Postcard")]
		public List<int> ReplyPostcard { get; set; }
		public List<int> Statement { get; set; }
	}
}
