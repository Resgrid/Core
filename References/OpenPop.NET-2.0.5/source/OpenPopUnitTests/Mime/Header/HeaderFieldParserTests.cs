using System;
using System.Net.Mime;
using NUnit.Framework;
using OpenPop.Mime.Header;

namespace OpenPopUnitTests.Mime.Header
{
	[TestFixture]
	public class HeaderFieldParserTests
	{
		#region Content-Disposition tests
		[Test]
		public static void ContentDispositionSimpleAttachment()
		{
			const string contentDispositionString = "attachment";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.IsFalse(contentDisposition.Inline);
			Assert.AreEqual("attachment", contentDisposition.DispositionType);
		}

		[Test]
		public static void ContentDispositionSimpleInline()
		{
			const string contentDispositionString = "inline";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.IsTrue(contentDisposition.Inline);
			Assert.AreEqual("inline", contentDisposition.DispositionType);
		}

		/// <summary>
		/// Tests that we are able to parse a Content-Disposition header
		/// </summary>
		[Test]
		public void ParseContentDispositionFull()
		{
			const string contentDispositionValue =
				"attachment; " +
				"filename=genome.jpeg; " +
				"modification-date=\"Wed, 12 February 1997 16:29:51 -0500\"; " +
				"creation-date=\"Tue, 11 February 1997 15:29:51 -0400\"; " +
				"read-date=\"Tue, 11 February 1997 16:29:52 -0500\"; " +
				"size=509;";

			ContentDisposition contentDisposition = null;
			Assert.DoesNotThrow(delegate { contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue); });
			Assert.NotNull(contentDisposition);

			// It is an attachment, not inline
			Assert.IsFalse(contentDisposition.Inline);
			Assert.AreEqual("attachment", contentDisposition.DispositionType);

			Assert.AreEqual("genome.jpeg", contentDisposition.FileName);

			// -0500 is the same as adding 5 hours in UTC
			Assert.AreEqual(new DateTime(1997, 2, 12, 21, 29, 51, DateTimeKind.Utc), contentDisposition.ModificationDate);

			// -0400 is the same as adding 4 hours in UTC
			Assert.AreEqual(new DateTime(1997, 2, 11, 19, 29, 51, DateTimeKind.Utc), contentDisposition.CreationDate);

			// -0500 is the same as adding 5 hours in UTC
			Assert.AreEqual(new DateTime(1997, 2, 11, 21, 29, 52, DateTimeKind.Utc), contentDisposition.ReadDate);

			Assert.AreEqual(509, contentDisposition.Size);
		}

		/// <summary>
		/// Tests that we are able to parse a Content-Disposition header
		/// </summary>
		[Test]
		public void ParseContentDispositionFull2()
		{
			const string contentDispositionValue =
				"inline; " +
				"filename=test.pdf; " +
				"modification-date=\"Tue, 16 November 2010 11:16:51 +0100\"; " +
				"creation-date=\"Tue, 16 November 2010 11:16:52 +0100\"; " +
				"read-date=\"Tue, 16 November 2010 11:16:53 +0100\"; " +
				"size=104953;";

			ContentDisposition contentDisposition = null;
			Assert.DoesNotThrow(delegate { contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue); });
			Assert.NotNull(contentDisposition);

			// It is an attachment, not inline
			Assert.IsTrue(contentDisposition.Inline);
			Assert.AreEqual("inline", contentDisposition.DispositionType);

			Assert.AreEqual("test.pdf", contentDisposition.FileName);

			// +0100 is the same as substractinbg 1 hour in UTC
			Assert.AreEqual(new DateTime(2010, 11, 16, 10, 16, 51, DateTimeKind.Utc), contentDisposition.ModificationDate);

			// -0400 is the same as adding 4 hours in UTC
			Assert.AreEqual(new DateTime(2010, 11, 16, 10, 16, 52, DateTimeKind.Utc), contentDisposition.CreationDate);

