using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenPop.Mime;
using OpenPop.Mime.Header;

namespace OpenPopUnitTests.Mime
{
	[TestFixture]
	public class MessagePartTests
	{
		[Test]
		public void TestIsTextTextPlain()
		{
			const string messagePartContent =
				"Content-Type: text/plain\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsText);
		}

		[Test]
		public void TestIsTextTextHtml()
		{
			const string messagePartContent =
				"Content-Type: text/html\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsText);
		}

		[Test]
		public void TestIsTextMessageRfc822()
		{
			const string messagePartContent =
				"Content-Type: message/rfc822\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsText);
		}

		[Test]
		public void TestIsAttachmentApplicationOgg()
		{
			const string messagePartContent =
				"Content-Type: application/ogg\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsAttachment);
		}

		[Test]
		public void TestIsAttachmentApplicationZip()
		{
			const string messagePartContent =
				"Content-Type: application/zip\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsAttachment);
		}

		[Test]
		public void TestIsAttachmentApplicationPdf()
		{
			const string messagePartContent =
				"Content-Type: application/pdf\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsAttachment);
		}

		[Test]
		public void TestIsAttachmentAudioMpeg()
		{
			const string messagePartContent =
				"Content-Type: audio/mpeg\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsAttachment);
		}

		[Test]
		public void TestIsAttachmentImageJpeg()
		{
			const string messagePartContent =
				"Content-Type: image/jpeg\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsTrue(messagePart.IsAttachment);
		}

		[Test]
		public void TestSaveToFile()
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

			const string partPdf =
				"Content-Type: application/pdf;\r\n" +
				" name=\"=?ISO-8859-1?Q?=D8nskeliste=2Epdf?=\"\r\n" +
				"Content-Transfer-Encoding: base64\r\n" +
				"\r\n" +
				base64;

			// Base 64 is only in ASCII
			Message message = new Message(Encoding.ASCII.GetBytes(partPdf));

			MessagePart messagePart = message.MessagePart;

			// Check the headers
			Assert.AreEqual("application/pdf", messagePart.ContentType.MediaType);
			Assert.AreEqual(ContentTransferEncoding.Base64, messagePart.ContentTransferEncoding);
			Assert.AreEqual("Ønskeliste.pdf", messagePart.ContentType.Name);

			byte[] correctBytes = Convert.FromBase64String(base64);
			// This will fail if US-ASCII is assumed on the bytes when decoded from base64 to bytes
			Assert.AreEqual(correctBytes, messagePart.Body);

			FileInfo testFile = new FileInfo("test_message_save_.testFile");
			messagePart.Save(testFile);

			byte[] fileBytes = File.ReadAllBytes(testFile.ToString());
			testFile.Delete();

			Assert.AreEqual(correctBytes, fileBytes);
		}

		[Test]
		public void TestQuotedPrintableDoesNotDecodeUnderscoresInBody()
		{
			const string messagePartContent =
				"Content-Transfer-Encoding: quoted-printable\r\n" +
				"\r\n" + // End of message headers
				"a_a";

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			// QuotedPrintable, when used as Content-Transfer-Encoding does not decode _ to spaces
			const string expectedBody = "a_a";
			string actualBody = messagePart.GetBodyAsText();

			Assert.AreEqual(expectedBody, actualBody);
		}

		[Test]
		public void TestContentDescription()
		{
			const string messagePartContent =
				"Content-Description: This is some human readable text\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			const string expectedDescription = "This is some human readable text";
			string actualDescription = messagePart.ContentDescription;
			Assert.AreEqual(expectedDescription, actualDescription);
		}

		[Test]
		public void TestContentDescriptionTwo()
		{
			const string messagePartContent =
				"Content-Description: This is some OTHER human readable text\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			const string expectedDescription = "This is some OTHER human readable text";
			string actualDescription = messagePart.ContentDescription;
			Assert.AreEqual(expectedDescription, actualDescription);
		}

		[Test]
		public void TestContentDisposition()
		{
			const string messagePartContent =
				"Content-Disposition: attachment\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;
			Assert.IsFalse(messagePart.ContentDisposition.Inline);
		}

		[Test]
		public void TestHeaderWhiteSpace()
		{
			const string messagePartContent =
				"Content-Disposition: attachment; filename=Attach2.txt; modification-date=\"21\r\n" +
				" Feb 2011 17:09:47 +0000\"; read-date=\"21 Feb 2011 17:09:47 +0000\";\r\n" +
				" creation-date=\"21 Feb 2011 12:59:07 +0000\"\r\n" +
				"\r\n"; // End of message headers

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.IsFalse(messagePart.ContentDisposition.Inline);

			DateTime modificationDate = new DateTime(2011, 2, 21, 17, 09, 47, DateTimeKind.Utc);
			Assert.AreEqual(modificationDate, messagePart.ContentDisposition.ModificationDate);

			DateTime readDate = modificationDate;
			Assert.AreEqual(readDate, messagePart.ContentDisposition.ReadDate);

			DateTime creationDate = new DateTime(2011, 2, 21, 12, 59, 07, DateTimeKind.Utc);
			Assert.AreEqual(creationDate, messagePart.ContentDisposition.CreationDate);

			Assert.AreEqual("Attach2.txt", messagePart.ContentDisposition.FileName);
		}

		[Test]
		public void TestContentTypeCharsetWithLargeFirstChar()
		{
			const string messagePartContent =
				"Content-Type: TEXT/PLAIN; Charset=\"US-ASCII\"\r\n" +
				"\r\n" + // End of message headers
				"foo";

			MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

			Assert.AreEqual(Encoding.ASCII, messagePart.BodyEncoding);
			Assert.AreEqual("foo", messagePart.GetBodyAsText());
		}

        [Test]
        public void TestContentTypeWithMissingSemicolonAndTabs()
        {
            const string messagePartContent =
                "Content-Type: text/plain;\r\n\tcharset=\"Windows-1252\"\r\n\tname=\"ALERTA_1.txt\"\r\n" +
                "\r\n"; // End of message headers

            MessagePart messagePart = new Message(Encoding.ASCII.GetBytes(messagePartContent)).MessagePart;

            Assert.AreEqual(Encoding.GetEncoding(1252), messagePart.BodyEncoding);
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

            const string filename = "test_message_part_save_truncate.testFile";

            FileInfo longTestFile = new FileInfo(filename);
            MessagePart messageLong = new Message(Encoding.ASCII.GetBytes(longMessage)).MessagePart;
            messageLong.Save(longTestFile);
            long longFileSize = longTestFile.Length;

            FileInfo smallTestFile = new FileInfo(filename);
            MessagePart messageSmall = new Message(Encoding.ASCII.GetBytes(smallMessage)).MessagePart;
            messageSmall.Save(smallTestFile);
            long smallFileSize = smallTestFile.Length;

            smallTestFile.Delete();

            Assert.AreNotEqual(longFileSize, smallFileSize);
        }
	}
}