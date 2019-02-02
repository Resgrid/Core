using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mail;
using System.Text;
using OpenPop.Mime.Header;
using OpenPop.Mime.Traverse;

namespace OpenPop.Mime
{
	/// <summary>
	/// This is the root of the email tree structure.<br/>
	/// <see cref="Mime.MessagePart"/> for a description about the structure.<br/>
	/// <br/>
	/// A Message (this class) contains the headers of an email message such as:
	/// <code>
	///  - To
	///  - From
	///  - Subject
	///  - Content-Type
	///  - Message-ID
	/// </code>
	/// which are located in the <see cref="Headers"/> property.<br/>
	/// <br/>
	/// Use the <see cref="Message.MessagePart"/> property to find the actual content of the email message.
	/// </summary>
	/// <example>
	/// Examples are available on the <a href="http://hpop.sourceforge.net/">project homepage</a>.
	/// </example>
	public class Message
	{
		#region Public properties
		/// <summary>
		/// Headers of the Message.
		/// </summary>
		public MessageHeader Headers { get; private set; }

		/// <summary>
		/// This is the body of the email Message.<br/>
		/// <br/>
		/// If the body was parsed for this Message, this property will never be <see langword="null"/>.
		/// </summary>
		public MessagePart MessagePart { get; private set; }

		/// <summary>
		/// The raw content from which this message has been constructed.<br/>
		/// These bytes can be persisted and later used to recreate the Message.
		/// </summary>
		public byte[] RawMessage { get; private set; }
		#endregion

		#region Constructors
		/// <summary>
		/// Convenience constructor for <see cref="Mime.Message(byte[], bool)"/>.<br/>
		/// <br/>
		/// Creates a message from a byte array. The full message including its body is parsed.
		/// </summary>
		/// <param name="rawMessageContent">The byte array which is the message contents to parse</param>
		public Message(byte[] rawMessageContent)
			: this(rawMessageContent, true)
		{
		}

		/// <summary>
		/// Constructs a message from a byte array.<br/>
		/// <br/>
		/// The headers are always parsed, but if <paramref name="parseBody"/> is <see langword="false"/>, the body is not parsed.
		/// </summary>
		/// <param name="rawMessageContent">The byte array which is the message contents to parse</param>
		/// <param name="parseBody">
		/// <see langword="true"/> if the body should be parsed,
		/// <see langword="false"/> if only headers should be parsed out of the <paramref name="rawMessageContent"/> byte array
		/// </param>
		public Message(byte[] rawMessageContent, bool parseBody)
		{
			RawMessage = rawMessageContent;

			// Find the headers and the body parts of the byte array
			MessageHeader headersTemp;
			byte[] body;
			HeaderExtractor.ExtractHeadersAndBody(rawMessageContent, out headersTemp, out body);

			// Set the Headers property
			Headers = headersTemp;

			// Should we also parse the body?
			if (parseBody)
			{
				// Parse the body into a MessagePart
				MessagePart = new MessagePart(body, Headers);
			}
		}
		#endregion

