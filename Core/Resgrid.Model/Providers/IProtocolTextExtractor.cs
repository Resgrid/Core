using System.Threading;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Reads the text of a protocol file (P1 R-P5) page by page: PDF through PdfPig (Apache-2.0; never an AGPL or commercial PDF
	/// library, §22.2) and DOCX through DocumentFormat.OpenXml. Bounded: <see cref="ProtocolDocumentRules.MaxPages"/> pages and
	/// <see cref="ProtocolDocumentRules.MaxChars"/> characters, and the caller's token cuts a slow file short. Encrypted and scanned
	/// PDFs come back as NoText with the reason; it never throws for a bad file.
	/// </summary>
	public interface IProtocolTextExtractor
	{
		ProtocolTextExtractionResult Extract(string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default);
	}
}
