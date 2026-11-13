using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>What a KML or KMZ upload yielded.</summary>
	public class KmlImportResult
	{
		/// <summary>Placemarks with a usable position.</summary>
		public List<Coordinates> Coordinates { get; set; } = new List<Coordinates>();

		/// <summary>
		/// Placemarks without a usable position that carry an address the caller can geocode: an &lt;address&gt; element,
		/// address columns in ExtendedData, or both. Google My Maps exports a layer it located "by address" this way,
		/// with no &lt;Point&gt; at all. Not counted in <see cref="SkippedCount"/>.
		/// </summary>
		public List<KmlAddressPlacemark> AddressOnly { get; set; } = new List<KmlAddressPlacemark>();

		/// <summary>Placemarks passed over because they have neither a usable position nor an address.</summary>
		public int SkippedCount { get; set; }

		/// <summary>Set when the file could not be read at all: a message the uploader can act on.</summary>
		public string Error { get; set; }
	}

	/// <summary>
	/// A placemark with a usable position (<see cref="KmlImportResult.Coordinates"/> holds these) that also carries ExtendedData,
	/// such as a "Capabilities" column a POI import reads. Never serialized: the protobuf members are the base class's.
	/// </summary>
	public class KmlPositionedPlacemark : Coordinates
	{
		/// <summary>ExtendedData Data and SimpleData values in document order, keyed by their name attribute.</summary>
		public List<KeyValuePair<string, string>> Fields { get; set; } = new List<KeyValuePair<string, string>>();
	}

	/// <summary>A placemark that has no usable position but may be located from its address.</summary>
	public class KmlAddressPlacemark
	{
		public string Name { get; set; }

		/// <summary>The placemark's &lt;address&gt; element, when it has one.</summary>
		public string Address { get; set; }

		/// <summary>ExtendedData Data and SimpleData values in document order, keyed by their name attribute.</summary>
		public List<KeyValuePair<string, string>> Fields { get; set; } = new List<KeyValuePair<string, string>>();
	}
}
