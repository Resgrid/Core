using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenPop.Mime;
using OpenPop.Mime.Header;
using OpenPop.Pop3.Exceptions;
using OpenPop.Common;
using OpenPop.Common.Logging;

namespace OpenPop.Pop3
{
	/// <summary>
	/// POP3 compliant POP Client<br/>
	/// <br/>	
	/// If you want to override where logging is sent, look at <see cref="DefaultLogger"/>
	/// </summary>
	/// <example>
	/// Examples are available on the <a href="http://hpop.sourceforge.net/">project homepage</a>.
	/// </example>
	public class Pop3Client : Disposable
	{
		#region Private member properties
		/// <summary>
		/// The stream used to communicate with the server
		/// </summary>
		private Stream Stream { get; set; }

		/// <summary>
		/// This is the last response the server sent back when a command was issued to it
		/// </summary>
		private string LastServerResponse { get; set; }

		/// <summary>
		/// The APOP time stamp sent by the server in it's welcome message if APOP is supported.
		/// </summary>
		private string ApopTimeStamp { get; set; }

		/// <summary>
		/// Describes what state the <see cref="Pop3Client"/> is in
		/// </summary>
		private ConnectionState State { get; set; }
		#endregion

		#region Public member properties
		/// <summary>
		/// Tells whether the <see cref="Pop3Client"/> is connected to a POP server or not
		/// </summary>
		public bool Connected { get; private set; }

		/// <summary>
		/// Allows you to check if the server supports
		/// the <see cref="AuthenticationMethod.Apop"/> authentication method.<br/>
		/// <br/>
		/// This value is filled when the connect method has returned,
		/// as the server tells in its welcome message if APOP is supported.
		/// </summary>
		public bool ApopSupported { get; private set; }
		#endregion

		#region Constructors
		/// <summary>
		/// Constructs a new Pop3Client for you to use.
		/// </summary>
		public Pop3Client()
		{
			SetInitialValues();
		}
		#endregion

		#region IDisposable implementation
		/// <summary>
		/// Disposes the <see cref="Pop3Client"/>.<br/>
		/// This is the implementation of the <see cref="IDisposable"/> interface.<br/>
		/// Sends the QUIT command to the server before closing the streams.
		/// </summary>
		/// <param name="disposing"><see langword="true"/> if managed and unmanaged code should be disposed, <see langword="false"/> if only managed code should be disposed</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && !IsDisposed)
			{
				if (Connected)
				{
					Disconnect();
				}
			}

			base.Dispose(disposing);
		}
		#endregion

		#region Connection managing methods
		/// <summary>
		/// Connect to the server using user supplied stream
		/// </summary>
		/// <param name="stream">The stream used to communicate with the server</param>
		/// <exception cref="ArgumentNullException">If <paramref name="stream"/> is <see langword="null"/></exception>
		public void Connect(Stream stream)
		{
			AssertDisposed();

			if (State != ConnectionState.Disconnected)
				throw new InvalidUseException("You cannot ask to connect to a POP3 server, when we are already connected to one. Disconnect first.");

			if (stream == null)
				throw new ArgumentNullException("stream");

			Stream = stream;

			// Fetch the server one-line welcome greeting
			string response = StreamUtility.ReadLineAsAscii(Stream);

			// Check if the response was an OK response
			try
			{
				// Assume we now need the user to supply credentials
				// If we do not connect correctly, Disconnect will set the
				// state to Disconnected
				// If this is not set, Disconnect will throw an exception
				State = ConnectionState.Authorization;

				IsOkResponse(response);
				ExtractApopTimestamp(response);
				Connected = true;
			}
			catch (PopServerException e)
			{
				// If not close down the connection and abort
				DisconnectStreams();
				
				DefaultLogger.Log.LogError("Connect(): " + "Error with connection, maybe POP3 server not exist");
				DefaultLogger.Log.LogDebug("Last response from server was: " + LastServerResponse);
				throw new PopServerNotAvailableException("Server is not available", e);
			}
		}

		/// <summary>
		/// Connects to a remote POP3 server using default timeouts of 60.000 milliseconds
		/// </summary>
		/// <param name="hostname">The <paramref name="hostname"/> of the POP3 server</param>
		/// <param name="port">The port of the POP3 server</param>
		/// <param name="useSsl"><see langword="true"/> if SSL should be used. <see langword="false"/> if plain TCP should be used.</param>
		/// <exception cref="PopServerNotAvailableException">If the server did not send an OK message when a connection was established</exception>
		/// <exception cref="PopServerNotFoundException">If it was not possible to connect to the server</exception>
		/// <exception cref="ArgumentNullException">If <paramref name="hostname"/> is <see langword="null"/></exception>
		/// <exception cref="ArgumentOutOfRangeException">If port is not in the range [<see cref="IPEndPoint.MinPort"/>, <see cref="IPEndPoint.MaxPort"/></exception>
		public void Connect(string hostname, int port, bool useSsl)
		{
			const int defaultTimeOut = 60000;
			Connect(hostname, port, useSsl, defaultTimeOut, defaultTimeOut, null);
		}