		/// <summary>
		/// This method will convert this <see cref="Message"/> into a <see cref="MailMessage"/> equivalent.<br/>
		/// The returned <see cref="MailMessage"/> can be used with <see cref="System.Net.Mail.SmtpClient"/> to forward the email.<br/>
		/// <br/>
		/// You should be aware of the following about this method:
		/// <list type="bullet">
		/// <item>
		///    All sender and receiver mail addresses are set.
		///    If you send this email using a <see cref="System.Net.Mail.SmtpClient"/> then all
		///    receivers in To, From, Cc and Bcc will receive the email once again.
		/// </item>
		/// <item>
		///    If you view the source code of this Message and looks at the source code of the forwarded
		///    <see cref="MailMessage"/> returned by this method, you will notice that the source codes are not the same.
		///    The content that is presented by a mail client reading the forwarded <see cref="MailMessage"/> should be the
		///    same as the original, though.
		/// </item>
		/// <item>
		///    Content-Disposition headers will not be copied to the <see cref="MailMessage"/>.
		///    It is simply not possible to set these on Attachments.
		/// </item>
		/// <item>
		///    HTML content will be treated as the preferred view for the <see cref="MailMessage.Body"/>. Plain text content will be used for the
		///    <see cref="MailMessage.Body"/> when HTML is not available.
		/// </item>
		/// </list>
		/// </summary>
		/// <returns>A <see cref="MailMessage"/> object that contains the same information that this Message does</returns>
		public MailMessage ToMailMessage()
		{
			// Construct an empty MailMessage to which we will gradually build up to look like the current Message object (this)
			MailMessage message = new MailMessage();

			message.Subject = Headers.Subject;

			// We here set the encoding to be UTF-8
			// We cannot determine what the encoding of the subject was at this point.
			// But since we know that strings in .NET is stored in UTF, we can
			// use UTF-8 to decode the subject into bytes
			message.SubjectEncoding = Encoding.UTF8;
			
			// The HTML version should take precedent over the plain text if it is available
			MessagePart preferredVersion = FindFirstHtmlVersion();
			if ( preferredVersion != null )
			{
				// Make sure that the IsBodyHtml property is being set correctly for our content
				message.IsBodyHtml = true;
			}
			else
			{
				// otherwise use the first plain text version as the body, if it exists
				preferredVersion = FindFirstPlainTextVersion();
			}

			if (preferredVersion != null)
			{
				message.Body = preferredVersion.GetBodyAsText();
				message.BodyEncoding = preferredVersion.BodyEncoding;
			}

			// Add body and alternative views (html and such) to the message
			IEnumerable<MessagePart> textVersions = FindAllTextVersions();
			foreach (MessagePart textVersion in textVersions)
			{
				// The textVersions also contain the preferred version, therefore
				// we should skip that one
				if (textVersion == preferredVersion)
					continue;

				MemoryStream stream = new MemoryStream(textVersion.Body);
				AlternateView alternative = new AlternateView(stream);
				alternative.ContentId = textVersion.ContentId;
				alternative.ContentType = textVersion.ContentType;
				message.AlternateViews.Add(alternative);
			}

			// Add attachments to the message
			IEnumerable<MessagePart> attachments = FindAllAttachments();
			foreach (MessagePart attachmentMessagePart in attachments)
			{
				MemoryStream stream = new MemoryStream(attachmentMessagePart.Body);
				Attachment attachment = new Attachment(stream, attachmentMessagePart.ContentType);
				attachment.ContentId = attachmentMessagePart.ContentId;
				message.Attachments.Add(attachment);
			}

			if(Headers.From != null && Headers.From.HasValidMailAddress)
				message.From = Headers.From.MailAddress;

			if (Headers.ReplyTo != null && Headers.ReplyTo.HasValidMailAddress)
				message.ReplyTo = Headers.ReplyTo.MailAddress;

			if(Headers.Sender != null && Headers.Sender.HasValidMailAddress)
				message.Sender = Headers.Sender.MailAddress;

			foreach (RfcMailAddress to in Headers.To)
			{
				if(to.HasValidMailAddress)
					message.To.Add(to.MailAddress);
			}

			foreach (RfcMailAddress cc in Headers.Cc)
			{
				if (cc.HasValidMailAddress)
					message.CC.Add(cc.MailAddress);
			}

			foreach (RfcMailAddress bcc in Headers.Bcc)
			{
				if (bcc.HasValidMailAddress)
					message.Bcc.Add(bcc.MailAddress);
			}

			return message;
		}

		#region MessagePart Searching Methods

		/// <summary>
		/// Finds the first text/plain <see cref="MessagePart"/> in this message.<br/>
		/// This is a convenience method - it simply propagates the call to <see cref="FindFirstMessagePartWithMediaType"/>.<br/>
		/// <br/>
		/// If no text/plain version is found, <see langword="null"/> is returned.
		/// </summary>
		/// <returns>
		/// <see cref="MessagePart"/> which has a MediaType of text/plain or <see langword="null"/>
		/// if such <see cref="MessagePart"/> could not be found.
		/// </returns>
		public MessagePart FindFirstPlainTextVersion()
		{
			return FindFirstMessagePartWithMediaType("text/plain");
		}

		/// <summary>
		/// Finds the first text/html <see cref="MessagePart"/> in this message.<br/>
		/// This is a convenience method - it simply propagates the call to <see cref="FindFirstMessagePartWithMediaType"/>.<br/>
		/// <br/>
		/// If no text/html version is found, <see langword="null"/> is returned.
		/// </summary>
		/// <returns>
		/// <see cref="MessagePart"/> which has a MediaType of text/html or <see langword="null"/>
		/// if such <see cref="MessagePart"/> could not be found.
		/// </returns>
		public MessagePart FindFirstHtmlVersion()
		{
			return FindFirstMessagePartWithMediaType("text/html");
		}

		/// <summary>
		/// Finds all the <see cref="MessagePart"/>'s which contains a text version.<br/>
		/// <br/>
		/// <see cref="Mime.MessagePart.IsText"/> for MessageParts which are considered to be text versions.<br/>
		/// <br/>
		/// Examples of MessageParts media types are:
		/// <list type="bullet">
		///    <item>text/plain</item>
		///    <item>text/html</item>
		///    <item>text/xml</item>
		/// </list>
		/// </summary>
		/// <returns>A List of MessageParts where each part is a text version</returns>
		public List<MessagePart> FindAllTextVersions()
		{
			return new TextVersionFinder().VisitMessage(this);
		}

		/// <summary>
		/// Finds all the <see cref="MessagePart"/>'s which are attachments to this message.<br/>
		/// <br/>
		/// <see cref="Mime.MessagePart.IsAttachment"/> for MessageParts which are considered to be attachments.
		/// </summary>
		/// <returns>A List of MessageParts where each is considered an attachment</returns>
		public List<MessagePart> FindAllAttachments()
		{
			return new AttachmentFinder().VisitMessage(this);
		}

