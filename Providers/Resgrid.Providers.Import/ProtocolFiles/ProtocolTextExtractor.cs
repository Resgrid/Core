using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Resgrid.Model;
using Resgrid.Model.Providers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace Resgrid.Providers.Import.ProtocolFiles
{
	/// <summary>
	/// Protocol file text (P1 R-P5): PDF through PdfPig (Apache-2.0), DOCX through DocumentFormat.OpenXml, one string per page,
	/// bounded by <see cref="ProtocolDocumentRules.MaxPages"/> and <see cref="ProtocolDocumentRules.MaxChars"/>. Encrypted PDFs and
	/// PDFs without a text layer (scanned) come back as NoText with the reason; a malformed file is NoText "unreadable". The token
	/// stops a slow file between pages (Failed "timeout": the caller queues or gives up). Never throws for a bad file.
	/// </summary>
	public class ProtocolTextExtractor : IProtocolTextExtractor
	{
		public ProtocolTextExtractionResult Extract(string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			if (data == null || data.Length == 0)
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Unreadable);

			if (data.LongLength > ProtocolDocumentRules.MaxFileBytes)
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.TooLarge);

			var detected = ProtocolDocumentRules.DetectContentType(fileName, data) ?? contentType;
			try
			{
				if (string.Equals(detected, ProtocolDocumentRules.PdfContentType, StringComparison.OrdinalIgnoreCase))
					return ExtractPdf(data, cancellationToken);
				if (string.Equals(detected, ProtocolDocumentRules.DocxContentType, StringComparison.OrdinalIgnoreCase))
					return ExtractDocx(data, cancellationToken);
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Unsupported);
			}
			catch (OperationCanceledException)
			{
				return ProtocolTextExtractionResult.Failed(ProtocolFileNoTextReasons.Timeout);
			}
			catch (PdfDocumentEncryptedException)
			{
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Encrypted);
			}
			catch (Exception)
			{
				// A malformed file is not an error worth a stack trace per upload: it is a file with no text.
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Unreadable);
			}
		}

		private static ProtocolTextExtractionResult ExtractPdf(byte[] data, CancellationToken cancellationToken)
		{
			using var document = PdfDocument.Open(data, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
			// A PDF that needed (even an empty) password is treated as encrypted: its owner restricted it, and its text is not ours to index.
			if (document.IsEncrypted)
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Encrypted);

			var result = new ProtocolTextExtractionResult { Status = ProtocolFileTextStatuses.Extracted };
			var remaining = ProtocolDocumentRules.MaxChars;
			var pages = Math.Min(document.NumberOfPages, ProtocolDocumentRules.MaxPages);
			for (var number = 1; number <= pages; number++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				string text;
				try
				{
					text = ContentOrderTextExtractor.GetText(document.GetPage(number));
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// One unreadable page keeps its place (an empty page) so the page numbers stay true.
					text = string.Empty;
				}

				var clean = ProtocolDocumentRules.CleanPageText(text, Math.Max(0, remaining));
				remaining -= clean.Length;
				result.Pages.Add(clean);
				if (remaining <= 0)
					break;
			}

			if (ProtocolDocumentRules.CountLetters(result.Pages) < ProtocolDocumentRules.ScannedLetterThreshold)
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Scanned);

			return result;
		}

		private static ProtocolTextExtractionResult ExtractDocx(byte[] data, CancellationToken cancellationToken)
		{
			using var stream = new MemoryStream(data, false);
			using var document = WordprocessingDocument.Open(stream, false);
			var body = document.MainDocumentPart?.Document?.Body;
			if (body == null)
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Unreadable);

			// Word stores no pages; the breaks Word last rendered are the truest page boundaries, else the explicit page breaks.
			var rendered = body.Descendants<LastRenderedPageBreak>().Any();
			var pages = new List<StringBuilder> { new StringBuilder() };
			var chars = 0;

			void NewPage()
			{
				if (pages.Count < ProtocolDocumentRules.MaxPages)
					pages.Add(new StringBuilder());
			}

			foreach (var paragraph in body.Descendants<Paragraph>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (chars >= ProtocolDocumentRules.MaxChars)
					break;

				if (!rendered && paragraph.ParagraphProperties?.PageBreakBefore != null && pages[pages.Count - 1].Length > 0)
					NewPage();

				foreach (var element in paragraph.Descendants())
				{
					switch (element)
					{
						case LastRenderedPageBreak _ when rendered:
							if (pages[pages.Count - 1].Length > 0) NewPage();
							break;
						case Break br when !rendered && br.Type != null && br.Type.Value == BreakValues.Page:
							NewPage();
							break;
						case WordText text:
							pages[pages.Count - 1].Append(text.Text);
							chars += text.Text?.Length ?? 0;
							break;
						case TabChar _:
							pages[pages.Count - 1].Append(' ');
							break;
					}
				}

				pages[pages.Count - 1].Append('\n');
			}

			var result = new ProtocolTextExtractionResult { Status = ProtocolFileTextStatuses.Extracted };
			var remaining = ProtocolDocumentRules.MaxChars;
			foreach (var page in pages)
			{
				var clean = ProtocolDocumentRules.CleanPageText(page.ToString(), Math.Max(0, remaining));
				remaining -= clean.Length;
				result.Pages.Add(clean);
			}

			// Drop a trailing empty page a final break left behind.
			while (result.Pages.Count > 1 && string.IsNullOrWhiteSpace(result.Pages[result.Pages.Count - 1]))
				result.Pages.RemoveAt(result.Pages.Count - 1);

			if (ProtocolDocumentRules.CountLetters(result.Pages) == 0)
				return ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Scanned);

			return result;
		}
	}
}