			// -0500 is the same as adding 5 hours in UTC
			Assert.AreEqual(new DateTime(2010, 11, 16, 10, 16, 53, DateTimeKind.Utc), contentDisposition.ReadDate);

			Assert.AreEqual(104953, contentDisposition.Size);
		}

		[Test]
		public void ParseContentDispositionFilenameWithEncodedWord()
		{
			const string contentDispositionString =
				"attachment; filename=\"=?utf-8?B?w5huc2tlIGluZGvDuGJzbGlzdGUuZG9j?=\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("Ønske indkøbsliste.doc", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionWithEncodedWordLong()
		{
			const string contentDispositionString =
				"attachment; filename=\"=?iso-8859-1?Q?Br=F8drenes_Jagtklub_-_Referat_af_generalforsamling.doc?=\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("Brødrenes Jagtklub - Referat af generalforsamling.doc", contentDisposition.FileName);
		}

		/// <summary>
		/// Checks that the quotes around the filename has been removed
		/// </summary>
		[Test]
		public void ParseContentDispositionWithFilenameInQuotes()
		{
			const string contentDispositionString =
				"attachment; filename=\"49139-msg.jpg\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("49139-msg.jpg", contentDisposition.FileName);
		}

		/// <summary>
		/// See http://tools.ietf.org/html/rfc2231 for the filename* definition.
		/// </summary>
		[Test]
		public void ParseContentDispositionFilenameWithEncoding()
		{
			const string contentDispositionString =
				"attachment;" +
				" filename*=ISO-8859-1\'\'%D8%6E%73%6B%65%6C%69%73%74%65%2E%70%64%66";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			// Tests that the ContentDisposition header correctly decoded the filename
			Assert.NotNull(contentDisposition.FileName);
			Assert.AreEqual("Ønskeliste.pdf", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionFilenameWithEncoding2()
		{
			const string contentDispositionString =
				"attachment; filename*=ISO-8859-1\'\'Ans%E6ttelseskontrakt.pdf";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("Ansættelseskontrakt.pdf", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionFilenameWithContinuation()
		{
			const string contentDispositionString =
				"attachment; filename*0=\"very long text document name is here to test if we can parse\"; filename*1=\"" +
				" continuation in the name in a header.txt\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("very long text document name is here to test if we can" +
			" parse continuation in the name in a header.txt", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionFilenameWithContinuationAndEncoding()
		{
			const string contentDispositionString =
				"attachment;" +
				" filename*0*=ISO-8859-1\'\'%76%65%72%79%20%6C%6F%6E%67%20%74%65%78%74%20%64;" +
				" filename*1*=%6F%63%75%6D%65%6E%74%20%6E%61%6D%65%20%69%73%20%68%65%72%65;" +
				" filename*2*=%20%74%6F%20%74%65%73%74%20%69%66%20%77%65%20%63%61%6E%20%70;" +
				" filename*3*=%61%72%73%65%20%63%6F%6E%74%69%6E%75%61%74%69%6F%6E%20%69%6E;" +
				" filename*4*=%20%74%68%65%20%6E%61%6D%65%20%69%6E%20%61%20%68%65%61%64%65;" +
				" filename*5*=%72%20%6E%6F%77%20%77%69%74%68%20%C6%D8%C5%2E%74%78%74";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("very long text document name is here to test if we can parse" +
			" continuation in the name in a header now with ÆØÅ.txt", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionFilenameWithQuotes()
		{
			const string contentDispositionString =
				"attachment;\r\n" +
				"\tfilename*=\"utf-8\'\'foobar.jpg\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("foobar.jpg", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionFilenameLongWithQuotes()
		{
			const string contentDispositionString =
				"attachment;" +
				" filename*0*=ISO-8859-1\'\'%76%65%72%79%20%6C%6F%6E%67%20%74%65%78%74%20%64;" +
				" filename*1*=\"%6F%63%75%6D%65%6E%74%20%6E%61%6D%65%20%69%73%20%68%65%72%65\";" +
				" filename*2*=%20%74%6F%20%74%65%73%74%20%69%66%20%77%65%20%63%61%6E%20%70;" +
				" filename*3*=%61%72%73%65%20%63%6F%6E%74%69%6E%75%61%74%69%6F%6E%20%69%6E;" +
				" filename*4*=\"%20%74%68%65%20%6E%61%6D%65%20%69%6E%20%61%20%68%65%61%64%65\";" +
				" filename*5*=%72%20%6E%6F%77%20%77%69%74%68%20%C6%D8%C5%2E%74%78%74";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("very long text document name is here to test if we can parse" +
			" continuation in the name in a header now with ÆØÅ.txt", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionFilenameWithBackwardsSupport()
		{
			const string contentDispositionString =
				"attachment;\r\n" +
				"\tfilename=\"=?utf-8?b?dHJhbnNmwqouanBn?=\";\r\n" +
				"\tfilename*=\"utf-8\'\'transf%C2%AA.jpg\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("transfª.jpg", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispositionSizeInQuotes()
		{
			const string contentDispositionString =
				"attachment;" +
				" size=\"104710\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual(104710, contentDisposition.Size);
		}

		[Test]
		public void ParseSizeInKilobyte()
		{
			const string contentDispositionValue =
				"inline; " +
				"size=104 kB;";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue);

			// 104 kilobytes = 106 496 bytes
			Assert.AreEqual(106496, contentDisposition.Size);
		}

		[Test]
		public void ParseSizeInMegaBytes()
		{
			const string contentDispositionValue =
				"inline; " +
				"size=2 mB;";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue);

			// 2 megabytes = 2 097 152 bytes
			Assert.AreEqual(2097152, contentDisposition.Size);
		}

		[Test]
		public void ParseSizeInGigaBytes()
		{
			const string contentDispositionValue =
				"inline; " +
				"size=2 GB;";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue);

			// 2 gigabytes = 2 147 483 648 bytes
			Assert.AreEqual(2147483648, contentDisposition.Size);
		}

		[Test]
		public void ParseSizeInTeraBytes()
		{
			const string contentDispositionValue =
				"inline; " +
				"size=3 TB;";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue);

			// 3 terabytes = 3 298 534 883 328 bytes
			Assert.AreEqual(3298534883328, contentDisposition.Size);
		}

		[Test]
		public void ParseSizeInKiloBytesWithSpacesAdnQuotes()
		{
			const string contentDispositionValue =
				"inline; " +
				"size=\"  1   kb    \";";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionValue);

			// 1 kilobyte = 1 024 bytes
			Assert.AreEqual(1024, contentDisposition.Size);
		}

		[Test]
		public void ParseContentDispositionFilenameLargeFirstCharacter()
		{
			const string contentDispositionString =
				"attachment;" +
				" Filename=\"test.csv\";";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("test.csv", contentDisposition.FileName);
		}

		[Test]
		public void ParseContentDispoistionWithNameParameter()
		{
			const string contentDispositionString =
				"attachment; name=\"010294-0011841.pdf\"; filename=\"010294-0011841.pdf\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("010294-0011841.pdf", contentDisposition.FileName);
			Assert.IsFalse(contentDisposition.Inline);
		}

		[Test]
		public void ParseContentDispositionWithNoSpaceAfterContinuation()
		{
			// Notice there is no space before filename*1
			const string contentDispositionString =
				"attachment; filename*0=\"KB - Examen BedrijfskundigFinancieel Management - 11980 - uitwe\";filename*1=\"rkingen.doc\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("KB - Examen BedrijfskundigFinancieel Management - 11980 - uitwerkingen.doc", contentDisposition.FileName);
			Assert.IsFalse(contentDisposition.Inline);
		}

		[Test]
		public void ParseContentDispositionWithXTokens()
		{
			// x-extensions should be ignored
			const string contentDispositionString =
				"attachment; x-foo=true";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("true", contentDisposition.Parameters["X-FOO"]);
			Assert.IsFalse(contentDisposition.Inline);
		}

		[Test]
		public void ParseContentDispositionWithXTokens2()
		{
			// x-extensions should be ignored
			const string contentDispositionString =
				"attachment; x-maiconizer-user=yes";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("yes", contentDisposition.Parameters["X-MAICONIZER-USER"]);
			Assert.AreEqual("yes", contentDisposition.Parameters["x-maiconizer-user"]); // Both upper and lower case keys should work
			Assert.IsFalse(contentDisposition.Inline);
		}

		[Test]
		public void ParseContentDispositionWithXTokensQuotesShouldBeRemoved()
		{
			// x-extensions should be ignored
			const string contentDispositionString =
				"attachment; x-some-special-parameter-with-quotes=\"foobar\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("foobar", contentDisposition.Parameters["x-some-special-parameter-with-quotes"]);
			Assert.AreNotEqual("\"foobar\"", contentDisposition.Parameters["x-some-special-parameter-with-quotes"]);
			Assert.IsFalse(contentDisposition.Inline);
		}

		[Test]
		public void TestMissingSemicolonAfterAttachment()
		{
			// Notice that there should have been a semicolon imidiatly after "attachment"
			const string contentDispositionString =
				"attachment filename=\"Green-Eco-Products.jpg\"";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.AreEqual("Green-Eco-Products.jpg", contentDisposition.Parameters["filename"]);
			Assert.IsFalse(contentDisposition.Inline);
		}

		[Test]
		public void TestMissingLanguageCodeInEncodedFilename()
		{
			// Notice that this header is supposed to be a continuation header, but it has been wrongly understood and
			// therefore someone has specified it is a encoding header, but there is no encoding in the string.
			// Check that this can be decoded correctly anyway
			const string contentDispositionString =
				"attachment;\r\n filename*=\"=?UTF-8?Q?This_is_an_attachment_with_long?= =?UTF-8?Q?_name_and_chinese_character_=E4=B8=AD=E6=96=87=2Etxt?=\";\r\n size=14";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.IsFalse(contentDisposition.Inline);
			Assert.AreEqual(14, contentDisposition.Size);
			Assert.AreEqual("This is an attachment with long name and chinese character 中文.txt", contentDisposition.Parameters["filename"]);
		}

		[Test]
		public void TestMissingLanguageCodeInEncodedFilenameWithContinuation()
		{
			// Notice that this header is supposed to be a continuation header, but it has been wrongly understood and
			// therefore someone has specified it is a encoding header, but there is no encoding in the string.
			// Check that this can be decoded correctly anyway
			const string contentDispositionString =
				"attachment;\r\n filename*0*=\"=?UTF-8?Q?This_is_an_attachment_with_long?=\" filename*1*=\"=?UTF-8?Q?_name_and_chinese_character_=E4=B8=AD=E6=96=87=2Etxt?=\";\r\n size=14";

			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.IsFalse(contentDisposition.Inline);
			Assert.AreEqual(14, contentDisposition.Size);
			Assert.AreEqual("This is an attachment with long name and chinese character 中文.txt", contentDisposition.Parameters["filename"]);
		}

		[Test]
		public void TestNoQuotesInAttachmentFilenameWithLinebreakAndTab()
		{
			const string contentDispositionString = "attachment; filename=\r\n\t\"a.txt\"";
			ContentDisposition contentDisposition = HeaderFieldParser.ParseContentDisposition(contentDispositionString);

			Assert.IsFalse(contentDisposition.Inline);
			Assert.AreEqual("a.txt", contentDisposition.Parameters["filename"]);
		}
		#endregion

		#region Content-Type tests
		/// <summary>
		/// Checks that the quotes have been removed
		/// </summary>
		[Test]
		public void TestContentTypeFilenameInQoutes()
		{
			const string contentTypeString =
				"image/gif;name=\"gradient.gif\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("gradient.gif", contentType.Name);
			Assert.AreEqual("image/gif", contentType.MediaType);
		}

		/// <summary>
		/// Checks that the quotes have been removed
		/// </summary>
		[Test]
		public void TestContentTypeFilenameInQoutes2()
		{
			const string contentTypeString =
				"image/jpeg;name=\"Rejsebazar_Kbh(2).jpg\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("Rejsebazar_Kbh(2).jpg", contentType.Name);
			Assert.AreEqual("image/jpeg", contentType.MediaType);
		}

		/// <summary>
		/// Test that the space between ...gif; and name=\" does not destroy parsing
		/// </summary>
		[Test]
		public void TestContentTypeWithSpace()
		{
			const string contentTypeString =
				"image/gif; name=\"aleabanr.gif\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("aleabanr.gif", contentType.Name);
			Assert.AreEqual("image/gif", contentType.MediaType);
		}

		[Test]
		public void TestContentTypeLongMediaType()
		{
			const string contentTypeString =
					"application/vnd.openxmlformats-officedocument.wordprocessingml.document; name=\"Hej.docx\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("application/vnd.openxmlformats-officedocument.wordprocessingml.document", contentType.MediaType);
			Assert.AreEqual("Hej.docx", contentType.Name);
		}

		[Test]
		public void TestContentTypeNameWithoutQuotes()
		{
			const string contentTypeString =
				"application/vnd.oasis.opendocument.text; name=Til";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("application/vnd.oasis.opendocument.text", contentType.MediaType);
			Assert.AreEqual("Til", contentType.Name);
		}

		[Test]
		public void TestContentTypeWithLongName()
		{
			const string contentTypeString =
				"text/plain;" +
				" name=\"very long text document name is here to test if we can parse continuation" +
				" in the name in a header.txt\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("very long text document name is here to test if we can parse" +
			" continuation in the name in a header.txt", contentType.Name);
		}

		[Test]
		public void TestContentTypeBoundary()
		{
			const string contentTypeString =
				"multipart/mixed; boundary=\"_004_76C5825B768EE04E99BD2EAC9C43507557EDD335B3server1hqcbbe_\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("multipart/mixed", contentType.MediaType);
			Assert.AreEqual("_004_76C5825B768EE04E99BD2EAC9C43507557EDD335B3server1hqcbbe_", contentType.Boundary);
		}

		[Test]
		public void TestContentTypeCharacterSet()
		{
			const string contentTypeString =
				"text/html; charset=\"iso-8859-1\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("text/html", contentType.MediaType);
			Assert.AreEqual("iso-8859-1", contentType.CharSet);
		}

		/// <summary>
		/// See http://tools.ietf.org/html/rfc2231 for the continuation of header fields definition.
		/// </summary>
		[Test]
		public void ParseMultiPartBoundaryWithContinuation()
		{
			const string contentTypeString =
				"multipart/report; report-type=delivery-status;" +
				" boundary*0=1804289383_1288411300_549365113_21474836;" +
				" boundary*1=47_bda2385.bisx.prod.on.blackberry";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.NotNull(contentType.Boundary);
			Assert.AreEqual("1804289383_1288411300_549365113_2147483647_bda2385.bisx.prod.on.blackberry", contentType.Boundary);
		}

		/// <summary>
		/// Tests that the content type parser can decode encoded-words
		/// </summary>
		[Test]
		public void TestContentTypeWithEncodedWordInName()
		{
			const string contentTypeString =
				"application/msword; name=\"=?Windows-1252?Q?revideret_forel=F8big_dagsorden_090110_version_2.doc?=\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("revideret foreløbig dagsorden 090110 version 2.doc", contentType.Name);
			Assert.AreEqual("application/msword", contentType.MediaType);
		}

		[Test]
		public void TestContentTypeWithNameEncoding()
		{
			const string contentTypeString =
				"application/pdf; name*=ISO-8859-1\'\'Ans%E6ttelseskontrakt.pdf";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("Ansættelseskontrakt.pdf", contentType.Name);
			Assert.AreEqual("application/pdf", contentType.MediaType);
		}

		[Test]
		public void TestContentTypeWithLongNameAndUsingEncodedWord()
		{
			const string contentTypeString =
				"text/plain;" +
				" name=\"=?ISO-8859-1?Q?very_long_text_document_name_is_here_to_te?=" +
				"=?ISO-8859-1?Q?st_if_we_can_parse_continuation_in_the_na?=" +
				"=?ISO-8859-1?Q?me_in_a_header_now_with_=C6=D8=C5=2Etxt?=\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("very long text document name is here to test if we can parse" +
			" continuation in the name in a header now with ÆØÅ.txt", contentType.Name);
		}

		/// <summary>
		/// This is a test case from RFC2231
		/// </summary>
		[Test]
		public void TestContentTypeWithContinuationAndEncoding()
		{
			const string contentTypeString =
				"application/x-stuff;" +
				" title*0*=us-ascii\'en\'This%20is%20even%20more%20;" +
				" title*1*=%2A%2A%2Afun%2A%2A%2A%20;" +
				" title*2=\"isn\'t it!\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("This is even more ***fun*** isn\'t it!", contentType.Parameters["title"]);
		}

		[Test]
		public void TestContentTypeWithSpaceAtCharacterSet()
		{
			const string contentTypeString =
				"text/plain; charset = \"us-ascii\"";

			ContentType contentType = null;

			Assert.DoesNotThrow(delegate { contentType = HeaderFieldParser.ParseContentType(contentTypeString); });

			Assert.AreEqual("us-ascii", contentType.CharSet);
		}

		/// <summary>
		/// Test with extra semicolon at the end, which is not needed
		/// </summary>
		[Test]
		public void TestContentTypeWithExcessEndingSemicolon()
		{
			const string contentTypeString = 
				"application/vnd.ms-excel;name=\"MMSCecpmcountry12_06_2010.xls\";";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("application/vnd.ms-excel", contentType.MediaType);
			Assert.AreEqual("MMSCecpmcountry12_06_2010.xls", contentType.Name);
		}

		/// <summary>
		/// Test with extra semicolon and whitespace at the end, which is not needed
		/// </summary>
		[Test]
		public void TestContentTypeWithExcessEndingSemicolonAndWhitespace()
		{
			const string contentTypeString =
				"application/vnd.ms-excel;name=\"MMSCecpmcountry12_06_2010.xls\"; ";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("application/vnd.ms-excel", contentType.MediaType);
			Assert.AreEqual("MMSCecpmcountry12_06_2010.xls", contentType.Name);
		}

		[Test]
		public void TestContentTypeWithFilenameIncludingSemicolon()
		{
			const string contentTypeString =
				"application/msword; name=\"NUMMER; 251478.doc\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("application/msword", contentType.MediaType);
			Assert.AreEqual("NUMMER; 251478.doc", contentType.Name);
		}

		[Test]
		public void TestContentTypeWithMissingSemicolon()
		{
			// Notice there is no semicolon after charset="iso-8859-1"
			const string contentTypeString =
				"text/plain; charset=\"iso-8859-1\" name=\"somefile.txt\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("text/plain", contentType.MediaType);
			Assert.AreEqual("iso-8859-1", contentType.CharSet);
			Assert.AreEqual("somefile.txt", contentType.Name);
		}

		[Test]
		public void TestContentTypeWithMissingSemicolonAndExcesiveWhitespace()
		{
			// Notice there is no semicolon after charset="iso-8859-1"
			const string contentTypeString =
				"text/plain; charset   =   \"iso-8859-1\" name   =   \"somefile.txt\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);

			Assert.AreEqual("text/plain", contentType.MediaType);
			Assert.AreEqual("iso-8859-1", contentType.CharSet);
			Assert.AreEqual("somefile.txt", contentType.Name);
		}

		[Test]
		public void TestContentTypeWithSpaceAndSemicolon()
		{
			const string contentTypeString =
				"text/plain; charset=\"us-ascii\" ; name=\"foo\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);
			Assert.AreEqual("text/plain", contentType.MediaType);
			Assert.AreEqual("us-ascii", contentType.CharSet);
			Assert.AreEqual("foo", contentType.Name);
		}

		[Test]
		public void TestContentTypeWithFormat()
		{
			const string contentTypeString =
				"text/plain; charset=\"us-ascii\"; format=\"flowed\"";

			ContentType contentType = HeaderFieldParser.ParseContentType(contentTypeString);
			Assert.AreEqual("text/plain", contentType.MediaType);
			Assert.AreEqual("us-ascii", contentType.CharSet);
			Assert.AreEqual("flowed", contentType.Parameters["format"]);
		}

		[Test]
		public void TestIllegalContentTypeMediaType()
		{
			const string contentTypeString =
				"text; charset=\"us-ascii\";";

			ContentType contentType = null;
			Assert.DoesNotThrow(delegate { contentType = HeaderFieldParser.ParseContentType(contentTypeString); });
			Assert.NotNull(contentType);
			Assert.AreEqual("text/plain", contentType.MediaType);
			Assert.AreEqual("us-ascii", contentType.CharSet);
		}
		#endregion

		#region Content-Transfer-Encoding tests
		[Test]
		public void TestContentTransferEncoding8Bit()
		{
			const string contentTransferEncodingString =
				"8bit";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(contentTransferEncodingString);

			Assert.AreEqual(ContentTransferEncoding.EightBit, encoding);
		}

		[Test]
		public void TestContentTransferEncodingBase64()
		{
			const string contentTransferEncodingString =
				"base64";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(contentTransferEncodingString);

			Assert.AreEqual(ContentTransferEncoding.Base64, encoding);
		}

		[Test]
		public void TestContentTransferEncodingQuotedPrintable()
		{
			const string contentTransferEncodingString =
				"quoted-printable";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(contentTransferEncodingString);

			Assert.AreEqual(ContentTransferEncoding.QuotedPrintable, encoding);
		}

		[Test]
		public void TestContentTransferEncoding7Bit()
		{
			const string contentTransferEncodingString =
				"7bit";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(contentTransferEncodingString);

			Assert.AreEqual(ContentTransferEncoding.SevenBit, encoding);
		}

		[Test]
		public void TestContentTransferEncodingBinary()
		{
			const string contentTransferEncodingString =
				"binary";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(contentTransferEncodingString);

			Assert.AreEqual(ContentTransferEncoding.Binary, encoding);
		}

		/// <summary>
		/// RFC states that the field values are case insensitive
		/// </summary>
		[Test]
		public void TestContentTransferEncodingQoutedPrintableFunnyCase()
		{
			const string contentTransferEncodingString =
				"quOTed-pRinTabLE";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(contentTransferEncodingString);

			Assert.AreEqual(ContentTransferEncoding.QuotedPrintable, encoding);
		}

		[Test]
		public void TestInvalidContentTransferEncoding()
		{
			const string wrongContentTransferEncoding = "ISO-8859-1";

			ContentTransferEncoding encoding = HeaderFieldParser.ParseContentTransferEncoding(wrongContentTransferEncoding);

			Assert.NotNull(encoding);

			// We want the implementation to return the default encoding instead of the wrongly specified one
			const ContentTransferEncoding defaultEncoding = ContentTransferEncoding.SevenBit;
			Assert.AreEqual(defaultEncoding, encoding);
		}
		#endregion
	}
}