		/// <summary>
		/// Connects to a remote POP3 server
		/// </summary>
		/// <param name="hostname">The <paramref name="hostname"/> of the POP3 server</param>
		/// <param name="port">The port of the POP3 server</param>
		/// <param name="useSsl"><see langword="true"/> if SSL should be used. <see langword="false"/> if plain TCP should be used.</param>
		/// <param name="receiveTimeout">Timeout in milliseconds before a socket should time out from reading. Set to 0 or -1 to specify infinite timeout.</param>
		/// <param name="sendTimeout">Timeout in milliseconds before a socket should time out from sending. Set to 0 or -1 to specify infinite timeout.</param>
		/// <param name="certificateValidator">If you want to validate the certificate in a SSL connection, pass a reference to your validator. Supply <see langword="null"/> if default should be used.</param>
		/// <exception cref="PopServerNotAvailableException">If the server did not send an OK message when a connection was established</exception>
		/// <exception cref="PopServerNotFoundException">If it was not possible to connect to the server</exception>
		/// <exception cref="ArgumentNullException">If <paramref name="hostname"/> is <see langword="null"/></exception>
		/// <exception cref="ArgumentOutOfRangeException">If port is not in the range [<see cref="IPEndPoint.MinPort"/>, <see cref="IPEndPoint.MaxPort"/> or if any of the timeouts is less than -1.</exception>
		public void Connect(string hostname, int port, bool useSsl, int receiveTimeout, int sendTimeout, RemoteCertificateValidationCallback certificateValidator)
		{
			AssertDisposed();

			if (hostname == null)
				throw new ArgumentNullException("hostname");

			if (hostname.Length == 0)
				throw new ArgumentException("hostname cannot be empty", "hostname");

			if (port > IPEndPoint.MaxPort || port < IPEndPoint.MinPort)
				throw new ArgumentOutOfRangeException("port");

			if (receiveTimeout < 0)
				throw new ArgumentOutOfRangeException("receiveTimeout");

			if (sendTimeout < 0)
				throw new ArgumentOutOfRangeException("sendTimeout");

			if (State != ConnectionState.Disconnected)
				throw new InvalidUseException("You cannot ask to connect to a POP3 server, when we are already connected to one. Disconnect first.");

			TcpClient clientSocket = new TcpClient();
			clientSocket.ReceiveTimeout = receiveTimeout;
			clientSocket.SendTimeout = sendTimeout;

			try
			{
				clientSocket.Connect(hostname, port);
			}
			catch (SocketException e)
			{
				// Close the socket - we are not connected, so no need to close stream underneath
				clientSocket.Close();

				DefaultLogger.Log.LogError("Connect(): " + e.Message);
				throw new PopServerNotFoundException("Server not found", e);
			}

			Stream stream;
			if (useSsl)
			{
				// If we want to use SSL, open a new SSLStream on top of the open TCP stream.
				// We also want to close the TCP stream when the SSL stream is closed
				// If a validator was passed to us, use it.
				SslStream sslStream;
				if (certificateValidator == null)
				{
					sslStream = new SslStream(clientSocket.GetStream(), false);
				}
				else
				{
					sslStream = new SslStream(clientSocket.GetStream(), false, certificateValidator);
				}
				sslStream.ReadTimeout = receiveTimeout;
				sslStream.WriteTimeout = sendTimeout;

				// Authenticate the server
				sslStream.AuthenticateAsClient(hostname);

				stream = sslStream;
			}
			else
			{
				// If we do not want to use SSL, use plain TCP
				stream = clientSocket.GetStream();
			}

			// Now do the connect with the same stream being used to read and write to
			Connect(stream);
		}

		/// <summary>
		/// Disconnects from POP3 server.
		/// Sends the QUIT command before closing the connection, which deletes all the messages that was marked as such.
		/// </summary>
		public void Disconnect()
		{
			AssertDisposed();

			if (State == ConnectionState.Disconnected)
				throw new InvalidUseException("You cannot disconnect a connection which is already disconnected");

			try
			{
				SendCommand("QUIT");
			} finally
			{
				DisconnectStreams();
			}
		}
		#endregion

