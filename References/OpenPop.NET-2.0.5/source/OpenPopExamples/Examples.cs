using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using OpenPop.Common.Logging;
using OpenPop.Mime;
using OpenPop.Mime.Decode;
using OpenPop.Mime.Header;
using OpenPop.Pop3;

namespace OpenPopExamples
{
	/// <summary>
	/// These are small examples problems for the
	/// <see cref="OpenPop"/>.NET POP3 library
	/// </summary>
	public class Examples
	{
		/// <summary>
		/// Example showing:
		///  - how to fetch all messages from a POP3 server
		/// </summary>
		/// <param name="hostname">Hostname of the server. For example: pop3.live.com</param>
		/// <param name="port">Host port to connect to. Normally: 110 for plain POP3, 995 for SSL POP3</param>
		/// <param name="useSsl">Whether or not to use SSL to connect to server</param>
		/// <param name="username">Username of the user on the server</param>
		/// <param name="password">Password of the user on the server</param>
		/// <returns>All Messages on the POP3 server</returns>
		public static List<Message> FetchAllMessages(string hostname, int port, bool useSsl, string username, string password)
		{
			// The client disconnects from the server when being disposed
			using(Pop3Client client = new Pop3Client())
			{
				// Connect to the server
				client.Connect(hostname, port, useSsl);

				// Authenticate ourselves towards the server
				client.Authenticate(username, password);

				// Get the number of messages in the inbox
				int messageCount = client.GetMessageCount();

				// We want to download all messages
				List<Message> allMessages = new List<Message>(messageCount);

				// Messages are numbered in the interval: [1, messageCount]
				// Ergo: message numbers are 1-based.
				// Most servers give the latest message the highest number
				for (int i = messageCount; i > 0; i--)
				{
					allMessages.Add(client.GetMessage(i));
				}

				// Now return the fetched messages
				return allMessages;
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to delete fetch an emails headers only
		///  - how to delete a message from the server
		/// </summary>
		/// <param name="client">A connected and authenticated Pop3Client from which to delete a message</param>
		/// <param name="messageId">A message ID of a message on the POP3 server. Is located in <see cref="MessageHeader.MessageId"/></param>
		/// <returns><see langword="true"/> if message was deleted, <see langword="false"/> otherwise</returns>
		public bool DeleteMessageByMessageId(Pop3Client client, string messageId)
		{
			// Get the number of messages on the POP3 server
			int messageCount = client.GetMessageCount();

			// Run trough each of these messages and download the headers
			for (int messageItem = messageCount; messageItem > 0; messageItem--)
			{
				// If the Message ID of the current message is the same as the parameter given, delete that message
				if (client.GetMessageHeaders(messageItem).MessageId == messageId)
				{
					// Delete
					client.DeleteMessage(messageItem);
					return true;
				}
			}

			// We did not find any message with the given messageId, report this back
			return false;
		}

		/// <summary>
		/// Example showing:
		///  - how to a find plain text version in a Message
		///  - how to save MessageParts to file
		/// </summary>
		/// <param name="message">The message to examine for plain text</param>
		public static void FindPlainTextInMessage(Message message)
		{
			MessagePart plainText = message.FindFirstPlainTextVersion();
			if(plainText != null)
			{
				// Save the plain text to a file, database or anything you like
				plainText.Save(new FileInfo("plainText.txt"));
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to find a html version in a Message
		///  - how to save MessageParts to file
		/// </summary>
		/// <param name="message">The message to examine for html</param>
		public static void FindHtmlInMessage(Message message)
		{
			MessagePart html = message.FindFirstHtmlVersion();
			if (html != null)
			{
				// Save the plain text to a file, database or anything you like
				html.Save(new FileInfo("html.txt"));
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to find a MessagePart with a specified MediaType
		///  - how to get the body of a MessagePart as a string
		/// </summary>
		/// <param name="message">The message to examine for xml</param>
		public static void FindXmlInMessage(Message message)
		{
			MessagePart xml = message.FindFirstMessagePartWithMediaType("text/xml");
			if (xml != null)
			{
				// Get out the XML string from the email
				string xmlString = xml.GetBodyAsText();

				System.Xml.XmlDocument doc = new System.Xml.XmlDocument();

				// Load in the XML read from the email
				doc.LoadXml(xmlString);

				// Save the xml to the filesystem
				doc.Save("test.xml");
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to fetch only headers from a POP3 server
		///  - how to examine some of the headers
		///  - how to fetch a full message
		///  - how to find a specific attachment and save it to a file
		/// </summary>
		/// <param name="hostname">Hostname of the server. For example: pop3.live.com</param>
		/// <param name="port">Host port to connect to. Normally: 110 for plain POP3, 995 for SSL POP3</param>
		/// <param name="useSsl">Whether or not to use SSL to connect to server</param>
		/// <param name="username">Username of the user on the server</param>
		/// <param name="password">Password of the user on the server</param>
		/// <param name="messageNumber">
		/// The number of the message to examine.
		/// Must be in range [1, messageCount] where messageCount is the number of messages on the server.
		/// </param>
		public static void HeadersFromAndSubject(string hostname, int port, bool useSsl, string username, string password, int messageNumber)
		{
			// The client disconnects from the server when being disposed
			using (Pop3Client client = new Pop3Client())
			{
				// Connect to the server
				client.Connect(hostname, port, useSsl);

				// Authenticate ourselves towards the server
				client.Authenticate(username, password);

				// We want to check the headers of the message before we download
				// the full message
				MessageHeader headers = client.GetMessageHeaders(messageNumber);

				RfcMailAddress from = headers.From;
				string subject = headers.Subject;

				// Only want to download message if:
				//  - is from test@xample.com
				//  - has subject "Some subject"
				if(from.HasValidMailAddress && from.Address.Equals("test@example.com") && "Some subject".Equals(subject))
				{
					// Download the full message
					Message message = client.GetMessage(messageNumber);

					// We know the message contains an attachment with the name "useful.pdf".
					// We want to save this to a file with the same name
					foreach (MessagePart attachment in message.FindAllAttachments())
					{
						if(attachment.FileName.Equals("useful.pdf"))
						{
							// Save the raw bytes to a file
							File.WriteAllBytes(attachment.FileName, attachment.Body);
						}
					}
				}
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to delete a specific message from a server
		/// </summary>
		/// <param name="hostname">Hostname of the server. For example: pop3.live.com</param>
		/// <param name="port">Host port to connect to. Normally: 110 for plain POP3, 995 for SSL POP3</param>
		/// <param name="useSsl">Whether or not to use SSL to connect to server</param>
		/// <param name="username">Username of the user on the server</param>
		/// <param name="password">Password of the user on the server</param>
		/// <param name="messageNumber">
		/// The number of the message to delete.
		/// Must be in range [1, messageCount] where messageCount is the number of messages on the server.
		/// </param>
		public static void DeleteMessageOnServer(string hostname, int port, bool useSsl, string username, string password, int messageNumber)
		{
			// The client disconnects from the server when being disposed
			using (Pop3Client client = new Pop3Client())
			{
				// Connect to the server
				client.Connect(hostname, port, useSsl);

				// Authenticate ourselves towards the server
				client.Authenticate(username, password);

				// Mark the message as deleted
				// Notice that it is only MARKED as deleted
				// POP3 requires you to "commit" the changes
				// which is done by sending a QUIT command to the server
				// You can also reset all marked messages, by sending a RSET command.
				client.DeleteMessage(messageNumber);

				// When a QUIT command is sent to the server, the connection between them are closed.
				// When the client is disposed, the QUIT command will be sent to the server
				// just as if you had called the Disconnect method yourself.
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to use UID's (unique ID's) of messages from the POP3 server
		///  - how to download messages not seen before
		///    (notice that the POP3 protocol cannot see if a message has been read on the server
		///     before. Therefore the client need to maintain this state for itself)
		/// </summary>
		/// <param name="hostname">Hostname of the server. For example: pop3.live.com</param>
		/// <param name="port">Host port to connect to. Normally: 110 for plain POP3, 995 for SSL POP3</param>
		/// <param name="useSsl">Whether or not to use SSL to connect to server</param>
		/// <param name="username">Username of the user on the server</param>
		/// <param name="password">Password of the user on the server</param>
		/// <param name="seenUids">
		/// List of UID's of all messages seen before.
		/// New message UID's will be added to the list.
		/// Consider using a HashSet if you are using >= 3.5 .NET
		/// </param>
		/// <returns>A List of new Messages on the server</returns>
		public static List<Message> FetchUnseenMessages(string hostname, int port, bool useSsl, string username, string password, List<string> seenUids)
		{
			// The client disconnects from the server when being disposed
			using(Pop3Client client = new Pop3Client())
			{
				// Connect to the server
				client.Connect(hostname, port, useSsl);

				// Authenticate ourselves towards the server
				client.Authenticate(username, password);

				// Fetch all the current uids seen
				List<string> uids = client.GetMessageUids();

				// Create a list we can return with all new messages
				List<Message> newMessages = new List<Message>();

				// All the new messages not seen by the POP3 client
				for(int i = 0; i<uids.Count; i++)
				{
					string currentUidOnServer = uids[i];
					if (!seenUids.Contains(currentUidOnServer))
					{
						// We have not seen this message before.
						// Download it and add this new uid to seen uids

						// the uids list is in messageNumber order - meaning that the first
						// uid in the list has messageNumber of 1, and the second has 
						// messageNumber 2. Therefore we can fetch the message using
						// i + 1 since messageNumber should be in range [1, messageCount]
						Message unseenMessage = client.GetMessage(i + 1);

						// Add the message to the new messages
						newMessages.Add(unseenMessage);

						// Add the uid to the seen uids, as it has now been seen
						seenUids.Add(currentUidOnServer);
					}
				}

				// Return our new found messages
				return newMessages;
			}
		}

		/// <summary>
		/// Example showing:
		///  - how to set timeouts
		///  - how to override the SSL certificate checks with your own implementation
		/// </summary>
		/// <param name="hostname">Hostname of the server. For example: pop3.live.com</param>
		/// <param name="port">Host port to connect to. Normally: 110 for plain POP3, 995 for SSL POP3</param>
		/// <param name="timeouts">Read and write timeouts used by the Pop3Client</param>
		public static void BypassSslCertificateCheck(string hostname, int port, int timeouts)
		{
			// The client disconnects from the server when being disposed
			using (Pop3Client client = new Pop3Client())
			{
				// Connect to the server using SSL with specified settings
				// true here denotes that we connect using SSL
				// The certificateValidator can validate the SSL certificate of the server.
				// This might be needed if the server is using a custom normally untrusted certificate
				client.Connect(hostname, port, true, timeouts, timeouts, certificateValidator);

				// Do something extra now that we are connected to the server
			}
		}

		private static bool certificateValidator(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslpolicyerrors)
		{
			// We should check if there are some SSLPolicyErrors, but here we simply say that
			// the certificate is okay - we trust it.
			return true;
		}

		/// <summary>
		/// Example showing:
		///  - how to save a message to a file
		///  - how to load a message from a file at a later point
		/// </summary>
		/// <param name="message">The message to save and load at a later point</param>
		/// <returns>The Message, but loaded from the file system</returns>
		public static Message SaveAndLoadFullMessage(Message message)
		{
			// FileInfo about the location to save/load message
			FileInfo file = new FileInfo("someFile.eml");

			// Save the full message to some file
			message.Save(file);

			// Now load the message again. This could be done at a later point
			Message loadedMessage = Message.Load(file);

			// use the message again
			return loadedMessage;
		}

		/// <summary>
		/// Example showing:
		///  - How to change logging
		///  - How to implement your own logger
		/// </summary>
		public static void ChangeLogging()
		{
			// All logging is sent trough logger defined at DefaultLogger.Log
			// The logger can be changed by calling DefaultLogger.SetLog(someLogger)

			// By default all logging is sent to the System.Diagnostics.Trace facilities.
			// These are not very useful if you are not debugging
			// Instead, lets send logging to a file:
			DefaultLogger.SetLog(new FileLogger());
			FileLogger.LogFile = new FileInfo("MyLoggingFile.log");

			// It is also possible to implement your own logging:
			DefaultLogger.SetLog(new MyOwnLogger());
		}

		class MyOwnLogger : ILog
		{
			public void LogError(string message)
			{
				Console.WriteLine("ERROR!!!: " + message);
			}

			public void LogDebug(string message)
			{
				// Dont want to log debug messages
			}
		}

		/// <summary>
		/// Example showing:
		///  - How to provide custom Encoding class
		///  - How to use UTF8 as default Encoding
		/// </summary>
		/// <param name="customEncoding">Own Encoding implementation</param>
		public void InsertCustomEncodings(Encoding customEncoding)
		{
			// Lets say some email contains a characterSet of "iso-9999-9" which
			// is fictional, but is really just UTF-8.
			// Lets add that mapping to the class responsible for finding
			// the Encoding from the name of it
			EncodingFinder.AddMapping("iso-9999-9", Encoding.UTF8);

			// It is also possible to implement your own Encoding if
			// the framework does not provide what you need
			EncodingFinder.AddMapping("specialEncoding", customEncoding);

			// Now, if the EncodingFinder is not able to find an encoding, lets
			// see if we can find one ourselves
			EncodingFinder.FallbackDecoder = CustomFallbackDecoder;
		}

		Encoding CustomFallbackDecoder(string characterSet)
		{
			// Is it a "foo" encoding?
			if (characterSet.StartsWith("foo"))
				return Encoding.ASCII; // then use ASCII

			// If no special encoding could be found, provide UTF8 as default.
			// You can also return null here, which would tell OpenPop that
			// no encoding could be found. This will then throw an exception.
			return Encoding.UTF8;
		}

		// Other examples to show, that is in the library
		// Show how to build a TreeNode representation of the Message hierarchy using the
		// TreeNodeBuilder class in OpenPopTest
	}
}