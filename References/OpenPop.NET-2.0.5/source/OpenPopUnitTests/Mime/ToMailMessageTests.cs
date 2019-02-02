using System;
using System.IO;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using NUnit.Framework;
using OpenPop.Mime;

namespace OpenPopUnitTests.Mime
{
	/// <summary>
	/// Tests the ToMailMessage method of the Message class.
	/// </summary>
	[TestFixture]
	public class ToMailMessageTests
	{
		[Test]
		public void ToMailMessageDoesNotReturnNull()
		{
			const string message =
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);
		}

		[Test]
		public void ToMailMessageSimpleHasTextBody()
		{
			const string message =
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);
			Assert.AreEqual("Test", mailMessage.Body);
		}

		[Test]
		public void ToMailMessageSimpleHasTextBodyWithCorrectEncodingISO88591()
		{
			const string message =
				"Content-Type: text/plain; charset=ISO-8859-1\r\n" +
				"\r\n" + // Headers end
				"Test ÆØÅ";

			MailMessage mailMessage = new Message(Encoding.GetEncoding("ISO-8859-1").GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);
			Assert.AreEqual("Test ÆØÅ", mailMessage.Body);
			Assert.AreEqual(Encoding.GetEncoding("ISO-8859-1"), mailMessage.BodyEncoding);
		}

		[Test]
		public void ToMailMessageSimpleHasTextBodyWithCorrectEncodingISO88599()
		{
			const string message =
				"Content-Type: text/plain; charset=ISO-8859-9\r\n" +
				"\r\n" + // Headers end
				"Test øùúûüışÿ";

			MailMessage mailMessage = new Message(Encoding.GetEncoding("ISO-8859-9").GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);
			Assert.AreEqual("Test øùúûüışÿ", mailMessage.Body);
			Assert.AreEqual(Encoding.GetEncoding("ISO-8859-9"), mailMessage.BodyEncoding);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectFrom()
		{
			const string message =
				"From: Test <test@test.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddress from = mailMessage.From;
			Assert.NotNull(from);
			Assert.AreEqual("Test", from.DisplayName);
			Assert.AreEqual("test@test.com", from.Address);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectFromWhenARFCMailAddressDoesNotHaveValidMailAddress()
		{
			const string message =
				"From: Test \r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			Assert.IsNull(mailMessage.From);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectReplyTo()
		{
			const string message =
				"Reply-To: Test <test@test.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddress replyTo = mailMessage.ReplyTo;
			Assert.NotNull(replyTo);
			Assert.AreEqual("Test", replyTo.DisplayName);
			Assert.AreEqual("test@test.com", replyTo.Address);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectReplyToWhenARFCMailAddressDoesNotHaveValidMailAddress()
		{
			const string message =
				"Reply-To: Test \r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			Assert.IsNull(mailMessage.ReplyTo);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectTo()
		{
			const string message =
				"To: Test <test@test.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection to = mailMessage.To;
			Assert.NotNull(to);
			Assert.AreEqual(1, to.Count);
			Assert.AreEqual("Test", to[0].DisplayName);
			Assert.AreEqual("test@test.com", to[0].Address);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectMultipleTo()
		{
			const string message =
				"To: Test <test@test.com>, Foo <foo@bar.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection to = mailMessage.To;
			Assert.NotNull(to);
			Assert.AreEqual(2, to.Count);

			Assert.AreEqual("Test", to[0].DisplayName);
			Assert.AreEqual("test@test.com", to[0].Address);

			Assert.AreEqual("Foo", to[1].DisplayName);
			Assert.AreEqual("foo@bar.com", to[1].Address);
		}

		[Test]
		public void ToMailMessageSimpleDoesNotThrowExceptionWhenAToRFCMailAddressDoesNotHaveValidMailAddress()
		{
			const string message =
				"To: Test no email\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection to = mailMessage.To;
			Assert.NotNull(to);

			// The email address cannot be parsed to MailMessage - therefore it cannot be added to the MailMessage
			Assert.IsEmpty(to);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectCc()
		{
			const string message =
				"CC: Test <test@test.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection cc = mailMessage.CC;
			Assert.NotNull(cc);
			Assert.AreEqual(1, cc.Count);
			Assert.AreEqual("Test", cc[0].DisplayName);
			Assert.AreEqual("test@test.com", cc[0].Address);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectMultipleCc()
		{
			const string message =
				"CC: Test <test@test.com>, Foo <foo@bar.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection cc = mailMessage.CC;
			Assert.NotNull(cc);
			Assert.AreEqual(2, cc.Count);

			Assert.AreEqual("Test", cc[0].DisplayName);
			Assert.AreEqual("test@test.com", cc[0].Address);

			Assert.AreEqual("Foo", cc[1].DisplayName);
			Assert.AreEqual("foo@bar.com", cc[1].Address);
		}

		[Test]
		public void ToMailMessageSimpleDoesNotThrowExceptionWhenACcRFCMailAddressDoesNotHaveValidMailAddress()
		{
			const string message =
				"CC: Test no email\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection cc = mailMessage.CC;
			Assert.NotNull(cc);

			// The email address cannot be parsed to MailMessage - therefore it cannot be added to the MailMessage
			Assert.IsEmpty(cc);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectBcc()
		{
			const string message =
				"BCC: Test <test@test.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection bcc = mailMessage.Bcc;
			Assert.NotNull(bcc);
			Assert.AreEqual(1, bcc.Count);
			Assert.AreEqual("Test", bcc[0].DisplayName);
			Assert.AreEqual("test@test.com", bcc[0].Address);
		}

		[Test]
		public void ToMailMessageSimpleHasCorrectMultipleBcc()
		{
			const string message =
				"BCC: Test <test@test.com>, Foo <foo@bar.com>\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection bcc = mailMessage.Bcc;
			Assert.NotNull(bcc);
			Assert.AreEqual(2, bcc.Count);

			Assert.AreEqual("Test", bcc[0].DisplayName);
			Assert.AreEqual("test@test.com", bcc[0].Address);

			Assert.AreEqual("Foo", bcc[1].DisplayName);
			Assert.AreEqual("foo@bar.com", bcc[1].Address);
		}

		[Test]
		public void ToMailMessageSimpleDoesNotThrowExceptionWhenABccRFCMailAddressDoesNotHaveValidMailAddress()
		{
			const string message =
				"CC: Test no email\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			MailAddressCollection bcc = mailMessage.Bcc;
			Assert.NotNull(bcc);

			// The email address cannot be parsed to MailMessage - therefore it cannot be added to the MailMessage
			Assert.IsEmpty(bcc);
		}

		[Test]
		public void ToMailMessageHasSubject()
		{
			const string message =
				"Subject: Testing\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			string subject = mailMessage.Subject;
			Assert.NotNull(subject);
			Assert.AreEqual("Testing", subject);
		}

		[Test]
		public void ToMailMessageHasSubjectWithIso88591Encoding()
		{
			const string message =
				"Subject: Test =?ISO-8859-1?Q?=E6=F8=E5=C6=D8=C5?=\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			string subject = mailMessage.Subject;
			Assert.NotNull(subject);
			Assert.AreEqual("Test æøåÆØÅ", subject);
		}

		[Test]
		public void ToMailMessageHasSubjectWithIso88599Encoding()
		{
			const string message =
				"Subject: Test =?ISO-8859-9?Q?=FE?=\r\n" +
				"\r\n" + // Headers end
				"Test";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(message)).ToMailMessage();

			Assert.NotNull(mailMessage);

			string subject = mailMessage.Subject;
			Assert.NotNull(subject);
			Assert.AreEqual("Test ş", subject);
		}

		/// <summary>
		/// See http://en.wikipedia.org/wiki/MIME#Multipart_messages for example
		/// </summary>
		[Test]
		public void ToMailMessageMultiPartMessage()
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

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(multipartMessage)).ToMailMessage();

			Assert.NotNull(mailMessage);

			// Check body
			Assert.NotNull(mailMessage.Body);
			Assert.AreEqual("This is the body of the message.", mailMessage.Body);
			Assert.AreEqual(Encoding.ASCII, mailMessage.BodyEncoding);
			Assert.IsFalse(mailMessage.IsBodyHtml);

			// Check no alternative view
			Assert.IsEmpty(mailMessage.AlternateViews);

			// Check attachments
			Assert.NotNull(mailMessage.Attachments);
			Assert.AreEqual(1, mailMessage.Attachments.Count);
			Attachment firstAttachment = mailMessage.Attachments[0];
			Assert.AreEqual("application/octet-stream", firstAttachment.ContentType.MediaType);
			Assert.AreEqual(TransferEncoding.Base64, firstAttachment.TransferEncoding);
			Assert.NotNull(firstAttachment.ContentStream);

			byte[] expectedBytes = Convert.FromBase64String(
				"PGh0bWw+CiAgPGhlYWQ+CiAgPC9oZWFkPgogIDxib2R5PgogICAgPHA+VGhpcyBpcyB0aGUg\r\n" +
				"Ym9keSBvZiB0aGUgbWVzc2FnZS48L3A+CiAgPC9ib2R5Pgo8L2h0bWw+Cg==");

			// Read the bytes from the attachment to see if they are equal
			byte[] actualBytes = getAttachmentBytes(firstAttachment);

			// Check if the bytes are equal
			Assert.AreEqual(expectedBytes, actualBytes);
		}

		/// <summary>
		/// Test that the attachments stored in the <see cref="MailMessage"/> has the same Content-ID as the original
		/// attachment. This is to ensure that pictures referred to from aHTMLl message is correctly referred to in the
		///<see cref="MailMessage"/>.
		/// </summary>
		[Test]
		public void ToMailMessageCheckSameContentID()
		{
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-ID: test\r\n" +
				"Content-Type: application/pdf\r\n" +
				"\r\n" +
				"Test if attachment has same ID in MailMessage";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(multipartMessage)).ToMailMessage();

			Assert.NotNull(mailMessage);

			// Check no body or alternative views
			Assert.IsNullOrEmpty(mailMessage.Body);
			Assert.IsEmpty(mailMessage.AlternateViews);

			// Check attachment
			Assert.AreEqual(1, mailMessage.Attachments.Count);
			Attachment firstAttachment = mailMessage.Attachments[0];
			Assert.AreEqual("test", firstAttachment.ContentId);
			Assert.AreEqual(Encoding.ASCII.GetBytes("Test if attachment has same ID in MailMessage"), getAttachmentBytes(firstAttachment));
		}

		[Test]
		public void ToMailMessageHasCorrectContentType()
		{
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-ID: test\r\n" +
				"Content-Type: application/pdf\r\n" +
				"\r\n" +
				"Test if attachment has same ID in MailMessage";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(multipartMessage)).ToMailMessage();

			Assert.NotNull(mailMessage);

			// Check no body or alternative views
			Assert.IsNullOrEmpty(mailMessage.Body);
			Assert.IsEmpty(mailMessage.AlternateViews);

			// Check attachment
			Assert.AreEqual(1, mailMessage.Attachments.Count);

			Attachment firstAttachment = mailMessage.Attachments[0];
			Assert.NotNull(firstAttachment.ContentType);
			Assert.NotNull(firstAttachment.ContentType.MediaType);
			Assert.AreEqual("application/pdf", firstAttachment.ContentType.MediaType);
		}

		private static byte[] getAttachmentBytes(AttachmentBase attachment)
		{
			MemoryStream actual = new MemoryStream();
			Stream stream = attachment.ContentStream;
			while (true)
			{
				int outByte = stream.ReadByte();
				if (outByte != -1)
					actual.Write(new[] { (byte)outByte }, 0, 1);
				else
					break;
			}
			return actual.ToArray();
		}

		[Test]
		public void ToMailMessageMultipleBodiesWithHtmlContent()
		{
			const string expectedPlain = "This is the body of the message - in plain text.";
			const string expectedHtml = "This is some <b>HTML</b> :)";
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-Type: multipart/alternative; boundary=\"frontier\"\r\n" +
				"\r\n" +
				"This is a message with multiple parts in MIME format.\r\n" +
				"--frontier\r\n" +
				"Content-Type: text/plain\r\n" +
				"\r\n" +
				expectedPlain +
				"\r\n" +
				"--frontier\r\n" +
				"Content-Type: text/html\r\n" +
				"\r\n" +
				expectedHtml +
				"\r\n" +
				"--frontier--";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(multipartMessage)).ToMailMessage();

			Assert.NotNull(mailMessage);

			// Check body for html
			Assert.NotNull(mailMessage.Body);
			Assert.AreEqual(expectedHtml, mailMessage.Body);
			Assert.AreEqual(Encoding.ASCII, mailMessage.BodyEncoding);
			Assert.IsTrue(mailMessage.IsBodyHtml);

			// Check alternative view for plain text
			Assert.IsNotEmpty(mailMessage.AlternateViews);
			Assert.AreEqual(1, mailMessage.AlternateViews.Count);

			AlternateView firstAlternative = mailMessage.AlternateViews[0];
			Assert.NotNull(firstAlternative.ContentType);
			Assert.NotNull(firstAlternative.ContentType.MediaType);
			Assert.AreEqual("text/plain", firstAlternative.ContentType.MediaType);
			Assert.AreEqual(expectedPlain, Encoding.ASCII.GetString(getAttachmentBytes(firstAlternative)));

			// Check attachments
			Assert.IsEmpty(mailMessage.Attachments);
		}

		[Test]
		public void ToMailMessageMultipleBodiesWithCorrectContentID()
		{
			const string multipartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-Type: multipart/alternative; boundary=\"frontier\"\r\n" +
				"Content-ID: 1\r\n" +
				"\r\n" +
				"--frontier\r\n" +
				"Content-Type: text/plain\r\n" +
				"\r\n" +
				"Body 1\r\n" +
				"--frontier\r\n" +
				"Content-Type: text/xml\r\n" +
				"Content-ID: 2\r\n" +
				"\r\n" +
				"<content>Body 2</content>\r\n" +
				"--frontier--";

			MailMessage mailMessage = new Message(Encoding.ASCII.GetBytes(multipartMessage)).ToMailMessage();

			Assert.NotNull(mailMessage);

			// Check body
			// Notice that the body does not have a content-id property
			Assert.NotNull(mailMessage.Body);
			Assert.AreEqual("Body 1", mailMessage.Body);
			Assert.AreEqual(Encoding.ASCII, mailMessage.BodyEncoding);
			Assert.IsFalse(mailMessage.IsBodyHtml);

			// Check html alternative view
			Assert.IsNotEmpty(mailMessage.AlternateViews);
			Assert.AreEqual(1, mailMessage.AlternateViews.Count);

			AlternateView firstAlternative = mailMessage.AlternateViews[0];
			Assert.NotNull(firstAlternative.ContentType);
			Assert.NotNull(firstAlternative.ContentType.MediaType);
			Assert.AreEqual("text/xml", firstAlternative.ContentType.MediaType);
			Assert.AreEqual("<content>Body 2</content>", Encoding.ASCII.GetString(getAttachmentBytes(firstAlternative)));
			Assert.AreEqual("2", mailMessage.AlternateViews[0].ContentId);

			// Check attachments
			Assert.IsEmpty(mailMessage.Attachments);
		}

		[Test]
		public void TestToMailMessageDoesNotOverrideBodyWhenTwoPlainTextVersion()
		{
			const string multiPartMessage =
				"MIME-Version: 1.0\r\n" +
				"Content-Type: multipart/mixed;\r\n" +
				"\t\t\t\t\t\t boundary=unique-boundary-1\r\n" +
				"\r\n" +
				"This is the preamble area of a multipart message.\r\n" +
				"--unique-boundary-1\r\n" +
				"\r\n" +
				" ... Some text appears here ...\r\n" +
				"--unique-boundary-1\r\n" +
				"Content-type: text/plain; charset=US-ASCII\r\n" +
				"\r\n" +
				"This could have been part of the previous part, but\r\n" +
				"illustrates explicit versus implicit typing of body\r\n" +
				"parts.\r\n" +
				"--unique-boundary-1--\r\n";

			Message message = new Message(Encoding.ASCII.GetBytes(multiPartMessage));
			MailMessage mailMessage = message.ToMailMessage();

			Assert.NotNull(mailMessage);

			// Get the first plain text message part
			MessagePart firstPlainText = message.MessagePart.MessageParts[0];
			Assert.AreEqual(firstPlainText.GetBodyAsText(), mailMessage.Body);

			// Get the scond plain text message part
			MessagePart secondPlainText = message.MessagePart.MessageParts[1];

			// The second plain text version should be in the alternative views collection
			Assert.AreEqual(1, mailMessage.AlternateViews.Count);
			Assert.AreEqual(secondPlainText.GetBodyAsText(), Encoding.ASCII.GetString(getAttachmentBytes(mailMessage.AlternateViews[0])));
		}

		[Test]
		public void TestSenderNotIncluded()
		{
			const string messageString =
				"Content-Type: text/plain; \r\n" +
				"\r\n" +
				"Testing";

			MailMessage message = new Message(Encoding.ASCII.GetBytes(messageString)).ToMailMessage();

			Assert.IsNull(message.Sender);
		}

		[Test]
		public void TestSender()
		{
			const string messageString =
				"Content-Type: text/plain; \r\n" +
				"Sender: Secretary <secretary@example.com>\r\n" +
				"\r\n" +
				"Testing";

			MailMessage message = new Message(Encoding.ASCII.GetBytes(messageString)).ToMailMessage();

			Assert.IsNotNull(message.Sender);
			Assert.AreEqual("Secretary", message.Sender.DisplayName);
			Assert.AreEqual("secretary@example.com", message.Sender.Address);
		}

		[Test]
		public void TestSenderInvalidMailAddress()
		{
			const string messageString =
				"Content-Type: text/plain; \r\n" +
				"Sender: Secretary\r\n" +
				"\r\n" +
				"Testing";

			MailMessage message = new Message(Encoding.ASCII.GetBytes(messageString)).ToMailMessage();

			// Unable to parse to MailAddress from RfcMailAddress
			// since no address is specified in the header
			Assert.IsNull(message.Sender);
		}
	}
}