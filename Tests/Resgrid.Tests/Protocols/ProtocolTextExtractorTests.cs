using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Import.ProtocolFiles;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocol file text (P1 R-P5): PDF through PdfPig and DOCX through OpenXml, one string per page, and the "no text" fallbacks:
	/// an encrypted PDF, a scanned PDF (no text layer), a malformed file, an unsupported type. The extractor never throws for a bad
	/// file, and a cancelled extraction comes back as a timeout the caller queues.
	/// </summary>
	[TestFixture]
	public class ProtocolTextExtractorTests
	{
		private readonly ProtocolTextExtractor _extractor = new ProtocolTextExtractor();

		/// <summary>A real PDF with one line of text per page (PdfPig's writer).</summary>
		public static byte[] Pdf(params string[] pages)
		{
			var builder = new PdfDocumentBuilder();
			var font = builder.AddStandard14Font(Standard14Font.Helvetica);
			foreach (var text in pages)
			{
				var page = builder.AddPage(612, 792);
				if (!string.IsNullOrEmpty(text))
					page.AddText(text, 12, new PdfPoint(72, 720), font);
			}
			return builder.Build();
		}

		/// <summary>A real DOCX: the paragraphs, with an explicit page break before each item that starts with "|".</summary>
		public static byte[] Docx(params string[] paragraphs)
		{
			using var stream = new MemoryStream();
			using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
			{
				var main = document.AddMainDocumentPart();
				var body = new Body();
				foreach (var text in paragraphs)
				{
					var paragraph = new Paragraph();
					if (text.StartsWith("|", StringComparison.Ordinal))
						paragraph.Append(new Run(new Break { Type = BreakValues.Page }));
					paragraph.Append(new Run(new Text(text.TrimStart('|'))));
					body.Append(paragraph);
				}
				main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
				main.Document.Save();
			}
			return stream.ToArray();
		}

		[Test]
		public void A_pdf_gives_its_text_page_by_page()
		{
			var result = _extractor.Extract("SOG-204 Mayday.pdf", ProtocolDocumentRules.PdfContentType, Pdf("Mayday procedure first page", "Rapid intervention second page"));

			result.Status.Should().Be(ProtocolFileTextStatuses.Extracted);
			result.Pages.Should().HaveCount(2);
			result.Pages[0].Should().Contain("Mayday procedure first page");
			result.Pages[1].Should().Contain("Rapid intervention second page");
			result.CharCount.Should().BeGreaterThan(40);
		}

		[Test]
		public void A_docx_gives_its_text_split_at_its_page_breaks()
		{
			var result = _extractor.Extract("Policy.docx", ProtocolDocumentRules.DocxContentType, Docx("Purpose of the policy", "Scope applies to all members", "|Second page procedure"));

			result.Status.Should().Be(ProtocolFileTextStatuses.Extracted);
			result.Pages.Should().HaveCount(2, "the explicit page break starts page 2");
			result.Pages[0].Should().Contain("Purpose of the policy").And.Contain("Scope applies to all members");
			result.Pages[1].Should().Contain("Second page procedure");
		}

		[Test]
		public void A_pdf_without_a_text_layer_is_no_text_scanned()
		{
			var result = _extractor.Extract("scan.pdf", ProtocolDocumentRules.PdfContentType, Pdf("", ""));

			result.Status.Should().Be(ProtocolFileTextStatuses.NoText);
			result.Reason.Should().Be(ProtocolFileNoTextReasons.Scanned);
			result.Pages.Should().BeEmpty();
		}

		[Test]
		public void An_encrypted_pdf_is_no_text_encrypted_and_never_throws()
		{
			var result = _extractor.Extract("locked.pdf", ProtocolDocumentRules.PdfContentType, EncryptedPdf());

			result.Status.Should().Be(ProtocolFileTextStatuses.NoText);
			result.Reason.Should().BeOneOf(ProtocolFileNoTextReasons.Encrypted, ProtocolFileNoTextReasons.Unreadable);
			result.Pages.Should().BeEmpty();
		}

		[Test]
		public void A_malformed_pdf_is_no_text_unreadable()
		{
			var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nthis is not really a pdf at all");
			var result = _extractor.Extract("broken.pdf", ProtocolDocumentRules.PdfContentType, bytes);

			result.Status.Should().Be(ProtocolFileTextStatuses.NoText);
			result.Reason.Should().BeOneOf(ProtocolFileNoTextReasons.Unreadable, ProtocolFileNoTextReasons.Scanned);
		}

		[Test]
		public void A_file_that_is_neither_pdf_nor_docx_is_unsupported()
		{
			var result = _extractor.Extract("notes.txt", "text/plain", Encoding.UTF8.GetBytes("plain text notes"));

			result.Status.Should().Be(ProtocolFileTextStatuses.NoText);
			result.Reason.Should().Be(ProtocolFileNoTextReasons.Unsupported);
		}

		[Test]
		public void A_cancelled_extraction_is_a_timeout_the_caller_queues()
		{
			using var cancelled = new CancellationTokenSource();
			cancelled.Cancel();

			var result = _extractor.Extract("long.pdf", ProtocolDocumentRules.PdfContentType, Pdf("One", "Two"), cancelled.Token);

			result.Status.Should().Be(ProtocolFileTextStatuses.Failed);
			result.Reason.Should().Be(ProtocolFileNoTextReasons.Timeout);
		}

		[Test]
		public void Content_type_comes_from_the_bytes_not_the_claimed_type()
		{
			ProtocolDocumentRules.DetectContentType("a.pdf", Pdf("x y z")).Should().Be(ProtocolDocumentRules.PdfContentType);
			ProtocolDocumentRules.DetectContentType("a.docx", Docx("x")).Should().Be(ProtocolDocumentRules.DocxContentType);
			ProtocolDocumentRules.DetectContentType("a.docx", Pdf("x")).Should().BeNull("a PDF named .docx is neither");
			ProtocolDocumentRules.DetectContentType("a.pdf", Encoding.ASCII.GetBytes("<html>")).Should().BeNull();
			ProtocolDocumentRules.DetectContentType("a.exe", Docx("x")).Should().BeNull("a zip that is not named .docx is not a protocol file");
		}

		[TestCase("SOG-204 Mayday.pdf", "SOG-204", "Mayday")]
		[TestCase("SOP_12 Hose testing.docx", "SOP-12", "Hose testing")]
		[TestCase("P 3.1 - Water supply.pdf", "P-3.1", "Water supply")]
		[TestCase("Uniform policy.pdf", null, "Uniform policy")]
		public void A_file_name_supplies_the_number_and_the_title(string fileName, string number, string title)
		{
			var (parsedNumber, parsedTitle) = ProtocolDocumentRules.ParseFileName(fileName);
			parsedNumber.Should().Be(number);
			parsedTitle.Should().Be(title);
		}

		[Test]
		public void The_document_hash_is_the_sha256_of_the_file_bytes()
		{
			var bytes = Pdf("Hash me");
			var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
			ProtocolDocumentRules.HashFile(bytes).Should().Be(expected);
			ProtocolDocumentRules.HashFile(bytes.Concat(new byte[] { 0 }).ToArray()).Should().NotBe(expected);
		}

		/// <summary>
		/// A minimal PDF whose trailer names a standard security handler (revision 3) with values no empty user password opens: PdfPig
		/// cannot read it, and the extractor reports it as encrypted (or unreadable), never as text and never by throwing.
		/// </summary>
		private static byte[] EncryptedPdf()
		{
			var o = new string('A', 64);
			var u = new string('B', 64);
			var body = new StringBuilder();
			body.Append("%PDF-1.4\n");
			var offsets = new int[5];
			string[] objects =
			{
				"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
				"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
				"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n",
				"4 0 obj\n<< /Length 8 >>\nstream\nABCDEFGH\nendstream\nendobj\n",
				"5 0 obj\n<< /Filter /Standard /V 2 /R 3 /Length 128 /P -44 /O <" + o + "> /U <" + u + "> >>\nendobj\n"
			};
			for (var i = 0; i < objects.Length; i++)
			{
				offsets[i] = body.Length;
				body.Append(objects[i]);
			}
			var xref = body.Length;
			body.Append("xref\n0 6\n0000000000 65535 f \n");
			foreach (var offset in offsets)
				body.Append(offset.ToString("D10")).Append(" 00000 n \n");
			body.Append("trailer\n<< /Size 6 /Root 1 0 R /Encrypt 5 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] >>\n");
			body.Append("startxref\n").Append(xref).Append("\n%%EOF\n");
			return Encoding.ASCII.GetBytes(body.ToString());
		}
	}
}