		#region Authentication methods
		/// <summary>
		/// Authenticates a user towards the POP server using <see cref="AuthenticationMethod.Auto"/>.<br/>
		/// If this authentication fails but you are sure that the username and password is correct, it might
		/// be that that the POP3 server is wrongly telling the client it supports <see cref="AuthenticationMethod.Apop"/>.
		/// You should try using <see cref="Authenticate(string, string, AuthenticationMethod)"/> while passing <see cref="AuthenticationMethod.UsernameAndPassword"/> to the method.
		/// </summary>
		/// <param name="username">The username</param>
		/// <param name="password">The user password</param>
		/// <exception cref="InvalidLoginException">If the user credentials was not accepted</exception>
		/// <exception cref="PopServerLockedException">If the server said the the mailbox was locked</exception>
		/// <exception cref="ArgumentNullException">If <paramref name="username"/> or <paramref name="password"/> is <see langword="null"/></exception>
		/// <exception cref="LoginDelayException">If the server rejects the login because of too recent logins</exception>
		public void Authenticate(string username, string password)
		{
			AssertDisposed();
			Authenticate(username, password, AuthenticationMethod.Auto);
		}

		/// <summary>
		/// Authenticates a user towards the POP server using some <see cref="AuthenticationMethod"/>.
		/// </summary>
		/// <param name="username">The username</param>
		/// <param name="password">The user password</param>
		/// <param name="authenticationMethod">The way that the client should authenticate towards the server</param>
		/// <exception cref="NotSupportedException">If <see cref="AuthenticationMethod.Apop"/> is used, but not supported by the server</exception>
		/// <exception cref="InvalidLoginException">If the user credentials was not accepted</exception>
		/// <exception cref="PopServerLockedException">If the server said the the mailbox was locked</exception>
		/// <exception cref="ArgumentNullException">If <paramref name="username"/> or <paramref name="password"/> is <see langword="null"/></exception>
		/// <exception cref="LoginDelayException">If the server rejects the login because of too recent logins</exception>
		public void Authenticate(string username, string password, AuthenticationMethod authenticationMethod)
		{
			AssertDisposed();

			if(username == null)
				throw new ArgumentNullException("username");

			if(password == null)
				throw new ArgumentNullException("password");

			if(State != ConnectionState.Authorization)
				throw new InvalidUseException("You have to be connected and not authorized when trying to authorize yourself");

			try
			{
				switch (authenticationMethod)
				{
					case AuthenticationMethod.UsernameAndPassword:
						AuthenticateUsingUserAndPassword(username, password);
						break;

					case AuthenticationMethod.Apop:
						AuthenticateUsingApop(username, password);
						break;

					case AuthenticationMethod.Auto:
						if (ApopSupported)
							AuthenticateUsingApop(username, password);
						else
							AuthenticateUsingUserAndPassword(username, password);
						break;

					case AuthenticationMethod.CramMd5:
						AuthenticateUsingCramMd5(username, password);
						break;
				}
			} catch(PopServerException e)
			{
				DefaultLogger.Log.LogError("Problem logging in using method " + authenticationMethod + ". Server response was: " + LastServerResponse);

				// Throw a more specific exception if special cases of failure is detected
				// using the response the server generated when the last command was sent
				CheckFailedLoginServerResponse(LastServerResponse, e);

				// If no special failure is detected, tell that the login credentials were wrong
				throw new InvalidLoginException(e);
			}

			// We are now authenticated and therefore we enter the transaction state
			State = ConnectionState.Transaction;
		}

		/// <summary>
		/// Authenticates a user towards the POP server using the USER and PASSWORD commands
		/// </summary>
		/// <param name="username">The username</param>
		/// <param name="password">The user password</param>
		/// <exception cref="PopServerException">If the server responded with -ERR</exception>
		private void AuthenticateUsingUserAndPassword(string username, string password)
		{
			SendCommand("USER " + username);
			SendCommand("PASS " + password);

			// Authentication was successful if no exceptions thrown before getting here
		}

		/// <summary>
		/// Authenticates a user towards the POP server using APOP
		/// </summary>
		/// <param name="username">The username</param>
		/// <param name="password">The user password</param>
		/// <exception cref="NotSupportedException">Thrown when the server does not support APOP</exception>
		/// <exception cref="PopServerException">If the server responded with -ERR</exception>
		private void AuthenticateUsingApop(string username, string password)
		{
			if (!ApopSupported)
				throw new NotSupportedException("APOP is not supported on this server");

			SendCommand("APOP " + username + " " + Apop.ComputeDigest(password, ApopTimeStamp));

			// Authentication was successful if no exceptions thrown before getting here
		}