		/// <summary>
		/// Finds the first <see cref="MessagePart"/> in the <see cref="Message"/> hierarchy with the given MediaType.<br/>
		/// <br/>
		/// The search in the hierarchy is a depth-first traversal.
		/// </summary>
		/// <param name="mediaType">The MediaType to search for. Case is ignored.</param>
		/// <returns>
		/// A <see cref="MessagePart"/> with the given MediaType or <see langword="null"/> if no such <see cref="MessagePart"/> was found
		/// </returns>
		public MessagePart FindFirstMessagePartWithMediaType(string mediaType)
		{
			return new FindFirstMessagePartWithMediaType().VisitMessage(this, mediaType);
		}

		/// <summary>
		/// Finds all the <see cref="MessagePart"/>s in the <see cref="Message"/> hierarchy with the given MediaType.
		/// </summary>
		/// <param name="mediaType">The MediaType to search for. Case is ignored.</param>
		/// <returns>
		/// A List of <see cref="MessagePart"/>s with the given MediaType.<br/>
		/// The List might be empty if no such <see cref="MessagePart"/>s were found.<br/>
		/// The order of the elements in the list is the order which they are found using
		/// a depth first traversal of the <see cref="Message"/> hierarchy.
		/// </returns>
		public List<MessagePart> FindAllMessagePartsWithMediaType(string mediaType)
		{
			return new FindAllMessagePartsWithMediaType().VisitMessage(this, mediaType);
		}

		#endregion

		#region Message Persistence

		/// <summary>
		/// Save this <see cref="Message"/> to a file.<br/>
		/// <br/>
		/// Can be loaded at a later time using the <see cref="Load(FileInfo)"/> method.
		/// </summary>
		/// <param name="file">The File location to save the <see cref="Message"/> to. Existent files will be overwritten.</param>
		/// <exception cref="ArgumentNullException">If <paramref name="file"/> is <see langword="null"/></exception>
		/// <exception>Other exceptions relevant to using a <see cref="FileStream"/> might be thrown as well</exception>
		public void Save(FileInfo file)
		{
			if (file == null)
				throw new ArgumentNullException("file");

			using (FileStream stream = new FileStream(file.FullName, FileMode.Create))
			{
				Save(stream);
			}
		}

		/// <summary>
		/// Save this <see cref="Message"/> to a stream.<br/>
		/// </summary>
		/// <param name="messageStream">The stream to write to</param>
		/// <exception cref="ArgumentNullException">If <paramref name="messageStream"/> is <see langword="null"/></exception>
		/// <exception>Other exceptions relevant to <see cref="Stream.Write"/> might be thrown as well</exception>
		public void Save(Stream messageStream)
		{
			if (messageStream == null)
				throw new ArgumentNullException("messageStream");

			messageStream.Write(RawMessage, 0, RawMessage.Length);
		}

		/// <summary>
		/// Loads a <see cref="Message"/> from a file containing a raw email.
		/// </summary>
		/// <param name="file">The File location to load the <see cref="Message"/> from. The file must exist.</param>
		/// <exception cref="ArgumentNullException">If <paramref name="file"/> is <see langword="null"/></exception>
		/// <exception cref="FileNotFoundException">If <paramref name="file"/> does not exist</exception>
		/// <exception>Other exceptions relevant to a <see cref="FileStream"/> might be thrown as well</exception>
		/// <returns>A <see cref="Message"/> with the content loaded from the <paramref name="file"/></returns>
		public static Message Load(FileInfo file)
		{
			if (file == null)
				throw new ArgumentNullException("file");

			if (!file.Exists)
				throw new FileNotFoundException("Cannot load message from non-existent file", file.FullName);

			using (FileStream stream = new FileStream(file.FullName, FileMode.Open))
			{
				return Load(stream);
			}
		}


		/// <summary>
		/// Loads a <see cref="Message"/> from a <see cref="Stream"/> containing a raw email.
		/// </summary>
		/// <param name="messageStream">The <see cref="Stream"/> from which to load the raw <see cref="Message"/></param>
		/// <exception cref="ArgumentNullException">If <paramref name="messageStream"/> is <see langword="null"/></exception>
		/// <exception>Other exceptions relevant to <see cref="Stream.Read"/> might be thrown as well</exception>
		/// <returns>A <see cref="Message"/> with the content loaded from the <paramref name="messageStream"/></returns>
		public static Message Load(Stream messageStream)
		{
			if (messageStream == null)
				throw new ArgumentNullException("messageStream");

			using (MemoryStream outStream = new MemoryStream())
			{
#if DOTNET4
				// TODO: Enable using native v4 framework methods when support is formally added.
				messageStream.CopyTo(outStream);
#else
				int bytesRead;
				byte[] buffer = new byte[4096];

				while ((bytesRead = messageStream.Read(buffer, 0, 4096)) > 0)
				{
					outStream.Write(buffer, 0, bytesRead);
				}
#endif
				byte[] content = outStream.ToArray();

				return new Message(content);
			}
		}
		#endregion
	}
}
