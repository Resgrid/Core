using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SharpKml.Base;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Resgrid.Providers.GeoLocationProvider
{
	/// <summary>
	/// Reads an uploaded KML or KMZ file the way Google Earth does. KmlFile.Load is namespace-aware, so it returns no
	/// elements at all for the files most converters write: the KML 2.0/2.1 namespaces (earth.google.com), no namespace,
	/// or whitespace before the XML declaration. Throws XmlException for malformed XML and InvalidDataException for a
	/// damaged KMZ.
	/// </summary>
	public static class KmlDocumentReader
	{
		private static readonly char[] LeadingNoise = { (char)0xFEFF, ' ', '\t', '\r', '\n' };

		private static readonly Regex EncodingDeclaration = new Regex(@"<\?xml[^>]*?encoding\s*=\s*[""']([A-Za-z0-9._:-]+)[""']", RegexOptions.IgnoreCase);

		private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

		/// <summary>
		/// The root element, or null when the file holds no KML. The root may be a Document, Folder or Placemark instead
		/// of kml, so callers walk it with Flatten(). Zipped content is read as KMZ whatever the file is called, because
		/// converters save zipped KML as .kml and plain KML as .kmz.
		/// </summary>
		public static Element ReadRoot(Stream input)
		{
			if (input == null)
				return null;

			using (var buffer = new MemoryStream())
			{
				input.CopyTo(buffer);
				return ReadRoot(buffer.ToArray());
			}
		}

		/// <inheritdoc cref="ReadRoot(Stream)"/>
		public static Element ReadRoot(byte[] content)
		{
			if (content == null || content.Length == 0)
				return null;

			string kml;
			if (IsZip(content))
			{
				using (var kmz = KmzFile.Open(new MemoryStream(content)))
					kml = kmz.ReadKml();
			}
			else
				kml = DecodeText(content);

			if (string.IsNullOrWhiteSpace(kml))
				return null;

			var parser = new Parser();
			parser.ParseString(kml.TrimStart(LeadingNoise), false);

			return parser.Root;
		}

		private static bool IsZip(byte[] content) =>
			content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;

		/// <summary>
		/// A byte-order mark wins, then the encoding the XML declaration names, then UTF-8. Bytes that are not UTF-8 are
		/// read as Latin-1, which is what legacy converters wrote while declaring UTF-8.
		/// </summary>
		private static string DecodeText(byte[] content)
		{
			try
			{
				return Decode(content, DeclaredEncoding(content) ?? StrictUtf8);
			}
			catch (DecoderFallbackException)
			{
				return Decode(content, Encoding.Latin1);
			}
		}

		private static string Decode(byte[] content, Encoding encoding)
		{
			using (var reader = new StreamReader(new MemoryStream(content), encoding, detectEncodingFromByteOrderMarks: true))
				return reader.ReadToEnd();
		}

		private static Encoding DeclaredEncoding(byte[] content)
		{
			var match = EncodingDeclaration.Match(Encoding.ASCII.GetString(content, 0, Math.Min(content.Length, 256)));
			if (!match.Success)
				return null;

			var name = match.Groups[1].Value;
			if (name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf8", StringComparison.OrdinalIgnoreCase))
				return null;

			try
			{
				return Encoding.GetEncoding(name);
			}
			catch (ArgumentException)
			{
				return null;
			}
		}
	}
}