		/// <summary>
		/// Authenticates using the CRAM-MD5 authentication method
		/// </summary>
		/// <param name="username">The username</param>
		/// <param name="password">The user password</param>
		/// <exception cref="NotSupportedException">Thrown when the server does not support AUTH CRAM-MD5</exception>
		/// <exception cref="InvalidLoginException">If the user credentials was not accepted</exception>
		/// <exception cref="PopServerLockedException">If the server said the the mailbox was locked</exception>
		/// <exception cref="LoginDelayException">If the server rejects the login because of too recent logins</exception>
		private void AuthenticateUsingCramMd5(string username, string password)
		{
			// Example of communication:
			// C: AUTH CRAM-MD5
			// S: + PDE4OTYuNjk3MTcwOTUyQHBvc3RvZmZpY2UucmVzdG9uLm1jaS5uZXQ+
			// C: dGltIGI5MTNhNjAyYzdlZGE3YTQ5NWI0ZTZlNzMzNGQzODkw
			// S: +OK CRAM authentication successful
			
			// Other example, where AUTH CRAM-MD5 is not supported
			// C: AUTH CRAM-MD5
			// S: -ERR Authentication method CRAM-MD5 not supported
			
			try
			{
				SendCommand("AUTH CRAM-MD5");
			} catch (PopServerException e)
			{
				// A PopServerException will be thrown if the server responds with a -ERR not supported
				throw new NotSupportedException("CRAM-MD5 authentication not supported", e);
			}
			
			// Fetch out the challenge from the server response
			string challenge = LastServerResponse.Substring(2);

			// Compute the challenge response
			string response = CramMd5.ComputeDigest(username, password, challenge);

			// Send the response to the server
			SendCommand(response);

			// Authentication was successful if no exceptions thrown before getting here
		}
		#endregion

		#region Public POP3 commands
		/// <summary>
		/// Get the number of messages on the server using a STAT command
		/// </summary>
		/// <returns>The message count on the server</returns>
		/// <exception cref="PopServerException">If the server did not accept the STAT command</exception>
		public int GetMessageCount()
		{
			AssertDisposed();

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("You cannot get the message count without authenticating yourself towards the server first");

			return SendCommandIntResponse("STAT", 1);
		}

		/// <summary>
		/// Marks the message with the given message number as deleted.<br/>
		/// <br/>
		/// The message will not be deleted until a QUIT command is sent to the server.<br/>
		/// This is done when you call <see cref="Disconnect()"/> or when the Pop3Client is <see cref="Dispose">Disposed</see>.
		/// </summary>
		/// <param name="messageNumber">
		/// The number of the message to be deleted. This message may not already have been deleted.<br/>
		/// The <paramref name="messageNumber"/> must be inside the range [1, messageCount]
		/// </param>
		/// <exception cref="PopServerException">If the server did not accept the delete command</exception>
		public void DeleteMessage(int messageNumber)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("You cannot delete any messages without authenticating yourself towards the server first");

