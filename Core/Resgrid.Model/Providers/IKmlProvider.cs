using System.IO;

namespace Resgrid.Model.Providers
{
	public interface IKmlProvider
	{
		/// <summary>
		/// The placemarks of a KML or KMZ file. <paramref name="isKmz"/> is only a hint: the content decides, because
		/// converters save zipped KML as .kml and plain KML as .kmz.
		/// </summary>
		KmlImportResult ImportFile(Stream input, bool isKmz);
	}
}
