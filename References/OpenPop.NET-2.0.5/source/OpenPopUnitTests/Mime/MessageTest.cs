using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenPop.Mime;
using OpenPop.Mime.Header;

namespace OpenPopUnitTests.Mime
{
	/// <summary>
	/// Tests the <see cref="Message"/> class.
	/// </summary>
	[TestFixture]
	public class MessageTest
	{
		/// <summary>
		/// This is a test which uses Quoted-Printable encoding to see if
		/// the message content is stripped.
		/// Quoted-Printable needs the last \r\n to decode correctly therefore the
		/// test will fail if \r\ is stripped away.
		/// </summary>
		[Test]
		public void TestLineEndingsNotStrippedAwayAtEndUsingQuotedPrintable()
		{
			const string input =
				"Content-Type: text/plain; charset=iso-8859-1\r\n" +
				"Content-Transfer-Encoding: quoted-printable\r\n" +
				"\r\n" + // Headers end
				"Hello=\r\n"; // This is where the last \r\n should not be removed

			// The QP encoding would have decoded Hello=\r\n into Hello, since =\r\n is a soft line break
			const string expectedOutput = "Hello";

			string output = new Message(Encoding.ASCII.GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// Tests that \r\n in the start of the message body is not stripped away
		/// </summary>
		[Test]
		public void TestLineEndingsNotStrippedAwayAtStart()
		{
			const string input =
				"Content-Type: text/plain; charset=iso-8859-1\r\n" +
				"Content-Transfer-Encoding: 7bit\r\n" +
				"\r\n" + // Headers end
				"\r\nHello"; // This is where the first \r\n should not be removed

			const string expectedOutput = "\r\nHello";

			string output = new Message(Encoding.ASCII.GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// ISO-8859-9. See http://en.wikipedia.org/wiki/ISO/IEC_8859-9
		/// </summary>
		[Test]
		public void TestISO88599CharacterSet()
		{
			const string Iso88599SpecialChars =
				"ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏĞÑÒÓÔÕÖ×ØÙÚÛÜİŞßàáâãäåæçèéêëìíîïğñòóôõö÷øùúûüışÿ";

			const string input =
				"Content-Type: text/plain; charset=iso-8859-9\r\n" +
				"Content-Transfer-Encoding: 7bit\r\n" +
				"\r\n" + // Headers end
				Iso88599SpecialChars;

			const string expectedOutput = Iso88599SpecialChars;

			string output = new Message(Encoding.GetEncoding("ISO-8859-9").GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// Windows-1254. See http://en.wikipedia.org/wiki/Windows-1254
		/// </summary>
		[Test]
		public void TestWindows1254CharacterSet()
		{
			const string windows1254SpecialChars =
				"€‚ƒ„…†‡ˆ‰Š‹Œ‘’“”•–—˜™š›œŸ";

			// Windows 1254 is compatible with ISO-8859-9 in ranges [A0-FF].
			const string Iso88599SpecialChars =
				"ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏĞÑÒÓÔÕÖ×ØÙÚÛÜİŞßàáâãäåæçèéêëìíîïğñòóôõö÷øùúûüışÿ";

			const string specialChars = windows1254SpecialChars + Iso88599SpecialChars;

			const string input =
				"Content-Type: text/plain; charset=windows-1254\r\n" +
				"Content-Transfer-Encoding: 7bit\r\n" +
				"\r\n" + // Headers end
				specialChars;

			const string expectedOutput = specialChars; 

			string output = new Message(Encoding.GetEncoding(1254).GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}


		/// <summary>
		/// ISO-8859-1. See http://en.wikipedia.org/wiki/ISO/IEC_8859-1
		/// </summary>
		[Test]
		public void TestISO88591CharacterSet()
		{
			const string Iso88591SpecialChars =
				"ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ";

			const string input =
				"Content-Type: text/plain; charset=iso-8859-1\r\n" +
				"Content-Transfer-Encoding: 7bit\r\n" +
				"\r\n" + // Headers end
				Iso88591SpecialChars;

			const string expectedOutput = Iso88591SpecialChars;

			string output = new Message(Encoding.GetEncoding("ISO-8859-1").GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// Windows 1252. See http://en.wikipedia.org/wiki/Windows-1252
		/// </summary>
		[Test]
		public void TestWindows1252CharacterSet()
		{
			const string windows1252SpecialChars =
				"€‚ƒ„…†‡ˆ‰Š‹Œ‘’“”•–—˜™š›œŸ";

			// Windows 1254 is compatible with ISO-8859-1 in ranges [A0-FF].
			const string Iso88591SpecialChars =
				"ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ";

			const string specialChars = windows1252SpecialChars + Iso88591SpecialChars;

			const string input =
				"Content-Type: text/plain; charset=windows-1252\r\n" +
				"Content-Transfer-Encoding: 7bit\r\n" +
				"\r\n" + // Headers end
				specialChars;

			const string expectedOutput = specialChars;

			string output = new Message(Encoding.GetEncoding(1252).GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// Windows 1252. See http://en.wikipedia.org/wiki/Windows-1252
		/// CP is just another way to say Windows 1252
		/// </summary>
		[Test]
		public void TestCP1252CharacterSet()
		{
			const string windows1252SpecialChars =
				"€‚ƒ„…†‡ˆ‰Š‹Œ‘’“”•–—˜™š›œŸ";

			// Windows 1254 is compatible with ISO-8859-1 in ranges [A0-FF].
			const string Iso88591SpecialChars =
				"ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ";

			const string specialChars = windows1252SpecialChars + Iso88591SpecialChars;

			const string input =
				"Content-Type: text/plain; charset=Cp1252\r\n" +
				"Content-Transfer-Encoding: 7bit\r\n" +
				"\r\n" + // Headers end
				specialChars;

			const string expectedOutput = specialChars;

			string output = new Message(Encoding.GetEncoding(1252).GetBytes(input)).MessagePart.GetBodyAsText();
			Assert.AreEqual(expectedOutput, output);
		}
		
		/// <summary>
		/// See http://en.wikipedia.org/wiki/MIME#Multipart_messages for example
		/// </summary>
		[Test]
		public void TestMultiPartMessage()
		{
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-Type: multipart/mixed; boundary=\"frontier\"\r\n" +
				"\r\n" +
				"This is a message with multiple parts in MIME format.\r\n" +
				"--frontier\r\n" + "Content-Type: text/plain\r\n" +
				"\r\n" +
				"This is the body of the message.\r\n" +
				"--frontier\r\n" +
				"Content-Type: application/octet-stream\r\n" +
				"Content-Transfer-Encoding: base64\r\n" +
				"\r\n" +
				"PGh0bWw+CiAgPGhlYWQ+CiAgPC9oZWFkPgogIDxib2R5PgogICAgPHA+VGhpcyBpcyB0aGUg\r\n" +
				"Ym9keSBvZiB0aGUgbWVzc2FnZS48L3A+CiAgPC9ib2R5Pgo8L2h0bWw+Cg==\r\n" +
				"--frontier--";

			Message message = new Message(Encoding.ASCII.GetBytes(multipartMessage));

			MessagePart topPart = message.MessagePart;
			Assert.IsTrue(topPart.IsMultiPart);
			Assert.NotNull(topPart.MessageParts);
			Assert.AreEqual(2, topPart.MessageParts.Count);

			MessagePart part1 = topPart.MessageParts[0];
			Assert.AreEqual("text/plain", part1.ContentType.MediaType);
			Assert.AreEqual("This is the body of the message.", part1.GetBodyAsText());

			MessagePart part2 = topPart.MessageParts[1];
			Assert.AreEqual("application/octet-stream", part2.ContentType.MediaType);
			Assert.AreEqual(ContentTransferEncoding.Base64, part2.ContentTransferEncoding);
			Assert.AreEqual(
				Convert.FromBase64String("PGh0bWw+CiAgPGhlYWQ+CiAgPC9oZWFkPgogIDxib2R5PgogICAgPHA+VGhpcyBpcyB0aGUg\r\n" +
				"Ym9keSBvZiB0aGUgbWVzc2FnZS48L3A+CiAgPC9ib2R5Pgo8L2h0bWw+Cg==\r\n"),
				part2.Body);
		}

		/// <summary>
		/// See http://www.ietf.org/rfc/rfc2049.txt complex MultiPart example
		/// The example has been altered a bit to add the correct base64 encoded audio and video
		/// and to add some correct from, to and subject headers in the last part
		/// </summary>
		[Test]
		public void TestComplexMultiPartMessage()
		{
			const string multiPartMessage =
				"MIME-Version: 1.0\r\n" +
				"From: Nathaniel Borenstein <nsb@nsb.fv.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Fri, 07 Oct 1994 16:15:05 -0700 (PDT)\r\n" +
				"Subject: A multipart example\r\n" +
				"Content-Type: multipart/mixed;\r\n" +
				"\t\t\t\t\t\t boundary=unique-boundary-1\r\n" +
				"\r\n" +
				"This is the preamble area of a multipart message.\r\n" +
				"Mail readers that understand multipart format\r\n" +
				"should ignore this preamble.\r\n" +
				"\r\n" +
				"If you are reading this text, you might want to\r\n" +
				"consider changing to a mail reader that understands\r\n" +
				"how to properly display multipart messages.\r\n" +
				"\r\n" +
				"--unique-boundary-1\r\n" +
				"\r\n" +
				" ... Some text appears here ...\r\n" +
				"--unique-boundary-1\r\n" +
				"Content-type: text/plain; charset=US-ASCII\r\n" +
				"\r\n" +
				"This could have been part of the previous part, but\r\n" +
				"illustrates explicit versus implicit typing of body\r\n" +
				"parts.\r\n" +
				"--unique-boundary-1\r\n" +
				"Content-Type: multipart/parallel; boundary=unique-boundary-2\r\n" +
				"\r\n" +
				"--unique-boundary-2\r\n" +
				"Content-Type: audio/basic\r\n" +
				"Content-Transfer-Encoding: base64\r\n" +
				"\r\n" +
				"dGVzdCBhdWRpbw==\r\n" + // "test audio" in base64
				"--unique-boundary-2\r\n" +
				"Content-Type: image/jpeg\r\n" +
				"Content-Transfer-Encoding: base64\r\n" +
				"\r\n" +
				"dGVzdCBpbWFnZQ==\r\n" + // "test image" in base64
				"--unique-boundary-2--\r\n" +
				"\r\n" +
				"--unique-boundary-1\r\n" +
				"Content-type: text/enriched\r\n" +
				"\r\n" +
				"This is <bold><italic>enriched.</italic></bold>\r\n" +
				"<smaller>as defined in RFC 1896</smaller>\r\n" +
				"\r\n" +
				"Isn\'t it\r\n" +
				"<bigger><bigger>cool?</bigger></bigger>\r\n" +
				"--unique-boundary-1\r\n" +
				"Content-Type: message/rfc822\r\n" + 
				"\r\n" +
				"From: Test <test@test.com>\r\n" +
				"To: Test <test@test.com>\r\n" +
				"Subject: Test subject\r\n" +
				"Content-Type: Text/plain; charset=ISO-8859-1\r\n" +
				"Content-Transfer-Encoding: Quoted-printable\r\n" +
				"\r\n" +
				"... Additional text in ISO-8859-1 goes here ... 3 + 5 =3D 8\r\n" +
				"--unique-boundary-1--";

			// No special characters used - we can use ASCII to get the bytes
			Message message = new Message(Encoding.ASCII.GetBytes(multiPartMessage));

			Assert.AreEqual("1.0", message.Headers.MimeVersion);

			// From
			Assert.AreEqual("Nathaniel Borenstein", message.Headers.From.DisplayName);
			Assert.AreEqual("nsb@nsb.fv.com", message.Headers.From.Address);

			// To
			Assert.NotNull(message.Headers.To);
			Assert.AreEqual(1, message.Headers.To.Count);
			Assert.AreEqual("Ned Freed", message.Headers.To[0].DisplayName);
			Assert.AreEqual("ned@innosoft.com", message.Headers.To[0].Address);

			// Date
			Assert.AreEqual("Fri, 07 Oct 1994 16:15:05 -0700 (PDT)", message.Headers.Date);
			// -0700 is the same as adding 7 hours in the UTC DateTime
			Assert.AreEqual(new DateTime(1994, 10, 7, 23, 15, 05, DateTimeKind.Utc), message.Headers.DateSent);

			// Subject
			Assert.AreEqual("A multipart example", message.Headers.Subject);

			MessagePart part1 = message.MessagePart;
			Assert.AreEqual("multipart/mixed", part1.ContentType.MediaType);
			Assert.IsTrue(part1.IsMultiPart);
			Assert.NotNull(part1.MessageParts);
			Assert.IsNull(part1.Body);

			// There is a total of 5 multiparts in the first message (unique-boundary-1)
			Assert.AreEqual(5, part1.MessageParts.Count);

			// Fetch out the parts, which are checked against later
			System.Collections.Generic.List<MessagePart> attachments = message.FindAllAttachments();
			System.Collections.Generic.List<MessagePart> textVersions = message.FindAllTextVersions();

			// We are now going one level deeper into the message tree
			{
				MessagePart part1Part1 = part1.MessageParts[0];
				Assert.NotNull(part1Part1);
				Assert.IsFalse(part1Part1.IsMultiPart);
				Assert.NotNull(part1Part1.Body);
				Assert.AreEqual("text/plain", part1Part1.ContentType.MediaType);
				Assert.AreEqual(" ... Some text appears here ...", part1Part1.GetBodyAsText());

				// Check that the fetching algoritm for finding a plain-text version is working
				Assert.AreEqual(part1Part1, message.FindFirstPlainTextVersion());

				// Check this message is included in the text version
				Assert.Contains(part1Part1, textVersions);

				// But not included in the attachments
				Assert.IsFalse(attachments.Contains(part1Part1));

				// We are now going one level deeper into the message tree
				{
					MessagePart part1Part2 = part1.MessageParts[1];
					Assert.NotNull(part1Part2);
					Assert.IsFalse(part1Part2.IsMultiPart);
					Assert.NotNull(part1Part2.Body);
					Assert.AreEqual("text/plain", part1Part2.ContentType.MediaType);
					Assert.AreEqual("US-ASCII", part1Part2.ContentType.CharSet);
					Assert.AreEqual("This could have been part of the previous part, but\r\n" +
									"illustrates explicit versus implicit typing of body\r\n" +
									"parts.", part1Part2.GetBodyAsText());

					// Check this message is included in the text version
					Assert.Contains(part1Part2, textVersions);

					// But not included in the attachments
					Assert.IsFalse(attachments.Contains(part1Part2));
				}

				// We are now going one level deeper into the message tree
				{
					MessagePart part1Part3 = part1.MessageParts[2];
					Assert.NotNull(part1Part3);
					Assert.IsTrue(part1Part3.IsMultiPart);
					Assert.IsNotNull(part1Part3.MessageParts);
					Assert.IsNull(part1Part3.Body);

					// There is a total of message parts in part1Part3
					Assert.AreEqual(2, part1Part3.MessageParts.Count);
					Assert.AreEqual("multipart/parallel", part1Part3.ContentType.MediaType);

					// Check this message is not in the text versions
					Assert.IsFalse(textVersions.Contains(part1Part3));

					// Check this message is not included in the attachments
					Assert.IsFalse(attachments.Contains(part1Part3));

					// We are now diving into part1Part3 multiparts - therefore going one level deeper in the message tree
					{
						MessagePart part1Part3Part1 = part1Part3.MessageParts[0];
						Assert.NotNull(part1Part3Part1);
						Assert.IsFalse(part1Part3Part1.IsMultiPart);
						Assert.NotNull(part1Part3Part1.Body);
						Assert.AreEqual("audio/basic", part1Part3Part1.ContentType.MediaType);
						Assert.AreEqual(ContentTransferEncoding.Base64, part1Part3Part1.ContentTransferEncoding);
						Assert.AreEqual("test audio", part1Part3Part1.GetBodyAsText());

						// Check this message is not in the text versions
						Assert.IsFalse(textVersions.Contains(part1Part3Part1));

						// Check this message is included in the attachments
						Assert.Contains(part1Part3Part1, attachments);

						MessagePart part1Part3Part2 = part1Part3.MessageParts[1];
						Assert.NotNull(part1Part3Part2);
						Assert.IsFalse(part1Part3Part2.IsMultiPart);
						Assert.NotNull(part1Part3Part2.Body);
						Assert.AreEqual("image/jpeg", part1Part3Part2.ContentType.MediaType);
						Assert.AreEqual(ContentTransferEncoding.Base64, part1Part3Part2.ContentTransferEncoding);
						Assert.AreEqual("test image", part1Part3Part2.GetBodyAsText());

						// Check this message is not in the text versions
						Assert.IsFalse(textVersions.Contains(part1Part3Part2));

						// Check this message is included in the attachments
						Assert.Contains(part1Part3Part2, attachments);
					}
				}

				// We are now going one level deeper into the message tree
				{
					MessagePart part1Part4 = part1.MessageParts[3];
					Assert.NotNull(part1Part4);
					Assert.IsFalse(part1Part4.IsMultiPart);
					Assert.NotNull(part1Part4.Body);
					Assert.AreEqual("text/enriched", part1Part4.ContentType.MediaType);
					Assert.AreEqual("This is <bold><italic>enriched.</italic></bold>\r\n" +
									"<smaller>as defined in RFC 1896</smaller>\r\n" +
									"\r\n" +
									"Isn\'t it\r\n" +
									"<bigger><bigger>cool?</bigger></bigger>", part1Part4.GetBodyAsText());

					// Check this message is in the text versions
					Assert.Contains(part1Part4, textVersions);

					// Check this message is not included in the attachments
					Assert.IsFalse(attachments.Contains(part1Part4));
				}

				// We are now going one level deeper into the message tree
				{
					MessagePart part1Part5 = part1.MessageParts[4];
					Assert.NotNull(part1Part5);
					Assert.IsFalse(part1Part5.IsMultiPart);
					Assert.NotNull(part1Part5.Body);
					Assert.AreEqual("message/rfc822", part1Part5.ContentType.MediaType);
					Assert.AreEqual("From: Test <test@test.com>\r\n" +
									"To: Test <test@test.com>\r\n" +
									"Subject: Test subject\r\n" +
									"Content-Type: Text/plain; charset=ISO-8859-1\r\n" +
									"Content-Transfer-Encoding: Quoted-printable\r\n" +
									"\r\n" +
									"... Additional text in ISO-8859-1 goes here ... 3 + 5 =3D 8", part1Part5.GetBodyAsText());

					// Check this message is in the text versions
					Assert.Contains(part1Part5, textVersions);

					// Check this message is not included in the attachments
					Assert.IsFalse(attachments.Contains(part1Part5));

					// This last part is actually a message. Lets try to parse it
					Message lastMessage = new Message(part1Part5.Body);

					// From
					Assert.AreEqual("Test", lastMessage.Headers.From.DisplayName);
					Assert.AreEqual("test@test.com", lastMessage.Headers.From.Address);

					// To
					Assert.NotNull(lastMessage.Headers.To);
					Assert.AreEqual(1, lastMessage.Headers.To.Count);
					Assert.AreEqual("Test", lastMessage.Headers.To[0].DisplayName);
					Assert.AreEqual("test@test.com", lastMessage.Headers.To[0].Address);

					// Subject
					Assert.AreEqual("Test subject", lastMessage.Headers.Subject);

					// We are now going one level deeper into the message tree
					{
						MessagePart lastPart = lastMessage.MessagePart;
						Assert.IsFalse(lastPart.IsMultiPart);
						Assert.IsNull(lastPart.MessageParts);
						Assert.NotNull(lastPart.Body);
						Assert.AreEqual(ContentTransferEncoding.QuotedPrintable, lastPart.ContentTransferEncoding);
						Assert.AreEqual("Text/plain", lastPart.ContentType.MediaType);
						Assert.AreEqual("ISO-8859-1", lastPart.ContentType.CharSet);

						// Notice that =3D has been decoded to = because it was QuotedPrintable encoded
						Assert.AreEqual("... Additional text in ISO-8859-1 goes here ... 3 + 5 = 8", lastPart.GetBodyAsText());
					}
				}
			}
		}



		/// <summary>
		/// Tests that when getting the raw bytes of an application/pdf
		/// then the bytes are not interpreted using US-ASCII before being passed
		/// back to the caller
		/// </summary>
		[Test]
		public void ParsingMediaTypeOctetStream()
		{
			const string base64 =
				"JVBERi0xLjUNCiW1tbW1DQoxIDAgb2JqDQo8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIv\r\n" +
				"TGFuZyhkYS1ESykgL1N0cnVjdFRyZWVSb290IDE1IDAgUi9NYXJrSW5mbzw8L01hcmtlZCB0\r\n" +
				"cnVlPj4+Pg0KZW5kb2JqDQoyIDAgb2JqDQo8PC9UeXBlL1BhZ2VzL0NvdW50IDEvS2lkc1sg\r\n" +
				"MyAwIFJdID4+DQplbmRvYmoNCjMgMCBvYmoNCjw8L1R5cGUvUGFnZS9QYXJlbnQgMiAwIFIv\r\n" +
				"UmVzb3VyY2VzPDwvRm9udDw8L0YxIDUgMCBSL0YyIDcgMCBSL0YzIDkgMCBSPj4vUHJvY1Nl\r\n" +
				"dFsvUERGL1RleHQvSW1hZ2VCL0ltYWdlQy9JbWFnZUldID4+L01lZGlhQm94WyAwIDAgNTk0\r\n" +
				"Ljk2IDg0Mi4wNF0gL0NvbnRlbnRzIDQgMCBSL0dyb3VwPDwvVHlwZS9Hcm91cC9TL1RyYW5z\r\n" +
				"cGFyZW5jeS9DUy9EZXZpY2VSR0I+Pi9UYWJzL1MvU3RydWN0UGFyZW50cyAwPj4NCmVuZG9i";

			const string partPDF =
				"Content-Type: application/pdf;\r\n" +
				" name=\"=?ISO-8859-1?Q?=D8nskeliste=2Epdf?=\"\r\n" +
				"Content-Transfer-Encoding: base64\r\n" +
				"\r\n" +
				base64;

			// Base 64 is only in ASCII
			Message message = new Message(Encoding.ASCII.GetBytes(partPDF));

			MessagePart messagePart = message.MessagePart;

			// Check the headers
			Assert.AreEqual("application/pdf", messagePart.ContentType.MediaType);
			Assert.AreEqual(ContentTransferEncoding.Base64, messagePart.ContentTransferEncoding);
			Assert.AreEqual("Ønskeliste.pdf", messagePart.ContentType.Name);

			// This will fail if US-ASCII is assumed on the bytes when decoded from base64 to bytes
			Assert.AreEqual(Convert.FromBase64String(base64), messagePart.Body);
		}

		/// <summary>
		/// Test that test that a message that does not end with the correct boundary of
		/// "--boundary--" but ends with "--boundary" instead can be parsed
		/// </summary>
		[Test]
		public void TestMultiPartMessageWithWrongBoundaryEnd()
		{
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-Type: multipart/alternative; boundary=\"boundary\"\r\n" +
				"\r\n" +
				"This is a message with multiple parts in MIME format.\r\n" +
				"--boundary\r\n" + "Content-Type: text/plain\r\n" +
				"\r\n" +
				"This is the body of the message.\r\n" +
				"--boundary\r\n" +
				"Content-Type: text/html\r\n" +
				"\r\n" +
				"<html></html>\r\n" +
				"--boundary"; // Here the correct ending boundary would be "--boundary--"

			Message message = null;
			
			Assert.DoesNotThrow( delegate { message = new Message(Encoding.ASCII.GetBytes(multipartMessage)); });

			Assert.NotNull(message);

			Assert.AreEqual("This is the body of the message.", message.MessagePart.MessageParts[0].GetBodyAsText());
			Assert.AreEqual("<html></html>", message.MessagePart.MessageParts[1].GetBodyAsText());
		}

		[Test]
		public void TestMultiPartMessageWithNoBoundaryEnd()
		{
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-Type: multipart/alternative; boundary=\"boundary\"\r\n" +
				"\r\n" +
				"This is a message with multiple parts in MIME format.\r\n" +
				"--boundary\r\n" + "Content-Type: text/plain\r\n" +
				"\r\n" +
				"This is the body of the message.\r\n" +
				"--boundary\r\n" +
				"Content-Type: text/html\r\n" +
				"\r\n" +
				"<html></html>\r\n";
				// Here there should have been a boundary "--boundary--" to delimit last ending

			Message message = null;

			Assert.DoesNotThrow(delegate { message = new Message(Encoding.ASCII.GetBytes(multipartMessage)); });

			Assert.NotNull(message);

			Assert.AreEqual(2, message.MessagePart.MessageParts.Count);
			Assert.AreEqual("This is the body of the message.", message.MessagePart.MessageParts[0].GetBodyAsText());
			Assert.AreEqual("<html></html>", message.MessagePart.MessageParts[1].GetBodyAsText());
		}

		/// <summary>
		/// See http://tools.ietf.org/html/rfc2046#section-5.1.1 for the example
		/// </summary>
		[Test]
		public void TestMultiPartRFCExample()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"This is the preamble.  It is to be ignored, though it\r\n" +
				" is a handy place for composition agents to include an\r\n" +
				" explanatory note to non-MIME conformant readers.\r\n" + 
				"\r\n" +
				"--simple boundary\r\n" + 
				"\r\n" +
				"This is implicitly typed plain US-ASCII text.\r\n" + 
				"It does NOT end with a linebreak.\r\n" + 
				"--simple boundary\r\n" +
				"Content-type: text/plain; charset=us-ascii\r\n" +
				"\r\n" +
				"This is explicitly typed plain US-ASCII text.\r\n" +
				"It DOES end with a linebreak.\r\n" + 
				"\r\n" +
				"--simple boundary--\r\n" + 
				"\r\n" +
				"This is the epilogue. It is also to be ignored.";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			Assert.NotNull(message);

			Assert.AreEqual("Nathaniel Borenstein", message.Headers.From.DisplayName);
			Assert.AreEqual("nsb@bellcore.com", message.Headers.From.Address);

			Assert.AreEqual(1, message.Headers.To.Count);
			Assert.AreEqual("Ned Freed", message.Headers.To[0].DisplayName);
			Assert.AreEqual("ned@innosoft.com", message.Headers.To[0].Address);

			// -0800 is the same as adding 8 hours to UTC
			Assert.AreEqual(new DateTime(1993, 3, 22, 7, 56, 48, DateTimeKind.Utc), message.Headers.DateSent);

			Assert.AreEqual("Sample message", message.Headers.Subject);

			Assert.AreEqual("1.0", message.Headers.MimeVersion);

			Assert.AreEqual("multipart/mixed", message.Headers.ContentType.MediaType);
			Assert.AreEqual("simple boundary", message.Headers.ContentType.Boundary);

			MessagePart multiPart = message.MessagePart;
			Assert.IsTrue(multiPart.IsMultiPart);
			Assert.AreEqual("multipart/mixed", multiPart.ContentType.MediaType);
			Assert.AreEqual("simple boundary", multiPart.ContentType.Boundary);
			Assert.NotNull(multiPart.MessageParts);
			Assert.AreEqual(2, multiPart.MessageParts.Count);

			MessagePart firstPlainText = multiPart.MessageParts[0];
			Assert.AreEqual("This is implicitly typed plain US-ASCII text.\r\n" +
				"It does NOT end with a linebreak.", firstPlainText.GetBodyAsText());
			Assert.AreEqual(Encoding.ASCII, firstPlainText.BodyEncoding);
			Assert.AreEqual("us-ascii", firstPlainText.ContentType.CharSet);
			Assert.AreEqual("text/plain", firstPlainText.ContentType.MediaType);

			MessagePart secondPlainText = multiPart.MessageParts[1];
			Assert.AreEqual("This is explicitly typed plain US-ASCII text.\r\n" +
				"It DOES end with a linebreak.\r\n", secondPlainText.GetBodyAsText());
			Assert.AreEqual(Encoding.ASCII, secondPlainText.BodyEncoding);
			Assert.AreEqual("us-ascii", secondPlainText.ContentType.CharSet);
			Assert.AreEqual("text/plain", secondPlainText.ContentType.MediaType);
		}

		[Test]
		public void TestGetFirstMessageWithMediaTypeSimpleFound()
		{
			const string rfcExample =
				"Content-type: text/plain; charset=us-ascii\r\n" +
				"\r\n" +
				"This is explicitly typed plain US-ASCII text";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("text/plain");

			Assert.NotNull(part);
			Assert.AreEqual("text/plain", part.ContentType.MediaType);
			Assert.AreEqual("us-ascii", part.ContentType.CharSet);
			Assert.AreEqual(Encoding.ASCII, part.BodyEncoding);
			Assert.AreEqual("This is explicitly typed plain US-ASCII text", part.GetBodyAsText());
		}

		[Test]
		public void TestGetFirstMessageWithMediaTypeSimpleNotFound()
		{
			const string rfcExample =
				"Content-type: text/plain; charset=us-ascii\r\n" +
				"\r\n" +
				"This is explicitly typed plain US-ASCII text";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("text/html");

			// We should not be able to find such a MessagePart
			Assert.Null(part);
		}

		[Test]
		public void TestGetFirstMessageWithMediaTypeSimpleFoundHTML()
		{
			const string rfcExample =
				"Content-type: text/html; charset=us-ascii\r\n" +
				"\r\n" +
				"This is explicitly typed plain US-ASCII HTML text";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("text/html");

			Assert.NotNull(part);
			Assert.AreEqual("text/html", part.ContentType.MediaType);
			Assert.AreEqual("us-ascii", part.ContentType.CharSet);
			Assert.AreEqual(Encoding.ASCII, part.BodyEncoding);
			Assert.AreEqual("This is explicitly typed plain US-ASCII HTML text", part.GetBodyAsText());
		}

		[Test]
		public void TestGetFirstMessageWithMediaTypeMultiPartFound()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("text/plain");

			Assert.NotNull(part);
			Assert.AreEqual("text/plain", part.ContentType.MediaType);
			Assert.AreEqual("us-ascii", part.ContentType.CharSet);
			Assert.AreEqual(Encoding.ASCII, part.BodyEncoding);
			Assert.AreEqual("TEXT", part.GetBodyAsText());
		}

		[Test]
		public void TestGetFirstMessageWithMediaTypeMultiPartFoundSecond()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("text/html");

			Assert.NotNull(part);
			Assert.AreEqual("text/html", part.ContentType.MediaType);
			Assert.AreEqual("ISO-8859-1", part.ContentType.CharSet);
			Assert.AreEqual(Encoding.GetEncoding("ISO-8859-1"), part.BodyEncoding);
			Assert.AreEqual("HTML", part.GetBodyAsText());
		}

		[Test]
		public void TestGetFirstMessageWithMediaTypeMultiPartFindMultiPartMediaType()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("multipart/mixed");

			Assert.NotNull(part);
			Assert.IsTrue(part.IsMultiPart);
			Assert.AreEqual("simple boundary", part.ContentType.Boundary);
		}

		/// <summary>
		/// Tests that it is the first multipart/mixed <see cref="MessagePart"/> that is returned,
		/// and not the most nested one
		/// </summary>
		[Test]
		public void TestGetFirstMessageWithMediaTypeMultiPartFindMultiPartMediaTypeWithNestedMultiPart()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"Content-type: multipart/mixed; boundary=\"anotherBoundary\"\r\n" +
				"\r\n" +
				"--anotherBoundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--anotherBoundary\r\n" +
				"\r\n" +
				"MORE TEXT\r\n" +
				"--anotherBoundary--\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("multipart/mixed");

			Assert.NotNull(part);
			Assert.IsTrue(part.IsMultiPart);
			Assert.AreEqual("simple boundary", part.ContentType.Boundary);
		}

		/// <summary>
		/// Tests that it is the first text/html <see cref="MessagePart"/> that is returned,
		/// and not the one mentioned later in the hierarchy
		/// </summary>
		[Test]
		public void TestGetFirstMessageWithMediaTypeMultiPartFindMultiPartMediaTypeWithMultipleHTML()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"Content-type: multipart/mixed; boundary=\"anotherBoundary\"\r\n" +
				"\r\n" +
				"--anotherBoundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--anotherBoundary\r\n" +
				"Content-Type: text/html; charset=us-ascii\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--anotherBoundary--\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"MORE HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstMessagePartWithMediaType("text/html");

			Assert.NotNull(part);
			Assert.IsFalse(part.IsMultiPart);
			Assert.AreEqual("text/html", part.ContentType.MediaType);
			Assert.AreEqual("HTML", part.GetBodyAsText());
		}

		[Test]
		public void TestGetAllMessagesWithMediaTypeSimpleFound()
		{
			const string rfcExample =
				"Content-type: text/plain; charset=us-ascii\r\n" +
				"\r\n" +
				"This is explicitly typed plain US-ASCII text";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			System.Collections.Generic.List<MessagePart> parts = message.FindAllMessagePartsWithMediaType("text/plain");

			Assert.NotNull(parts);
			Assert.IsNotEmpty(parts);
			Assert.AreEqual(1, parts.Count);
			MessagePart foundMessagePart = parts[0];
			Assert.AreEqual("text/plain", foundMessagePart.ContentType.MediaType);
			Assert.AreEqual("us-ascii", foundMessagePart.ContentType.CharSet);
			Assert.AreEqual(Encoding.ASCII, foundMessagePart.BodyEncoding);
			Assert.AreEqual("This is explicitly typed plain US-ASCII text", foundMessagePart.GetBodyAsText());
		}

		[Test]
		public void TestAllFirstMessagesWithMediaTypeSimpleNotFound()
		{
			const string rfcExample =
				"Content-type: text/plain; charset=us-ascii\r\n" +
				"\r\n" +
				"This is explicitly typed plain US-ASCII text";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			System.Collections.Generic.List<MessagePart> parts = message.FindAllMessagePartsWithMediaType("text/html");

			// We should not be able to find such a MessagePart
			Assert.NotNull(parts);
			Assert.IsEmpty(parts);
		}

		[Test]
		public void TestGetAllMessagesWithMediaTypeMultiPartFindMultiPartMediaTypeWithMultipleHTML()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"Content-type: multipart/mixed; boundary=\"anotherBoundary\"\r\n" +
				"\r\n" +
				"--anotherBoundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--anotherBoundary\r\n" +
				"Content-Type: text/html; charset=us-ascii\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--anotherBoundary--\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"MORE HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			System.Collections.Generic.List<MessagePart> parts = message.FindAllMessagePartsWithMediaType("text/html");

			Assert.NotNull(parts);
			Assert.IsNotEmpty(parts);
			Assert.AreEqual(2, parts.Count);

			MessagePart firstPart = parts[0];
			Assert.IsFalse(firstPart.IsMultiPart);
			Assert.AreEqual("text/html", firstPart.ContentType.MediaType);
			Assert.AreEqual("HTML", firstPart.GetBodyAsText());

			MessagePart secondPart = parts[1];
			Assert.IsFalse(secondPart.IsMultiPart);
			Assert.AreEqual("text/html", secondPart.ContentType.MediaType);
			Assert.AreEqual("MORE HTML", secondPart.GetBodyAsText());
		}

		[Test]
		public void TestGetAllMessagesWithMediaTypeMultiPartFindMultiPartMediaTypeWithMultipleMultiParts()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"Content-type: multipart/mixed; boundary=\"anotherBoundary\"\r\n" +
				"\r\n" +
				"--anotherBoundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--anotherBoundary\r\n" +
				"Content-Type: text/html; charset=us-ascii\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--anotherBoundary--\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"MORE HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			System.Collections.Generic.List<MessagePart> parts = message.FindAllMessagePartsWithMediaType("multipart/mixed");

			Assert.NotNull(parts);
			Assert.IsNotEmpty(parts);
			Assert.AreEqual(2, parts.Count);

			MessagePart firstPart = parts[0];
			Assert.IsTrue(firstPart.IsMultiPart);
			Assert.AreEqual("multipart/mixed", firstPart.ContentType.MediaType);
			Assert.AreEqual("simple boundary", firstPart.ContentType.Boundary);

			MessagePart secondPart = parts[1];
			Assert.IsTrue(secondPart.IsMultiPart);
			Assert.AreEqual("multipart/mixed", secondPart.ContentType.MediaType);
			Assert.AreEqual("anotherBoundary", secondPart.ContentType.Boundary);
		}

		[Test]
		public void TestGetFirstHTMLVersion()
		{
			const string rfcExample =
				"Content-type: text/html; charset=us-ascii\r\n" +
				"\r\n" +
				"HTML here";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			MessagePart part = message.FindFirstHtmlVersion();

			Assert.NotNull(part);
			Assert.AreEqual("text/html", part.ContentType.MediaType);
			Assert.AreEqual("HTML here", part.GetBodyAsText());
		}

		[Test]
		public void TestContentTypeWithLongName()
		{
			const string messageHeaders =
				"Content-Type: text/plain;\r\n" +
				" name=\"very long email testvery long email testvery long email testvery long email\r\n" +
				" testvery long email testvery long email testvery long email test.txt\"\r\n" +
				"\r\n";

			MessageHeader headers = new Message(Encoding.ASCII.GetBytes(messageHeaders), false).Headers;

			Assert.NotNull(headers);
			Assert.NotNull(headers.ContentType);

			Assert.AreEqual("very long email testvery long email testvery long email testvery long email" +
			" testvery long email testvery long email testvery long email test.txt", headers.ContentType.Name);
		}

		/// <summary>
		/// See http://tools.ietf.org/html/rfc2231 for the continuation of header fields definition.
		/// </summary>
		[Test]
		public void TestContentTypeParseMultiPartBoundaryWithContinuation()
		{
			const string messageHeaders =
				"Content-type: multipart/report; report-type=delivery-status;\r\n" +
				" boundary*0=1804289383_1288411300_549365113_21474836;\r\n" +
				" boundary*1=47_bda2385.bisx.prod.on.blackberry\r\n" +
				"\r\n";

			MessageHeader headers = new Message(Encoding.ASCII.GetBytes(messageHeaders), false).Headers;

			Assert.NotNull(headers.ContentType.Boundary);
			Assert.AreEqual("1804289383_1288411300_549365113_2147483647_bda2385.bisx.prod.on.blackberry", headers.ContentType.Boundary);
		}

		/// <summary>
		/// See http://tools.ietf.org/html/rfc2231 for the filename* definition.
		/// </summary>
		[Test]
		public void ParseContentDispositionFilenameWithEncoding()
		{
			const string messageHeaders =
				"Content-Disposition: attachment;\r\n" +
				" filename*=ISO-8859-1\'\'%D8%6E%73%6B%65%6C%69%73%74%65%2E%70%64%66\r\n" +
				"\r\n";

			MessageHeader headers = new Message(Encoding.ASCII.GetBytes(messageHeaders), false).Headers;

			// Tests that the ContentDisposition header correctly decoded the filename
			Assert.NotNull(headers.ContentDisposition.FileName);
			Assert.AreEqual("Ønskeliste.pdf", headers.ContentDisposition.FileName);
		}

		[Test]
		public void TestIsAttachmentNotAttachment()
		{
			const string messageString =
				"Content-Type: text/plain\r\n" + 
				"\r\n" +
				"Testing";

			Message message = new Message(Encoding.ASCII.GetBytes(messageString));

			Assert.IsFalse(message.MessagePart.IsAttachment);
		}

		[Test]
		public void TestIsAttachmentIsAnAttachment()
		{
			const string messageString =
				"Content-Type: text/plain\r\n" +
				"Content-Disposition: attachment\r\n" +
				"\r\n" +
				"Testing";

			Message message = new Message(Encoding.ASCII.GetBytes(messageString));

			Assert.IsTrue(message.MessagePart.IsAttachment);
		}

		[Test]
		public void TestSaveAndLoad()
		{
			const string messageString =
				"Content-Type: text/plain\r\n" +
				"Content-Disposition: attachment\r\n" +
				"\r\n" +
				"Testing";

			Message message = new Message(Encoding.ASCII.GetBytes(messageString));

			FileInfo testFile = new FileInfo("test_message_save_.testFile");

			message.Save(testFile);

			Message message2 = Message.Load(testFile);

			Assert.AreEqual("Testing", message.MessagePart.GetBodyAsText());
			Assert.AreEqual("Testing", message2.MessagePart.GetBodyAsText());

			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message2.Headers.ContentType.MediaType);

			Assert.IsFalse(message.Headers.ContentDisposition.Inline);
			Assert.IsFalse(message2.Headers.ContentDisposition.Inline);

			testFile.Delete();
		}

		[Test]
		public void TestSaveAndLoadComplex()
		{
			const string rfcExample =
				"From: Nathaniel Borenstein <nsb@bellcore.com>\r\n" +
				"To: Ned Freed <ned@innosoft.com>\r\n" +
				"Date: Sun, 21 Mar 1993 23:56:48 -0800 (PST)\r\n" +
				"Subject: Sample message\r\n" +
				"MIME-Version: 1.0\r\n" +
				"Content-type: multipart/mixed; boundary=\"simple boundary\"\r\n" +
				"\r\n" +
				"--simple boundary\r\n" +
				"Content-type: multipart/mixed; boundary=\"anotherBoundary\"\r\n" +
				"\r\n" +
				"--anotherBoundary\r\n" +
				"\r\n" +
				"TEXT\r\n" +
				"--anotherBoundary\r\n" +
				"Content-Type: text/html; charset=us-ascii\r\n" +
				"\r\n" +
				"HTML\r\n" +
				"--anotherBoundary--\r\n" +
				"--simple boundary\r\n" +
				"Content-type: text/html; charset=ISO-8859-1\r\n" +
				"\r\n" +
				"MORE HTML\r\n" +
				"--simple boundary--";

			Message message = new Message(Encoding.ASCII.GetBytes(rfcExample));

			{
				System.Collections.Generic.List<MessagePart> parts = message.FindAllMessagePartsWithMediaType("multipart/mixed");

				Assert.NotNull(parts);
				Assert.IsNotEmpty(parts);
				Assert.AreEqual(2, parts.Count);

				MessagePart firstPart = parts[0];
				Assert.IsTrue(firstPart.IsMultiPart);
				Assert.AreEqual("multipart/mixed", firstPart.ContentType.MediaType);
				Assert.AreEqual("simple boundary", firstPart.ContentType.Boundary);

				MessagePart secondPart = parts[1];
				Assert.IsTrue(secondPart.IsMultiPart);
				Assert.AreEqual("multipart/mixed", secondPart.ContentType.MediaType);
				Assert.AreEqual("anotherBoundary", secondPart.ContentType.Boundary);
			}

			FileInfo testFile = new FileInfo("test_message_save_.testFile");
			message.Save(testFile);

			Message message2 = Message.Load(testFile);
			{
				System.Collections.Generic.List<MessagePart> parts2 = message2.FindAllMessagePartsWithMediaType("multipart/mixed");

				Assert.NotNull(parts2);
				Assert.IsNotEmpty(parts2);
				Assert.AreEqual(2, parts2.Count);

				MessagePart firstPart2 = parts2[0];
				Assert.IsTrue(firstPart2.IsMultiPart);
				Assert.AreEqual("multipart/mixed", firstPart2.ContentType.MediaType);
				Assert.AreEqual("simple boundary", firstPart2.ContentType.Boundary);

				MessagePart secondPart2 = parts2[1];
				Assert.IsTrue(secondPart2.IsMultiPart);
				Assert.AreEqual("multipart/mixed", secondPart2.ContentType.MediaType);
				Assert.AreEqual("anotherBoundary", secondPart2.ContentType.Boundary);
			}

			testFile.Delete();
		}

		[Test]
		public void TestSenderNotIncluded()
		{
			const string messageString =
				"Content-Type: text/plain; \r\n" +
				"\r\n" +
				"Testing";

			Message message = new Message(Encoding.ASCII.GetBytes(messageString));

			Assert.IsNull(message.Headers.Sender);
		}

		[Test]
		public void TestSender()
		{
			const string messageString =
				"Content-Type: text/plain; \r\n" +
				"Sender: Secretary <secretary@example.com>\r\n" +
				"\r\n" +
				"Testing";

			Message message = new Message(Encoding.ASCII.GetBytes(messageString));

			Assert.IsNotNull(message.Headers.Sender);
			Assert.IsTrue(message.Headers.Sender.HasValidMailAddress);
			Assert.AreEqual("Secretary", message.Headers.Sender.DisplayName);
			Assert.AreEqual("secretary@example.com", message.Headers.Sender.Address);
		}

		[Test]
		public void TestMessageWithonlyHeaders()
		{
			const string messageString =
				"Content-Type: text/plain";

			Message message = new Message(Encoding.ASCII.GetBytes(messageString));

			Assert.NotNull(message);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			Assert.NotNull(message.MessagePart);
			Assert.IsFalse(message.MessagePart.IsMultiPart);
			Assert.IsEmpty(message.MessagePart.Body);
			Assert.IsEmpty(message.MessagePart.GetBodyAsText());
		}

		[Test]
		public void TestContentTypeWithLargeCharactersCanStillBeFound()
		{
			const string messagePartContent =
				"Content-Type: TEXT/PLAIN\r\n" +
				"\r\n" + // End of message headers
				"foo";

			Message message = new Message(Encoding.ASCII.GetBytes(messagePartContent));

			// Cna be found
			MessagePart textHtml = message.FindFirstPlainTextVersion();
			Assert.NotNull(textHtml);

			Assert.AreEqual("foo", textHtml.GetBodyAsText());

			// Can still be found
			System.Collections.Generic.List<MessagePart> messageParts = message.FindAllTextVersions();
			Assert.IsNotEmpty(messageParts);
			Assert.AreEqual(1, messageParts.Count);
			Assert.AreEqual(textHtml, messageParts[0]);
		}

        [Test]
        public void TestLoadSimple()
        {
            const string input =
                "Content-Type: text/plain; charset=iso-8859-1\r\n" +
                "Content-Transfer-Encoding: quoted-printable\r\n" +
                "\r\n" + // Headers end
                "Hello=\r\n";

            // The QP encoding would have decoded Hello=\r\n into Hello, since =\r\n is a soft line break
            const string expectedOutput = "Hello";

            string output = Message.Load(new MemoryStream(Encoding.ASCII.GetBytes(input))).MessagePart.GetBodyAsText();
            Assert.AreEqual(expectedOutput, output);
        }

        [Test]
        public void TestFileOverwriteTruncatesFileCorrectly()
        {
            const string longMessage = "Content-Type: text/plain\r\n" +
                "Content-Disposition: attachment\r\n" +
                "\r\n" +
                "Testing very long...";

            const string smallMessage =
                "Content-Type: text/plain\r\n" +
                "Content-Disposition: attachment\r\n" +
                "\r\n" +
                "Testing";

            FileInfo testFile = new FileInfo("test_message_save_truncate.testFile");

            Message messageLong = new Message(Encoding.ASCII.GetBytes(longMessage));
            messageLong.Save(testFile);

            Message messageSmall = new Message(Encoding.ASCII.GetBytes(smallMessage));
            messageSmall.Save(testFile);

            Message messageLoaded = Message.Load(testFile);

            testFile.Delete();

            // We should now have the small message in the file.
            // IE We should have overwritten the longMessage file with our shortMessage file.
            Assert.AreEqual(messageSmall.RawMessage, messageLoaded.RawMessage);
        }
	}
}