			SendCommand("DELE " + messageNumber);
		}

		/// <summary>
		/// Marks all messages as deleted.<br/>
		/// <br/>
		/// The messages will not be deleted until a QUIT command is sent to the server.<br/>
		/// This is done when you call <see cref="Disconnect()"/> or when the Pop3Client is <see cref="Dispose">Disposed</see>.<br/>
		/// The method assumes that no prior message has been marked as deleted, and is not valid to call if this is wrong.
		/// </summary>
		/// <exception cref="PopServerException">If the server did not accept one of the delete commands. All prior marked messages will still be marked.</exception>
		public void DeleteAllMessages()
		{
			AssertDisposed();

			int messageCount = GetMessageCount();

			for (int messageItem = messageCount; messageItem > 0; messageItem--)
			{
				DeleteMessage(messageItem);
			}
		}

		/// <summary>
		/// Keep server active by sending a NOOP command.<br/>
		/// This might keep the server from closing the connection due to inactivity.<br/>
		/// <br/>
		/// RFC:<br/>
		/// The POP3 server does nothing, it merely replies with a positive response.
		/// </summary>
		/// <exception cref="PopServerException">If the server did not accept the NOOP command</exception>
		public void NoOperation()
		{
			AssertDisposed();

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("You cannot use the NOOP command unless you are authenticated to the server");

			SendCommand("NOOP");
		}

		/// <summary>
		/// Send a reset command to the server.<br/>
		/// <br/>
		/// RFC:<br/>
		/// If any messages have been marked as deleted by the POP3
		/// server, they are unmarked. The POP3 server then replies
		/// with a positive response.
		/// </summary>
		/// <exception cref="PopServerException">If the server did not accept the RSET command</exception>
		public void Reset()
		{
			AssertDisposed();

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("You cannot use the RSET command unless you are authenticated to the server");

			SendCommand("RSET");
		}

		/// <summary>
		/// Get a unique ID for a single message.<br/>
		/// </summary>
		/// <param name="messageNumber">
		/// Message number, which may not be marked as deleted.<br/>
		/// The <paramref name="messageNumber"/> must be inside the range [1, messageCount]
		/// </param>
		/// <returns>The unique ID for the message</returns>
		/// <exception cref="PopServerException">If the server did not accept the UIDL command. This could happen if the <paramref name="messageNumber"/> does not exist</exception>
		public string GetMessageUid(int messageNumber)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot get message ID, when the user has not been authenticated yet");

			// Example from RFC:
			//C: UIDL 2
			//S: +OK 2 QhdPYR:00WBw1Ph7x7

			SendCommand("UIDL " + messageNumber);

			// Parse out the unique ID
			return LastServerResponse.Split(' ')[2];
		}

		/// <summary>
		/// Gets a list of unique IDs for all messages.<br/>
		/// Messages marked as deleted are not listed.
		/// </summary>
		/// <returns>
		/// A list containing the unique IDs in sorted order from message number 1 and upwards.
		/// </returns>
		/// <exception cref="PopServerException">If the server did not accept the UIDL command</exception>
		public List<string> GetMessageUids()
		{
			AssertDisposed();

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot get message IDs, when the user has not been authenticated yet");

			// RFC Example:
			// C: UIDL
			// S: +OK
			// S: 1 whqtswO00WBw418f9t5JxYwZ
			// S: 2 QhdPYR:00WBw1Ph7x7
			// S: .      // this is the end

			SendCommand("UIDL");

			List<string> uids = new List<string>();

			string response;
			// Keep reading until multi-line ends with a "."
			while (!IsLastLineInMultiLineResponse(response = StreamUtility.ReadLineAsAscii(Stream)))
			{
				// Add the unique ID to the list
				uids.Add(response.Split(' ')[1]);
			}

			return uids;
		}

		/// <summary>
		/// Gets the size in bytes of a single message
		/// </summary>
		/// <param name="messageNumber">
		/// The number of a message which may not be a message marked as deleted.<br/>
		/// The <paramref name="messageNumber"/> must be inside the range [1, messageCount]
		/// </param>
		/// <returns>Size of the message</returns>
		/// <exception cref="PopServerException">If the server did not accept the LIST command</exception>
		public int GetMessageSize(int messageNumber)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot get message size, when the user has not been authenticated yet");

			// RFC Example:
			// C: LIST 2
			// S: +OK 2 200
			return SendCommandIntResponse("LIST " + messageNumber, 2);
		}

		/// <summary>
		/// Get the sizes in bytes of all the messages.<br/>
		/// Messages marked as deleted are not listed.
		/// </summary>
		/// <returns>Size of each message excluding deleted ones</returns>
		/// <exception cref="PopServerException">If the server did not accept the LIST command</exception>
		public List<int> GetMessageSizes()
		{
			AssertDisposed();

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot get message sizes, when the user has not been authenticated yet");

			// RFC Example:
			// C: LIST
			// S: +OK 2 messages (320 octets)
			// S: 1 120
			// S: 2 200
			// S: .       // End of multi-line

			SendCommand("LIST");

			List<int> sizes = new List<int>();

			string response;
			// Read until end of multi-line
			while (!".".Equals(response = StreamUtility.ReadLineAsAscii(Stream)))
			{
				sizes.Add(int.Parse(response.Split(' ')[1], CultureInfo.InvariantCulture));
			}

			return sizes;
		}

		/// <summary>
		/// Fetches a message from the server and parses it
		/// </summary>
		/// <param name="messageNumber">
		/// Message number on server, which may not be marked as deleted.<br/>
		/// Must be inside the range [1, messageCount]
		/// </param>
		/// <returns>The message, containing the email message</returns>
		/// <exception cref="PopServerException">If the server did not accept the command sent to fetch the message</exception>
		public Message GetMessage(int messageNumber)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot fetch a message, when the user has not been authenticated yet");

			byte[] messageContent = GetMessageAsBytes(messageNumber);

			return new Message(messageContent);
		}

		/// <summary>
		/// Fetches a message in raw form from the server
		/// </summary>
		/// <param name="messageNumber">
		/// Message number on server, which may not be marked as deleted.<br/>
		/// Must be inside the range [1, messageCount]
		/// </param>
		/// <returns>The raw bytes of the message</returns>
		/// <exception cref="PopServerException">If the server did not accept the command sent to fetch the message</exception>
		public byte[] GetMessageAsBytes(int messageNumber)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot fetch a message, when the user has not been authenticated yet");

			// Get the full message
			return GetMessageAsBytes(messageNumber, false);
		}

		/// <summary>
		/// Get all the headers for a message.<br/>
		/// The server will not need to send the body of the message.
		/// </summary>
		/// <param name="messageNumber">
		/// Message number, which may not be marked as deleted.<br/>
		/// Must be inside the range [1, messageCount]
		/// </param>
		/// <returns>MessageHeaders object</returns>
		/// <exception cref="PopServerException">If the server did not accept the command sent to fetch the message</exception>
		public MessageHeader GetMessageHeaders(int messageNumber)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot fetch a message, when the user has not been authenticated yet");

			// Only fetch the header part of the message
			byte[] messageContent = GetMessageAsBytes(messageNumber, true);

			// Do not parse the body - as it is not in the byte array
			return new Message(messageContent, false).Headers;
		}

		/// <summary>
		/// Asks the server to return it's capability listing.<br/>
		/// This is an optional command, which a server is not enforced to accept.
		/// </summary>
		/// <returns>
		/// The returned Dictionary keys are the capability names.<br/>
		/// The Lists pointed to are the capability parameters fitting that certain capability name.
		/// See <a href="http://tools.ietf.org/html/rfc2449#section-6">RFC section 6</a> for explanation for some of the capabilities.
		/// </returns>
		/// <remarks>
		/// Capabilities are case-insensitive.<br/>
		/// The dictionary uses case-insensitive searching, but the Lists inside
		/// does not. Therefore you will have to use something like the code below
		/// to search for a capability parameter.<br/>
		/// foo is the capability name and bar is the capability parameter.
		/// <code>
		/// List&lt;string&gt; arguments = capabilities["foo"];
		///	bool contains = null != arguments.Find(delegate(string str)
		///				{
		///					return String.Compare(str, "bar", true) == 0;
		///				});
		/// </code>
		/// If we were running on .NET framework >= 3.5, a HashSet could have been used.
		/// </remarks>
		/// <exception cref="PopServerException">If the server did not accept the capability command</exception>
		public Dictionary<string, List<string>> Capabilities()
		{
			AssertDisposed();

			if(State != ConnectionState.Authorization && State != ConnectionState.Transaction)
				throw new InvalidUseException("Capability command only available while connected or authenticated");

			// RFC Example
			// Examples:
			// C: CAPA
            // S: +OK Capability list follows
            // S: TOP
            // S: USER
            // S: SASL CRAM-MD5 KERBEROS_V4
            // S: RESP-CODES
            // S: LOGIN-DELAY 900
            // S: PIPELINING
            // S: EXPIRE 60
            // S: UIDL
            // S: IMPLEMENTATION Shlemazle-Plotz-v302
            // S: .
			SendCommand("CAPA");

			// Capablities are case-insensitive
			Dictionary<string, List<string>> capabilities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

			string lineRead;
			// Keep reading until we are at the end of the multi line response
			while (!IsLastLineInMultiLineResponse(lineRead = StreamUtility.ReadLineAsAscii(Stream)))
			{
				// Example of read line
				// SASL CRAM-MD5 KERBEROS_V4
				// SASL is the name of the capability while
				// CRAM-MD5 and KERBEROS_V4 are arguments to SASL
				string[] splitted = lineRead.Split(' ');

				// There should always be a capability name
				string capabilityName = splitted[0];
				
				// Find all the arguments
				List<string> capabilityArguments = new List<string>();
				for(int i = 1; i<splitted.Length; i++)
				{
					capabilityArguments.Add(splitted[i]);
				}

				// Add the capability found to the dictionary
				capabilities.Add(capabilityName, capabilityArguments);
			}

			return capabilities;
		}
		#endregion

		#region Private helper methods
		/// <summary>
		/// Examines string to see if it contains a time stamp to use with the APOP command.<br/>
		/// If it does, sets the <see cref="ApopTimeStamp"/> property to this value.
		/// </summary>
		/// <param name="response">The string to examine</param>
		/// <exception cref="ArgumentNullException">If <paramref name="response"/> is <see langword="null"/></exception>
		private void ExtractApopTimestamp(string response)
		{
			if(response == null)
				throw new ArgumentNullException("response");

			// RFC Example:
			// +OK POP3 server ready <1896.697170952@dbc.mtview.ca.us>
			Match match = Regex.Match(response, "<.+>");
			if (match.Success)
			{
				ApopTimeStamp = match.Value;
				ApopSupported = true;
			}
		}

		/// <summary>
		/// Tests a string to see if it is a "+" string.<br/>
		/// An "+" string should be returned by a compliant POP3
		/// server if the request could be served.<br/>
		/// <br/>
		/// The method does only check if it starts with "+".
		/// </summary>
		/// <param name="response">The string to examine</param>
		/// <exception cref="PopServerException">Thrown if server did not respond with "+" message</exception>
		private static void IsOkResponse(string response)
		{
			if (response == null)
				throw new PopServerException("The stream used to retrieve responses from was closed");

			if (response.StartsWith("+", StringComparison.OrdinalIgnoreCase))
				return;

			throw new PopServerException("The server did not respond with a + response. The response was: \"" + response + "\"");
		}

		/// <summary>
		/// Sends a command to the POP server.<br/>
		/// If this fails, an exception is thrown.
		/// </summary>
		/// <param name="command">The command to send to server</param>
		/// <exception cref="PopServerException">If the server did not send an OK message to the command</exception>
		private void SendCommand(string command)
		{
			// Convert the command with CRLF afterwards as per RFC to a byte array which we can write
			byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\r\n");

			// Write the command to the server
			Stream.Write(commandBytes, 0, commandBytes.Length);
			Stream.Flush(); // Flush the content as we now wait for a response

			// Read the response from the server. The response should be in ASCII
			LastServerResponse = StreamUtility.ReadLineAsAscii(Stream);

			IsOkResponse(LastServerResponse);
		}

		/// <summary>
		/// Sends a command to the POP server, expects an integer reply in the response
		/// </summary>
		/// <param name="command">command to send to server</param>
		/// <param name="location">
		/// The location of the int to return.<br/>
		/// Example:<br/>
		/// <c>S: +OK 2 200</c><br/>
		/// Set <paramref name="location"/>=1 to get 2<br/>
		/// Set <paramref name="location"/>=2 to get 200<br/>
		/// </param>
		/// <returns>Integer value in the reply</returns>
		/// <exception cref="PopServerException">If the server did not accept the command</exception>
		private int SendCommandIntResponse(string command, int location)
		{
			SendCommand(command);

			return int.Parse(LastServerResponse.Split(' ')[location], CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Asks the server for a message and returns the message response as a byte array.
		/// </summary>
		/// <param name="messageNumber">
		/// Message number on server, which may not be marked as deleted.<br/>
		/// Must be inside the range [1, messageCount]
		/// </param>
		/// <param name="askOnlyForHeaders">If <see langword="true"/> only the header part of the message is requested from the server. If <see langword="false"/> the full message is requested</param>
		/// <returns>A byte array that the message requested consists of</returns>
		/// <exception cref="PopServerException">If the server did not accept the command sent to fetch the message</exception>
		private byte[] GetMessageAsBytes(int messageNumber, bool askOnlyForHeaders)
		{
			AssertDisposed();

			ValidateMessageNumber(messageNumber);

			if (State != ConnectionState.Transaction)
				throw new InvalidUseException("Cannot fetch a message, when the user has not been authenticated yet");

			if (askOnlyForHeaders)
			{
				// 0 is the number of lines of the message body to fetch, therefore it is set to zero to fetch only headers
				SendCommand("TOP " + messageNumber + " 0");
			}
			else
			{
				// Ask for the full message
				SendCommand("RETR " + messageNumber);
			}

			// RFC 1939 Example
			// C: RETR 1
			// S: +OK 120 octets
			// S: <the POP3 server sends the entire message here>
			// S: .

			// Create a byte array builder which we use to write the bytes too
			// When done, we can get the byte array out
			using (MemoryStream byteArrayBuilder = new MemoryStream())
			{
				bool first = true;
				byte[] lineRead;

				// Keep reading until we are at the end of the multi line response
				while (!IsLastLineInMultiLineResponse(lineRead = StreamUtility.ReadLineAsBytes(Stream)))
				{
					// We should not write CRLF on the very last line, therefore we do this
					if (!first)
					{
						// Write CRLF which was not included in the lineRead bytes of last line
						byte[] crlfPair = Encoding.ASCII.GetBytes("\r\n");
						byteArrayBuilder.Write(crlfPair, 0, crlfPair.Length);
					} else
					{
						// We are now not the first anymore
						first = false;
					}

					// This is a multi-line. See http://tools.ietf.org/html/rfc1939#section-3
					// It says that a line starting with "." and not having CRLF after it
					// is a multi line, and the "." should be stripped
					if (lineRead.Length > 0 && lineRead[0] == '.')
					{
						// Do not write the first period
						byteArrayBuilder.Write(lineRead, 1, lineRead.Length - 1);
					} else
					{
						// Write everything
						byteArrayBuilder.Write(lineRead, 0, lineRead.Length);
					}
				}

				// If we are fetching a header - add an extra line to denote the headers ended
				if (askOnlyForHeaders)
				{
					byte[] crlfPair = Encoding.ASCII.GetBytes("\r\n");
					byteArrayBuilder.Write(crlfPair, 0, crlfPair.Length);
				}

				// Get out the bytes we have written to byteArrayBuilder
				byte[] receivedBytes = byteArrayBuilder.ToArray();

				return receivedBytes;
			}
		}

		/// <summary>
		/// Check if the bytes received is the last line in a multi line response
		/// from the pop3 server. It is the last line if the line contains only a "."
		/// </summary>
		/// <param name="bytesReceived">The last line received from the server, which could be the last response line</param>
		/// <returns><see langword="true"/> if last line in a multi line response, <see langword="false"/> otherwise</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="bytesReceived"/> is <see langword="null"/></exception>
		private static bool IsLastLineInMultiLineResponse(byte[] bytesReceived)
		{
			if(bytesReceived == null)
				throw new ArgumentNullException("bytesReceived");

			return bytesReceived.Length == 1 && bytesReceived[0] == '.';
		}

		/// <see cref="IsLastLineInMultiLineResponse(byte[])"> for documentation</see>
		private static bool IsLastLineInMultiLineResponse(string lineReceived)
		{
			if (lineReceived == null)
				throw new ArgumentNullException("lineReceived");

			// If the string is indeed the last line, then it is okay to do ASCII encoding
			// on it. For performance reasons we check if the length is equal to 1
			// so that we do not need to decode a long message string just to see if
			// it is the last line
			return lineReceived.Length == 1 && IsLastLineInMultiLineResponse(Encoding.ASCII.GetBytes(lineReceived));
		}

		/// <summary>
		/// Method for checking that a <paramref name="messageNumber"/> argument given to some method
		/// is indeed valid. If not, <see cref="InvalidUseException"/> will be thrown.
		/// </summary>
		/// <param name="messageNumber">The message number to validate</param>
		private static void ValidateMessageNumber(int messageNumber)
		{
			if(messageNumber <= 0)
				throw new InvalidUseException("The messageNumber argument cannot have a value of zero or less. Valid messageNumber is in the range [1, messageCount]");
		}

		/// <summary>
		/// Closes down the streams and sets the Pop3Client into the initial configuration
		/// </summary>
		private void DisconnectStreams()
		{
			try
			{
				Stream.Close();
			}
			finally
			{
				// Reset values to initial state
				SetInitialValues();
			}
		}

		/// <summary>
		/// Sets the initial values on the public properties of this Pop3Client.
		/// </summary>
		private void SetInitialValues()
		{
			// We have not seen the APOPTimestamp yet
			ApopTimeStamp = null;

			// We are not connected
			Connected = false;
			State = ConnectionState.Disconnected;

			// APOP is not supported before we check on login
			ApopSupported = false;
		}

		/// <summary>
		/// Checks for extra response codes when an authentication has failed and throws
		/// the correct exception.
		/// If no such response codes is found, nothing happens.
		/// </summary>
		/// <param name="serverErrorResponse">The server response string</param>
		/// <param name="e">The exception thrown because the server responded with -ERR</param>
		/// <exception cref="PopServerLockedException">If the account is locked or in use</exception>
		/// <exception cref="LoginDelayException">If the server rejects the login because of too recent logins</exception>
		private static void CheckFailedLoginServerResponse(string serverErrorResponse, PopServerException e)
		{
			string upper = serverErrorResponse.ToUpperInvariant();

			// Bracketed strings are extra response codes addded
			// in RFC http://tools.ietf.org/html/rfc2449
			// together with the CAPA command.

			// Specifies the account is in use
			if (upper.Contains("[IN-USE]") || upper.Contains("LOCK"))
			{
				DefaultLogger.Log.LogError("Authentication: maildrop is locked or in-use");
				throw new PopServerLockedException(e);
			}

			// Specifies that there must go some time between logins
			if (upper.Contains("[LOGIN-DELAY]"))
			{
				throw new LoginDelayException(e);
			}
		}
		#endregion
	}
}