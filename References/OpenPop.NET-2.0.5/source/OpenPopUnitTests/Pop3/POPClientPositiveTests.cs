using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenPop.Mime;
using OpenPop.Mime.Header;
using OpenPop.Pop3;
using OpenPop.Pop3.Exceptions;

namespace OpenPopUnitTests.Pop3
{
	[TestFixture]
	public class POPClientPositiveTests
	{
		/// <summary>
		/// This test comes from the RFC 1939 example located at 
		/// http://tools.ietf.org/html/rfc1939#page-16
		/// </summary>
		[Test]
		public void TestApopAuthentication()
		{
			const string welcomeMessage = "+OK POP3 server ready <1896.697170952@dbc.mtview.ca.us>";
			const string loginMessage = "+OK mrose's maildrop has 2 messages (320 octets)";
			const string serverResponses = welcomeMessage + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server supports APOP
			Assert.IsTrue(client.ApopSupported);

			client.Authenticate("mrose", "tanstaaf", AuthenticationMethod.Apop);

			const string expectedOutput = "APOP mrose c4c9334bac560ecc979e58001b3e22fb\r\n";
			string output = Encoding.ASCII.GetString(outputStream.ToArray());

			// The correct APOP command should have been sent
			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestAutoAuthenticationApop()
		{
			const string welcomeMessage = "+OK POP3 server ready <1896.697170952@dbc.mtview.ca.us>";
			const string loginMessage = "+OK mrose's maildrop has 2 messages (320 octets)";
			const string serverResponses = welcomeMessage + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server supports APOP
			Assert.IsTrue(client.ApopSupported);

			client.Authenticate("mrose", "tanstaaf");

			const string expectedOutput = "APOP mrose c4c9334bac560ecc979e58001b3e22fb\r\n";
			string output = Encoding.ASCII.GetString(outputStream.ToArray());

			// The correct APOP command should have been sent
			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestAutoAuthenticationUsernameAndPassword()
		{
			const string welcomeMessage = "+OK POP3 server ready";
			const string okUsername = "+OK";
			const string loginMessage = "+OK mrose's maildrop has 2 messages (320 octets)";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername  + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server does not support APOP
			Assert.IsFalse(client.ApopSupported);

			client.Authenticate("mrose", "tanstaaf");

			string[] commandsFired = GetCommands(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			const string firstCommand = "USER mrose";
			Assert.AreEqual(firstCommand, commandsFired[0]);

			const string secondCommand = "PASS tanstaaf";
			Assert.AreEqual(secondCommand, commandsFired[1]);
		}

		[Test]
		public void TestUsernameAndPasswordAuthentication()
		{
			const string welcomeMessage = "+OK POP3 server ready";
			const string okUsername = "+OK";
			const string loginMessage = "+OK";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server does not support APOP
			Assert.IsFalse(client.ApopSupported);

			client.Authenticate("foo", "bar", AuthenticationMethod.UsernameAndPassword);

			string[] commandsFired = GetCommands(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			const string firstCommand = "USER foo";
			Assert.AreEqual(firstCommand, commandsFired[0]);

			const string secondCommand = "PASS bar";
			Assert.AreEqual(secondCommand, commandsFired[1]);
		}

		[Test]
		public void TestUsernameAndPasswordAuthenticationLocked()
		{
			const string welcomeMessage = "+OK POP3 server ready";
			const string okUsername = "+OK";
			const string loginMessage = "-ERR account is locked";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server does not support APOP
			Assert.IsFalse(client.ApopSupported);

			Assert.Throws<PopServerLockedException>(delegate { client.Authenticate("foo", "bar", AuthenticationMethod.UsernameAndPassword); });
		}

		[Test]
		public void TestUsernameAndPasswordAuthenticationInUse()
		{
			const string welcomeMessage = "+OK POP3 server ready";
			const string okUsername = "+OK";
			const string loginMessage = "-ERR [IN-USE]";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server does not support APOP
			Assert.IsFalse(client.ApopSupported);

			Assert.Throws<PopServerLockedException>(delegate { client.Authenticate("foo", "bar", AuthenticationMethod.UsernameAndPassword); });
		}

		[Test]
		public void TestUsernameAndPasswordAuthenticationInUseCaseInsensitive()
		{
			const string welcomeMessage = "+OK POP3 server ready";
			const string okUsername = "+OK";
			const string loginMessage = "-ERR [In-use]";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			// The Pop3Client should now have seen, that the server does not support APOP
			Assert.IsFalse(client.ApopSupported);

			Assert.Throws<PopServerLockedException>(delegate { client.Authenticate("foo", "bar", AuthenticationMethod.UsernameAndPassword); });
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-6
		/// </summary>
		[Test]
		public void TestGetMessageCount()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string statCommandResponse = "+OK 5 10"; // 5 Messages with total size of 10 octets
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + statCommandResponse + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			int numberOfMessages = client.GetMessageCount();

			// We expected 5 messages
			Assert.AreEqual(5, numberOfMessages);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-8
		/// </summary>
		[Test]
		public void TestDeleteMessage()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string deleteResponse = "+OK"; // Message was deleted
			const string quitAccepted = "+OK";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + deleteResponse + "\r\n" + quitAccepted + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			client.DeleteMessage(5);

			const string expectedOutput = "DELE 5";
			string output = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			// We expected that the last command is the delete command
			Assert.AreEqual(expectedOutput, output);

			client.Disconnect();

			const string expectedOutputAfterQuit = "QUIT";
			string outputAfterQuit = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			// We now expect that the client has sent the QUIT command
			Assert.AreEqual(expectedOutputAfterQuit, outputAfterQuit);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-8
		/// </summary>
		[Test]
		public void TestDeleteAllMessages()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string messageCountResponse = "+OK 2 5"; // 2 messages with total size of 5 octets
			const string deleteResponse = "+OK"; // Message was deleted
			const string quitAccepted = "+OK";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + messageCountResponse  + "\r\n" + deleteResponse + "\r\n" + deleteResponse + "\r\n" + quitAccepted + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			// Delete all the messages
			client.DeleteAllMessages();

			// Check that message 1 and message 2 was deleted
			string[] commandsFired = GetCommands(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			bool message1Deleted = false;
			bool message2Deleted = false;
			foreach (string commandFired in commandsFired)
			{
				if (commandFired.Equals("DELE 1"))
					message1Deleted = true;

				if (commandFired.Equals("DELE 2"))
					message2Deleted = true;
			}

			// We expect message 1 to be deleted
			Assert.IsTrue(message1Deleted);

			// We expect message 2 to be deleted
			Assert.IsTrue(message2Deleted);

			// Quit and commit
			client.Disconnect();

			const string expectedOutputAfterQuit = "QUIT";
			string outputAfterQuit = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			// We now expect that the client has sent the QUIT command
			Assert.AreEqual(expectedOutputAfterQuit, outputAfterQuit);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-9
		/// </summary>
		[Test]
		public void TestNoOperation()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string noOperationOk = "+OK";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + noOperationOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			client.NoOperation();

			// Get the last command issued by the client
			string output = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			// We expect it to be NOOP
			const string expectedOutput = "NOOP";

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-9
		/// </summary>
		[Test]
		public void TestReset()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string resetOk = "+OK";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + resetOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			client.Reset();

			// Get the last command issued by the client
			string output = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			// We expect it to be RSET
			const string expectedOutput = "RSET";

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-12
		/// </summary>
		[Test]
		public void TestGetMessageUid()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string messageUidResponse = "+OK 2 psycho"; // Message 2 has UID psycho
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + messageUidResponse + "\r\n";
			
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			const string expectedOutput = "psycho";

			// Delete all the messages
			string output = client.GetMessageUid(2);

			// We now expect that the client has given us the correct UID
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-12
		/// </summary>
		[Test]
		public void TestGetMessageUids()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string messageUidAccepted = "+OK";
			const string messageUid1 = "1 psycho"; // Message 1 has UID psycho
			const string messageUid2 = "2 lord"; // Message 2 has UID lord
			const string uidListEnded = ".";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + messageUidAccepted + "\r\n" + messageUid1 + "\r\n" + messageUid2 + "\r\n" + uidListEnded + "\r\n";
			
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			// Get the UIDs for all the messages in sorted order from 1 and upwards
			List<string> uids = client.GetMessageUids();

			// The list should have size 2
			Assert.AreEqual(2, uids.Count);

			// The first entry should have uid psycho
			Assert.AreEqual("psycho", uids[0]);

			// The second entry should have uid lord
			Assert.AreEqual("lord", uids[1]);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-7
		/// </summary>
		[Test]
		public void TestGetMessageSize()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string messageSize = "+OK 9 200"; // Message 9 has size 200 octets
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + messageSize + "\r\n";
			
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			// Message 9 should have size 200
			const int expectedOutput = 200;
			int output = client.GetMessageSize(9);

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-7
		/// </summary>
		[Test]
		public void TestGetMessageSizes()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string messageListAccepted = "+OK 2 messages (320 octets)";
			const string messageSize1 = "1 120";
			const string messageSize2 = "2 200";
			const string messageListEnd = ".";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + messageListAccepted + "\r\n" + messageSize1 + "\r\n" + messageSize2 + "\r\n" + messageListEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			// Message 9 should have size 200
			List<int> messageSizes = client.GetMessageSizes();

			// The list should have size 2
			Assert.AreEqual(2, messageSizes.Count);

			// The first entry should have size 120
			Assert.AreEqual(120, messageSizes[0]);

			// The second entry should have size 200
			Assert.AreEqual(200, messageSizes[1]);
		}

		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-8
		/// This also tests that the message parsing is correct
		/// </summary>
		[Test]
		public void TestGetMessageNoContentType()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Return-path: <test@test.com>\r\nEnvelope-to: test2@test.com\r\nDelivery-date: Tue, 05 Oct 2010 04:02:06 +0200\r\nReceived: from test by test.com with local (MailThing 4.69)\r\n\t(envelope-from <test@test.com>)\r\n\tid 1P2wr0-0003vw-U9\r\n\tfor test2@test.com; Tue, 05 Oct 2010 04:02:06 +0200\r\nTo: test2@test.com\r\nSubject: CRON-APT completed on test-server [/etc/auto-apt/configuration]\r\nMessage-Id: <E1P2wr0-0003vw-U9@test.com>\r\nFrom: test <test@test.com>\r\nDate: Tue, 05 Oct 2010 04:02:06 +0200";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "CRON-APT RUN [/etc/auto-apt/configuration]: Tue Oct  5 04:00:01 CEST 2010\r\nCRON-APT SLEEP: 116, Tue Oct  5 04:01:57 CEST 2010\r\nCRON-APT ACTION: 3-download\r\nCRON-APT LINE: /user/bin/apt-get dist-upgrade -d -y -o APT::Get::Show-Upgraded=true\r\nReading package lists...\r\nBuilding dependency tree...\r\nReading state information...\r\nThe following packages will be upgraded:\r\n  libaprutil1 libfreetype6\r\n2 upgraded, 0 newly installed, 0 to remove and 0 not upgraded.\r\nNeed to get 445kB of archives.\r\nAfter this operation, 4096B of additional disk space will be used.\r\nGet:1 http://security.debian.org Lenny/updates/main libaprutil1 1.2.12+DFSG-8+lenny5 [74.0kB]\r\nGet:2 http://security.debian.org Lenny/updates/main libfreetype6 2.3.7-2+lenny4 [371kB]\r\nFetched 445kB in 0s (1022kB/s)\r\nDownload complete and in download only mode\r\n";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n"  + messageBody + "\r\n" + messageEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);
			
			Assert.AreEqual("CRON-APT completed on test-server [/etc/auto-apt/configuration]", message.Headers.Subject);
			Assert.AreEqual("E1P2wr0-0003vw-U9@test.com", message.Headers.MessageId);

			// The Date header was:
			// Date: Tue, 05 Oct 2010 04:02:06 +0200
			// The +0200 is the same as going back two hours in UTC
			Assert.AreEqual(new DateTime(2010, 10, 5, 2, 2, 6, DateTimeKind.Utc), message.Headers.DateSent);
			Assert.AreEqual("Tue, 05 Oct 2010 04:02:06 +0200", message.Headers.Date);

			Assert.NotNull(message.Headers.From);
			Assert.AreEqual("test@test.com", message.Headers.From.Address);
			Assert.AreEqual("test", message.Headers.From.DisplayName);

			// There should only be one receiver
			Assert.NotNull(message.Headers.To);
			Assert.AreEqual(1, message.Headers.To.Count);
			Assert.AreEqual("test2@test.com", message.Headers.To[0].Address);
			Assert.IsEmpty(message.Headers.To[0].DisplayName);

			Assert.NotNull(message.Headers.ReturnPath);
			Assert.AreEqual("test@test.com", message.Headers.ReturnPath.Address);
			Assert.IsEmpty(message.Headers.ReturnPath.DisplayName);

			// There should only be one body
			Assert.NotNull(message.MessagePart);
			Assert.AreEqual(messageBody, message.MessagePart.GetBodyAsText());
			// Even though there is no declaration saying the type is text/plain, this is the default if nothing is supplied
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}

		/// <summary>
		/// Tests a real email between Kasper and John
		/// </summary>
		[Test]
		public void TestGetMessageIso88591()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Return-Path: <nhojmc@spam.gmail.com>\r\nReceived: from fep22 ([80.160.76.226]) by fep31.mail.dk\r\n          (InterMail vM.7.09.02.02 201-2219-117-103-20090326) with ESMTP\r\n          id <20101018210945.WKYX19924.fep31.mail.dk@fep22>\r\n          for <thefeds@spam.mail.dk>; Mon, 18 Oct 2010 23:09:45 +0200\r\nX-TDC-Received-From-IP: 74.125.82.54\r\nX-TDCICM: v=1.1 cv=f/0wZEcxj9tnJ8pax90Ax24drQNytfp8yOyhRTrlZkQ= c=1 sm=1 a=8nJEP1OIZ-IA:10 a=AP5iSteITFkr-SbgjHQA:9 a=ev7MReTRFKXz7Gsr7Zgb0CdRMZcA:4 a=wPNLvfGTeEIA:10 a=1PqlE-0FaytecYBE:21 a=bZoohpooc2ldffvA:21 a=rxAKiTD8bQmauZVlwM9vwA==:117\r\nX-TDC-RCPTTO: thefeds@spam.mail.dk\r\nX-TDC-FROM: nhojmc@spam.gmail.com\r\nReceived: from [74.125.82.54] ([74.125.82.54:63778] helo=mail-ww0-f54.google.com)\r\n\tby fep22 (envelope-from <nhojmc@spam.gmail.com>)\r\n\t(ecelerity 2.2.2.45 r()) with ESMTP\r\n\tid 92/2D-17911-897BCBC4; Mon, 18 Oct 2010 23:09:45 +0200\r\nReceived: by wwb39 with SMTP id 39so482967wwb.35\r\n        for <thefeds@spam.mail.dk>; Mon, 18 Oct 2010 14:09:42 -0700 (PDT)\r\nDKIM-Signature: v=1; a=rsa-sha256; c=relaxed/relaxed;\r\n        d=gmail.com; s=gamma;\r\n        h=domainkey-signature:mime-version:received:received:date:message-id\r\n         :subject:from:to:content-type;\r\n        bh=Xbyk5CmNRvc3U3s+wNmr55cx9fVqL9C82Dw3trI+OUA=;\r\n        b=CguczhTSNbLI1IOWFbFoExmIMnJPoU54mQUD7GyP7uK3B6dzews4jWP60jvWVmq/15\r\n         cmE9f08W2hLMsI6VtLtbPsOq/WVjVRK9A0sikvyCCxDdBy141Al94Ef0fAwt77Fc7jLW\r\n         YWLuM5PNjxjNjsw4D6pVvhfRcLArERrhrCXxw=\r\nDomainKey-Signature: a=rsa-sha1; c=nofws;\r\n        d=gmail.com; s=gamma;\r\n        h=mime-version:date:message-id:subject:from:to:content-type;\r\n        b=jVEyGC3V7FnaxCiZyHtOuPTe5goCPTIqdbJZ3TE/k1mAaS/gaQwHdJJrZNH1Zqi81+\r\n         kHAI86Z+o/raYZM51gdzhBg7DcuN2FgLnfnlncbAtNDQxR/CadLsL/OFKBg2CpgszGXA\r\n         vlMPszRP7C658j5v38dM8J4p6Q86nAnem7v6g=\r\nMIME-Version: 1.0\r\nReceived: by 10.227.145.70 with SMTP id c6mr1938128wbv.106.1287436181523; Mon,\r\n 18 Oct 2010 14:09:41 -0700 (PDT)\r\nReceived: by 10.227.146.13 with HTTP; Mon, 18 Oct 2010 14:09:41 -0700 (PDT)\r\nDate: Mon, 18 Oct 2010 17:09:41 -0400\r\nMessage-ID: <AANLkTik0O_9JZCeS7Za__w_G6L=9jKq2=BQKnqHVXAQo@mail.gmail.com>\r\nSubject: Email Addresses\r\nFrom: John McDaniel <nhojmc@spam.gmail.com>\r\nTo: Kasper Foens <thefeds@spam.mail.dk>\r\nContent-Type: text/plain; charset=ISO-8859-1";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "I have run into an issue with the email addresses. It seems one of the\r\ncases my QA dept ran across is when the Email address is not of the\r\nform x@y. I fixed the case where the code would throw an exception on\r\nan invalid email, but I see where I am going to need access to the\r\noriginal address strings.  I\'m hesitant to add properties to the\r\nMessageHeader such as RawTo, RawFrom  but I really don\'t see a better\r\nway of getting at that information. Any thoughts? Maybe keeping the\r\nheader items in a publicly accessible dictionary???\r\n\r\nJohn\r\n";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody + "\r\n" + messageEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.GetEncoding("ISO-8859-1").GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("something", "else");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);

			Assert.AreEqual("Email Addresses", message.Headers.Subject);
			Assert.AreEqual("AANLkTik0O_9JZCeS7Za__w_G6L=9jKq2=BQKnqHVXAQo@mail.gmail.com", message.Headers.MessageId);

			Assert.AreEqual("1.0", message.Headers.MimeVersion);

			// Testing a custom header
			Assert.NotNull(message.Headers.UnknownHeaders);
			string[] tdcHeader = message.Headers.UnknownHeaders.GetValues("X-TDC-Received-From-IP");
			Assert.NotNull(tdcHeader);

			// This is to stop content assist from nagging me. It is clear that it is not null now
			// since the Assert above would have failed then
			if (tdcHeader != null)
			{
				Assert.AreEqual(1, tdcHeader.Length);
				Assert.AreEqual("74.125.82.54", tdcHeader[0]);
			}

			Assert.NotNull(message.Headers.ContentType);
			Assert.NotNull(message.Headers.ContentType.CharSet);
			Assert.AreEqual("ISO-8859-1", message.Headers.ContentType.CharSet);
			Assert.NotNull(message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			// The Date header was:
			// Date: Mon, 18 Oct 2010 17:09:41 -0400
			// The -0400 is the same as adding 4 hours in UTC
			Assert.AreEqual(new DateTime(2010, 10, 18, 21, 9, 41, DateTimeKind.Utc), message.Headers.DateSent);
			Assert.AreEqual("Mon, 18 Oct 2010 17:09:41 -0400", message.Headers.Date);

			Assert.NotNull(message.Headers.From);
			Assert.AreEqual("nhojmc@spam.gmail.com", message.Headers.From.Address);
			Assert.AreEqual("John McDaniel", message.Headers.From.DisplayName);

			// There should only be one receiver
			Assert.NotNull(message.Headers.To);
			Assert.AreEqual(1, message.Headers.To.Count);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.To[0].Address);
			Assert.AreEqual("Kasper Foens", message.Headers.To[0].DisplayName);

			Assert.NotNull(message.Headers.ReturnPath);
			Assert.AreEqual("nhojmc@spam.gmail.com", message.Headers.ReturnPath.Address);
			Assert.IsEmpty(message.Headers.ReturnPath.DisplayName);

			// There should only be one body
			Assert.AreEqual(messageBody, message.MessagePart.GetBodyAsText());
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}

		/// <summary>
		/// Base64 string from http://en.wikipedia.org/wiki/Base64#Examples
		/// </summary>
		[Test]
		public void TestGetMessageBase64()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Return-Path: <thefeds@spam.mail.dk>\r\nMessage-ID: <4CBACC87.8080600@mail.dk>\r\nDate: Sun, 17 Oct 2010 12:14:31 +0200\r\nFrom: =?ISO-8859-1?Q?Kasper_F=F8ns?= <thefeds@spam.mail.dk>\r\nMIME-Version: 1.0\r\nTo: =?ISO-8859-1?Q?Kasper_F=F8ns?= <thefeds@spam.mail.dk>\r\nSubject: Test =?ISO-8859-1?Q?=E6=F8=E5=C6=D8=C5?=\r\nContent-Type: text/plain; charset=US-ASCII;\r\nContent-Transfer-Encoding: base64";
			const string messageHeaderToBodyDelimiter = "";
			// Removed last K for the \n and added == for padding
			const string messageBody = "TWFuIGlzIGRpc3Rpbmd1aXNoZWQsIG5vdCBvbmx5IGJ5IGhpcyByZWFzb24sIGJ1dCBieSB0aGlz\r\nIHNpbmd1bGFyIHBhc3Npb24gZnJvbSBvdGhlciBhbmltYWxzLCB3aGljaCBpcyBhIGx1c3Qgb2Yg\r\ndGhlIG1pbmQsIHRoYXQgYnkgYSBwZXJzZXZlcmFuY2Ugb2YgZGVsaWdodCBpbiB0aGUgY29udGlu\r\ndWVkIGFuZCBpbmRlZmF0aWdhYmxlIGdlbmVyYXRpb24gb2Yga25vd2xlZGdlLCBleGNlZWRzIHRo\r\nZSBzaG9ydCB2ZWhlbWVuY2Ugb2YgYW55IGNhcm5hbCBwbGVhc3VyZS4=";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody + "\r\n" + messageEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("something", "else");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);

			Assert.AreEqual("Test æøåÆØÅ", message.Headers.Subject);
			Assert.AreEqual("4CBACC87.8080600@mail.dk", message.Headers.MessageId);

			Assert.AreEqual("1.0", message.Headers.MimeVersion);

			Assert.NotNull(message.Headers.ContentType);
			Assert.NotNull(message.Headers.ContentType.CharSet);
			Assert.AreEqual("US-ASCII", message.Headers.ContentType.CharSet);
			Assert.NotNull(message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			// We are using base64 as encoding
			Assert.AreEqual(ContentTransferEncoding.Base64, message.Headers.ContentTransferEncoding);

			// The Date header was:
			// Date: Sun, 17 Oct 2010 12:14:31 +0200
			// The +0200 is the same as substracting 2 hours in UTC
			Assert.AreEqual(new DateTime(2010, 10, 17, 10, 14, 31, DateTimeKind.Utc), message.Headers.DateSent);
			Assert.AreEqual("Sun, 17 Oct 2010 12:14:31 +0200", message.Headers.Date);

			Assert.NotNull(message.Headers.From);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.From.Address);
			Assert.AreEqual("Kasper Føns", message.Headers.From.DisplayName);

			// There should only be one receiver
			Assert.NotNull(message.Headers.To);
			Assert.AreEqual(1, message.Headers.To.Count);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.To[0].Address);
			Assert.AreEqual("Kasper Føns", message.Headers.To[0].DisplayName);

			Assert.NotNull(message.Headers.ReturnPath);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.ReturnPath.Address);
			Assert.IsEmpty(message.Headers.ReturnPath.DisplayName);

			const string expectedOutput = "Man is distinguished, not only by his reason, but by this singular passion from other animals, which is a lust of the mind, that by a perseverance of delight in the continued and indefatigable generation of knowledge, exceeds the short vehemence of any carnal pleasure.";

			// There should only be one body
			Assert.AreEqual(expectedOutput, message.MessagePart.GetBodyAsText());
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}

		/// <summary>
		/// Tests a ISO-8859-1 email which has special characters in the body
		/// </summary>
		[Test]
		public void TestGetMessageIso88591SpecialChars()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Return-Path: <thefeds@spam.mail.dk>\r\nReceived: from fep28 ([80.160.76.232]) by fep30.mail.dk\r\n          (InterMail vM.7.09.02.02 201-2219-117-103-20090326) with ESMTP\r\n          id <20101017101437.WEXY2819.fep30.mail.dk@fep28>\r\n          for <thefeds@spam.mail.dk>; Sun, 17 Oct 2010 12:14:37 +0200\r\nReceived: from [195.41.46.142] ([195.41.46.142:41886] helo=fep41.mail.dk)\r\n\tby fep28 (envelope-from <thefeds@spam.mail.dk>)\r\n\t(ecelerity 2.2.2.45 r()) with ESMTP\r\n\tid 88/D0-14647-D8CCABC4; Sun, 17 Oct 2010 12:14:37 +0200\r\nReceived: from [87.48.47.215] ([87.48.47.215:49596] helo=[192.168.0.234])\r\n\tby fep41.mail.dk (envelope-from <thefeds@spam.mail.dk>)\r\n\t(ecelerity 2.2.2.45 r()) with ESMTPA\r\n\tid 7F/2E-18479-B8CCABC4; Sun, 17 Oct 2010 12:14:35 +0200\r\nMessage-ID: <4CBACC87.8080600@spam.mail.dk>\r\nDate: Sun, 17 Oct 2010 12:14:31 +0200\r\nFrom: =?ISO-8859-1?Q?Kasper_F=F8ns?= <thefeds@spam.mail.dk>\r\nUser-Agent: Mozilla/5.0 (Windows; U; Windows NT 6.1; en-US; rv:1.9.2.9) Gecko/20100915 Thunderbird/3.1.4\r\nMIME-Version: 1.0\r\nTo: =?ISO-8859-1?Q?Kasper_F=F8ns?= <thefeds@spam.mail.dk>\r\nSubject: Test =?ISO-8859-1?Q?=E6=F8=E5=C6=D8=C5?=\r\nContent-Type: text/plain; charset=ISO-8859-1; format=flowed\r\nContent-Transfer-Encoding: 8bit";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "This is a test message. It contains some ISO-8859-1 characters like:\r\nÆØÅæøå\r\nWhich is Danish characters\r\n\r\nRegards\r\nKasper Føns\r\n";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody + "\r\n" + messageEnd + "\r\n";

			// We want to use a normal stream instead of a StringReader.
			// Therefore we convert our message and responses into a byte array.
			// We can use the encoding ISO-8859-1 on all the strings since
			// all the parts that are compaitible with ASCII can be decoded using ASCII
			// But the message body's special characters will be encoding using ISO-8859-1, which
			// is the encoding mentioned in the content-type charset property.
			byte[] serverResponsesInBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(serverResponses);

			Stream inputStream = new MemoryStream(serverResponsesInBytes);
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("user", "password");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);

			Assert.AreEqual("Test æøåÆØÅ", message.Headers.Subject);
			Assert.AreEqual("4CBACC87.8080600@spam.mail.dk", message.Headers.MessageId);

			Assert.AreEqual("1.0", message.Headers.MimeVersion);

			Assert.NotNull(message.Headers.ContentType);
			Assert.NotNull(message.Headers.ContentType.CharSet);
			Assert.AreEqual("ISO-8859-1", message.Headers.ContentType.CharSet);
			Assert.NotNull(message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			// The Date header was:
			// Sun, 17 Oct 2010 12:14:31 +0200
			// The +0200 is the same as substracting 2 hours in UTC
			Assert.AreEqual(new DateTime(2010, 10, 17, 10, 14, 31, DateTimeKind.Utc), message.Headers.DateSent);
			Assert.AreEqual("Sun, 17 Oct 2010 12:14:31 +0200", message.Headers.Date);

			Assert.NotNull(message.Headers.From);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.From.Address);
			Assert.AreEqual("Kasper Føns", message.Headers.From.DisplayName);

			// There should only be one receiver
			Assert.NotNull(message.Headers.To);
			Assert.AreEqual(1, message.Headers.To.Count);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.To[0].Address);
			Assert.AreEqual("Kasper Føns", message.Headers.To[0].DisplayName);

			Assert.NotNull(message.Headers.ReturnPath);
			Assert.AreEqual("thefeds@spam.mail.dk", message.Headers.ReturnPath.Address);
			Assert.IsEmpty(message.Headers.ReturnPath.DisplayName);

			// There should only be one body
			Assert.AreEqual(messageBody, message.MessagePart.GetBodyAsText());
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}

		/// <summary>
		/// Tests a ISO-8859-2 email which has special characters in the body
		/// </summary>
		[Test]
		public void TestGetMessageIso88592()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Content-Type: text/plain; charset=ISO-8859-2; format=flowed\r\nContent-Transfer-Encoding: 8bit";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "This is a test message. It contains some ISO-8859-2 characters like:\r\nŚŞř\r\nWhich is Danish characters\r\n\r\nRegards\r\nKasper Foens\r\n";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody + "\r\n" + messageEnd + "\r\n";

			// We want to use a normal stream instead of a StringReader.
			// Therefore we convert our message and responses into a byte array.
			// We can use the encoding ISO-8859-2 on all the strings since
			// all the parts that are compaitible with ASCII can be decoded using ASCII
			// But the message body's special characters will be encoding using ISO-8859-2, which
			// is the encoding mentioned in the content-type charset property.
			byte[] serverResponsesInBytes = Encoding.GetEncoding("ISO-8859-2").GetBytes(serverResponses);

			Stream inputStream = new MemoryStream(serverResponsesInBytes);
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("user", "password");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);

			Assert.NotNull(message.Headers.ContentType);
			Assert.NotNull(message.Headers.ContentType.CharSet);
			Assert.AreEqual("ISO-8859-2", message.Headers.ContentType.CharSet);
			Assert.NotNull(message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			// There should only be one body
			Assert.AreEqual(messageBody, message.MessagePart.GetBodyAsText());
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}

		[Test]
		public void TestGetMessageWindows1252()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Content-Type: text/plain; charset=windows-1252; format=flowed\r\nContent-Transfer-Encoding: 8bit";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "\x00C5\x00F7\x0192";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody + "\r\n" + messageEnd + "\r\n";

			// We want to use a normal stream instead of a StringReader.
			// Therefore we convert our message and responses into a byte array.
			// We can use the encoding ISO-8859-2 on all the strings since
			// all the parts that are compaitible with ASCII can be decoded using ASCII
			// But the message body's special characters will be encoding using ISO-8859-2, which
			// is the encoding mentioned in the content-type charset property.
			byte[] serverResponsesInBytes = Encoding.GetEncoding(1252).GetBytes(serverResponses);

			Stream inputStream = new MemoryStream(serverResponsesInBytes);
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("user", "password");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);

			Assert.NotNull(message.Headers.ContentType);
			Assert.NotNull(message.Headers.ContentType.CharSet);
			Assert.AreEqual("windows-1252", message.Headers.ContentType.CharSet);
			Assert.NotNull(message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			// http://en.wikipedia.org/wiki/Windows-1254
			const string expectedBody = "Å÷ƒ";

			// There should only be one body
			Assert.AreEqual(expectedBody, message.MessagePart.GetBodyAsText());
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}

		[Test]
		public void TestGetMessageUTF8()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Content-Type: text/plain; charset=UTF-8; format=flowed\r\nContent-Transfer-Encoding: 8bit";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "ÆØÅnŚŞřÅ÷ƒ\u2D8C";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody + "\r\n" + messageEnd + "\r\n";

			// We want to use a normal stream instead of a StringReader.
			// Therefore we convert our message and responses into a byte array.
			// We can use the encoding ISO-8859-2 on all the strings since
			// all the parts that are compaitible with ASCII can be decoded using ASCII
			// But the message body's special characters will be encoding using ISO-8859-2, which
			// is the encoding mentioned in the content-type charset property.
			byte[] serverResponsesInBytes = Encoding.UTF8.GetBytes(serverResponses);

			Stream inputStream = new MemoryStream(serverResponsesInBytes);
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("user", "password");

			Message message = client.GetMessage(132);

			Assert.NotNull(message);
			Assert.NotNull(message.Headers);

			Assert.NotNull(message.Headers.ContentType);
			Assert.NotNull(message.Headers.ContentType.CharSet);
			Assert.AreEqual("UTF-8", message.Headers.ContentType.CharSet);
			Assert.NotNull(message.Headers.ContentType.MediaType);
			Assert.AreEqual("text/plain", message.Headers.ContentType.MediaType);

			// There should only be one body
			Assert.AreEqual(messageBody, message.MessagePart.GetBodyAsText());
			Assert.AreEqual("text/plain", message.MessagePart.ContentType.MediaType);
		}
		
		/// <summary>
		/// http://tools.ietf.org/html/rfc1939#page-11
		/// </summary>
		[Test]
		public void TestGetMessageHeaders()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string messageTopAccepted = "+OK";
			const string messageHeaders = "Subject: [Blinded by the lights] New Comment On: Comparison of .Net libraries for fetching emails via POP3";
			const string messageHeaderToBodyDelimiter = ""; // Blank line ends message
			const string messageListingEnd = ".";
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + messageTopAccepted + "\r\n" + messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageListingEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("test", "test");

			// Fetch the header of message 7
			MessageHeader header = client.GetMessageHeaders(7);

			const string expectedSubject = "[Blinded by the lights] New Comment On: Comparison of .Net libraries for fetching emails via POP3";
			string subject = header.Subject;

			Assert.AreEqual(expectedSubject, subject);
		}

		[Test]
		public void TestGetMessageAsBytes()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageHeaders = "Return-Path: <thefeds@spam.mail.dk>";
			const string messageHeaderToBodyDelimiter = "";
			const string messageBody = "\r\nTest\r\n";
			const string messageEnd = ".";

			const string message = messageHeaders + "\r\n" + messageHeaderToBodyDelimiter + "\r\n" + messageBody;

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + message + "\r\n" + messageEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			byte[] expectedBytes = Encoding.ASCII.GetBytes(message);

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("something", "else");

			byte[] messageBytes = client.GetMessageAsBytes(132);

			Assert.AreEqual(expectedBytes, messageBytes);
		}

		/// <summary>
		/// When a message is received from a POP3 server, the server sends this:
		/// 
		/// +OK
		/// ::message here::
		/// .
		/// 
		/// The dot specifies that the message has ended.
		/// If somewhere in the message a single dot is on a line, then
		/// that dot would be seen as end of the message
		/// Therefore http://tools.ietf.org/html/rfc1939#section-3 says that it must
		/// be encoded by adding yet another dot.
		/// This dot should be removed when fetching the email - this is what this test is testing
		/// </summary>
		[Test]
		public void TestGetMessageAsBytesWithDotDot()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string messageEnd = ".";

			const string message		 = "Return-Path: <thefeds@spam.mail.dk>" + "\r\n" + "" + "\r\n" + "..";
			const string expectedMessage = "Return-Path: <thefeds@spam.mail.dk>" + "\r\n" + "" + "\r\n" + ".";
			
			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + message + "\r\n" + messageEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			// When a .. is expected as the first character of a line, it really means that it is only a .
			byte[] expectedBytes = Encoding.ASCII.GetBytes(expectedMessage);

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("something", "else");

			byte[] messageBytes = client.GetMessageAsBytes(139);

			Assert.AreEqual(expectedBytes, messageBytes);
		}

		[Test]
		public void TestFetchingMessageWithoutBodyWorks()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string okMessageFetch = "+OK";
			const string message = "Return-Path: <thefeds@spam.mail.dk>";
			const string messageEnd = ".";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + okMessageFetch + "\r\n" + message + "\r\n" + messageEnd + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			Stream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("something", "else");

			Message messageFetched = client.GetMessage(3);

			Assert.NotNull(messageFetched);
			Assert.IsEmpty(messageFetched.Headers.ReturnPath.DisplayName);
			Assert.AreEqual("thefeds@spam.mail.dk", messageFetched.Headers.ReturnPath.Address);

			Assert.NotNull(messageFetched.MessagePart);
			Assert.IsEmpty(messageFetched.MessagePart.Body);
		}

		[Test]
		public void TestCramMd5Login()
		{
			const string welcomeMessage = "+OK";
			const string challengeResponse = "+ PDE4OTYuNjk3MTcwOTUyQHBvc3RvZmZpY2UucmVzdG9uLm1jaS5uZXQ+";
			const string okCramMd5 = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + challengeResponse + "\r\n" + okCramMd5 + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("tim", "tanstaaftanstaaf", AuthenticationMethod.CramMd5);

			string[] commandsFromClient = GetCommands(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());
			Assert.NotNull(commandsFromClient);
			Assert.AreEqual(2, commandsFromClient.Length);

			const string expectedCommand = "AUTH CRAM-MD5";
			string actualCommand = commandsFromClient[0];
			Assert.AreEqual(expectedCommand, actualCommand);

			const string expectedResponse = "dGltIGI5MTNhNjAyYzdlZGE3YTQ5NWI0ZTZlNzMzNGQzODkw";
			string actualResponse = commandsFromClient[1];
			Assert.AreEqual(expectedResponse, actualResponse);
		}

		[Test]
		public void TestCramMd5LoginOtherUser()
		{
			const string welcomeMessage = "+OK";
			const string challengeResponse = "+ PHRoaXMuaXMudGhlLmJhc2U2NC5lbmNvZGVkLmNoYWxsZW5nZUBzZXJ2ZXIuY29tPg==";
			const string okCramMd5 = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + challengeResponse + "\r\n" + okCramMd5 + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			client.Authenticate("foens", "thisIsAnInsanelyLongPasswordManWoot", AuthenticationMethod.CramMd5);

			string[] commandsFromClient = GetCommands(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());
			Assert.NotNull(commandsFromClient);
			Assert.AreEqual(2, commandsFromClient.Length);

			const string expectedCommand = "AUTH CRAM-MD5";
			string actualCommand = commandsFromClient[0];
			Assert.AreEqual(expectedCommand, actualCommand);

			const string expectedResponse = "Zm9lbnMgNTAyNDU5OTU1NjMwNTliNWUxZWQyMmMzMzQzYzYxNDg=";
			string actualResponse = commandsFromClient[1];
			Assert.AreEqual(expectedResponse, actualResponse);
		}

		[Test]
		public void TestCramMd5LoginNotSupported()
		{
			const string welcomeMessage = "+OK";
			const string challengeResponse = "-ERR CRAM-MD5 is not supported on this server";

			const string serverResponses = welcomeMessage + "\r\n" + challengeResponse + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			Assert.Throws<NotSupportedException>(delegate { client.Authenticate("tim", "tanstaaftanstaaf", AuthenticationMethod.CramMd5); });

			string[] commandsFromClient = GetCommands(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());
			Assert.NotNull(commandsFromClient);
			// Check the the client only sent one command
			Assert.AreEqual(1, commandsFromClient.Length);

			const string expectedCommand = "AUTH CRAM-MD5";
			string actualCommand = commandsFromClient[0];
			Assert.AreEqual(expectedCommand, actualCommand);
		}

		[Test]
		public void TestCramMd5LoginNotCorrect()
		{
			const string welcomeMessage = "+OK";
			const string challengeResponse = "+ PDE4OTYuNjk3MTcwOTUyQHBvc3RvZmZpY2UucmVzdG9uLm1jaS5uZXQ+";
			const string okCramMd5 = "-ERR Login not correct";

			const string serverResponses = welcomeMessage + "\r\n" + challengeResponse + "\r\n" + okCramMd5 + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			Assert.Throws<InvalidLoginException>(delegate { client.Authenticate("tim", "tanstaaftanstaaf", AuthenticationMethod.CramMd5); });
		}

		[Test]
		public void TestCramMd5LoginCorrectButMaildropLocked()
		{
			const string welcomeMessage = "+OK";
			const string challengeResponse = "+ PDE4OTYuNjk3MTcwOTUyQHBvc3RvZmZpY2UucmVzdG9uLm1jaS5uZXQ+";
			const string okCramMd5 = "-ERR The maildrop is locked, please try later";

			const string serverResponses = welcomeMessage + "\r\n" + challengeResponse + "\r\n" + okCramMd5 + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));
			Assert.Throws<PopServerLockedException>(delegate { client.Authenticate("tim", "tanstaaftanstaaf", AuthenticationMethod.CramMd5); });
		}

		[Test]
		public void TestDisposeSendsQuit()
		{
			const string welcomeMessage = "+OK";
			const string quitOk = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + quitOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			using (Pop3Client client = new Pop3Client())
			{
				client.Connect(new CombinedStream(inputStream, outputStream));
			}

			// Check that a command was sent
			Assert.IsNotEmpty(outputStream.ToArray());

			const string expectedCommand = "QUIT";
			string actualCommand = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

			// Check last command was QUIT
			Assert.AreEqual(expectedCommand, actualCommand);
		}

		[Test]
		public void TestCapabilityRfcExample()
		{
			const string welcomeMessage = "+OK";
			const string capabilityResponse =
				"+OK Capability list follows\r\n" +
				"TOP\r\n" +
				"USER\r\n" +
				"SASL CRAM-MD5 KERBEROS_V4\r\n" +
				"RESP-CODES\r\n" +
				"LOGIN-DELAY 900\r\n" +
				"PIPELINING\r\n" +
				"EXPIRE 60\r\n" +
				"UIDL\r\n" +
				"IMPLEMENTATION Shlemazle-Plotz-v302\r\n" +
				".";
			const string quitOk = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + capabilityResponse + "\r\n" + quitOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			using (Pop3Client client = new Pop3Client())
			{
				client.Connect(new CombinedStream(inputStream, outputStream));

				Dictionary<string, List<string>> capabilities = client.Capabilities();

				// Check the capabilities received was correct
				Assert.Contains("TOP", capabilities.Keys);
				Assert.Contains("USER", capabilities.Keys);
				Assert.Contains("SASL", capabilities.Keys);
				Assert.Contains("RESP-CODES", capabilities.Keys);
				Assert.Contains("LOGIN-DELAY", capabilities.Keys);
				Assert.Contains("PIPELINING", capabilities.Keys);
				Assert.Contains("EXPIRE", capabilities.Keys);
				Assert.Contains("UIDL", capabilities.Keys);
				Assert.Contains("IMPLEMENTATION", capabilities.Keys);
				Assert.AreEqual(9, capabilities.Keys.Count);

				// Check arguments
				Assert.IsEmpty(capabilities["TOP"]);
				Assert.IsEmpty(capabilities["USER"]);

				List<string> saslArguments = capabilities["SASL"];
				Assert.NotNull(saslArguments);
				Assert.Contains("CRAM-MD5", saslArguments);
				Assert.Contains("KERBEROS_V4", saslArguments);
				Assert.AreEqual(2, saslArguments.Count);

				Assert.IsEmpty(capabilities["RESP-CODES"]);

				List<string> loginDelayArguments = capabilities["LOGIN-DELAY"];
				Assert.NotNull(loginDelayArguments);
				Assert.Contains("900", loginDelayArguments);
				Assert.AreEqual(1, loginDelayArguments.Count);

				Assert.IsEmpty(capabilities["PIPELINING"]);

				List<string> expireArguments = capabilities["EXPIRE"];
				Assert.NotNull(expireArguments);
				Assert.Contains("60", expireArguments);
				Assert.AreEqual(1, expireArguments.Count);

				Assert.IsEmpty(capabilities["UIDL"]);

				List<string> implementationArguments = capabilities["IMPLEMENTATION"];
				Assert.NotNull(implementationArguments);
				Assert.Contains("Shlemazle-Plotz-v302", implementationArguments);
				Assert.AreEqual(1, implementationArguments.Count);

				// Check the correct command was sent
				const string expectedCommand = "CAPA";
				string actualCommand = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

				Assert.AreEqual(expectedCommand, actualCommand);
			}
		}

		[Test]
		public void TestCapabilityOtherCapabilities()
		{
			const string welcomeMessage = "+OK";
			const string capabilityResponse =
				"+OK Capability list follows\r\n" +
				"FOO\r\n" +
				"BAR BAZ\r\n" +
				"SSS SSS1 SSS2\r\n" +
				"IMPLEMENTATION Foo-bar-baz-Server\r\n" +
				".";
			const string quitOk = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + capabilityResponse + "\r\n" + quitOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			using (Pop3Client client = new Pop3Client())
			{
				client.Connect(new CombinedStream(inputStream, outputStream));

				Dictionary<string, List<string>> capabilities = client.Capabilities();

				// Check the capabilities received was correct
				Assert.Contains("FOO", capabilities.Keys);
				Assert.Contains("BAR", capabilities.Keys);
				Assert.Contains("SSS", capabilities.Keys);
				Assert.Contains("IMPLEMENTATION", capabilities.Keys);
				Assert.AreEqual(4, capabilities.Keys.Count);

				// Check arguments
				Assert.IsEmpty(capabilities["FOO"]);

				List<string> barArguments = capabilities["BAR"];
				Assert.NotNull(barArguments);
				Assert.Contains("BAZ", barArguments);
				Assert.AreEqual(1, barArguments.Count);

				List<string> sssArguments = capabilities["SSS"];
				Assert.NotNull(sssArguments);
				Assert.Contains("SSS1", sssArguments);
				Assert.Contains("SSS2", sssArguments);
				Assert.AreEqual(2, sssArguments.Count);

				List<string> implementationArguments = capabilities["IMPLEMENTATION"];
				Assert.NotNull(implementationArguments);
				Assert.Contains("Foo-bar-baz-Server", implementationArguments);
				Assert.AreEqual(1, implementationArguments.Count);

				// Check the correct command was sent
				const string expectedCommand = "CAPA";
				string actualCommand = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

				Assert.AreEqual(expectedCommand, actualCommand);
			}
		}

		[Test]
		public void TestCapabilityInTransactionState()
		{
			const string welcomeMessage = "+OK";
			const string okUsername = "+OK";
			const string okPassword = "+OK";
			const string capabilityResponse =
				"+OK Capability list follows\r\n" +
				"TEST\r\n" +
				"TEST2 TEST\r\n" +
				".";
			const string quitOk = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + okUsername + "\r\n" + okPassword + "\r\n" + capabilityResponse + "\r\n" + quitOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			using (Pop3Client client = new Pop3Client())
			{
				client.Connect(new CombinedStream(inputStream, outputStream));

				client.Authenticate("user", "password");

				Dictionary<string, List<string>> capabilities = client.Capabilities();

				// Check the capabilities received was correct
				Assert.Contains("TEST", capabilities.Keys);
				Assert.Contains("TEST2", capabilities.Keys);
				Assert.AreEqual(2, capabilities.Keys.Count);

				// Check arguments
				Assert.IsEmpty(capabilities["TEST"]);

				List<string> test2Arguments = capabilities["TEST2"];
				Assert.NotNull(test2Arguments);
				Assert.Contains("TEST", test2Arguments);
				Assert.AreEqual(1, test2Arguments.Count);

				// Check the correct command was sent
				const string expectedCommand = "CAPA";
				string actualCommand = GetLastCommand(new StreamReader(new MemoryStream(outputStream.ToArray())).ReadToEnd());

				Assert.AreEqual(expectedCommand, actualCommand);
			}
		}

		[Test]
		public void TestCapabilityCaseInsensitivity()
		{
			const string welcomeMessage = "+OK";
			const string capabilityResponse =
				"+OK Capability list follows\r\n" +
				"TEST\r\n" +
				"TEST2 TEST\r\n" +
				"aBcDeFg\r\n" +
				".";
			const string quitOk = "+OK";

			const string serverResponses = welcomeMessage + "\r\n" + capabilityResponse + "\r\n" + quitOk + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			using (Pop3Client client = new Pop3Client())
			{
				client.Connect(new CombinedStream(inputStream, outputStream));

				Dictionary<string, List<string>> capabilities = client.Capabilities();

				// Check the capabilities received was correct
				Assert.IsTrue(capabilities.ContainsKey("test"));
				Assert.IsTrue(capabilities.ContainsKey("TeSt2"));
				Assert.IsTrue(capabilities.ContainsKey("aBcDeFg"));
				Assert.IsTrue(capabilities.ContainsKey("AbCdEfG"));
				Assert.AreEqual(3, capabilities.Keys.Count);

				// Check arguments
				Assert.IsEmpty(capabilities["TEST"]);

				List<string> test2Arguments = capabilities["tEsT2"];
				Assert.NotNull(test2Arguments);
				Assert.Contains("TEST", test2Arguments);
				Assert.AreEqual(1, test2Arguments.Count);

				Assert.IsEmpty(capabilities["aBcDeFg"]);
			}
		}

		[Test]
		public void TestAccountLoginDelayResponseApop()
		{
			const string welcomeMessage = "+OK POP3 server ready <1896.697170952@dbc.mtview.ca.us>";
			const string loginMessage = "-ERR [LOGIN-DELAY] wait some time before loggin in";
			const string serverResponses = welcomeMessage + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			Assert.Throws<LoginDelayException>(delegate { client.Authenticate("foo", "bar", AuthenticationMethod.Apop); });
		}

		[Test]
		public void TestAccountLoginDelayResponseCramMd5()
		{
			const string welcomeMessage = "+OK";
			const string challengeResponse = "+ PDE4OTYuNjk3MTcwOTUyQHBvc3RvZmZpY2UucmVzdG9uLm1jaS5uZXQ+";
			const string loginDelay = "-ERR [LOGIN-DELAY]";

			const string serverResponses = welcomeMessage + "\r\n" + challengeResponse + "\r\n" + loginDelay + "\r\n";

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));
			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			Assert.Throws<LoginDelayException>(delegate { client.Authenticate("tim", "tanstaaftanstaaf", AuthenticationMethod.CramMd5); });
		}

		[Test]
		public void TestAccountLoginDelayResponseUsernamePassword()
		{
			const string welcomeMessage = "+OK POP3 server ready <1896.697170952@dbc.mtview.ca.us>";
			const string userOk = "+OK";
			const string loginMessage = "-ERR [LOGIN-DELAY] wait some time before loggin in";
			const string serverResponses = welcomeMessage + "\r\n" + userOk + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			Assert.Throws<LoginDelayException>(delegate { client.Authenticate("foo", "bar", AuthenticationMethod.UsernameAndPassword); });
		}

		[Test]
		public void TestAccountLoginDelayResponseUsernamePasswordInsensitiveCasing()
		{
			const string welcomeMessage = "+OK POP3 server ready <1896.697170952@dbc.mtview.ca.us>";
			const string userOk = "+OK";
			const string loginMessage = "-ERR [login-DeLay] wait some time before loggin in";
			const string serverResponses = welcomeMessage + "\r\n" + userOk + "\r\n" + loginMessage + "\r\n";
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(serverResponses));

			MemoryStream outputStream = new MemoryStream();

			Pop3Client client = new Pop3Client();
			client.Connect(new CombinedStream(inputStream, outputStream));

			Assert.Throws<LoginDelayException>(delegate { client.Authenticate("foo", "bar", AuthenticationMethod.UsernameAndPassword); });
		}

		/// <summary>
		/// Helper method to get the last line from a <see cref="StringBuilder"/>
		/// which is the last line that the client has sent.
		/// </summary>
		/// <param name="builder">The builder to get the last line from</param>
		/// <returns>A single line, which is the last one in the builder</returns>
		private static string GetLastCommand(string builder)
		{
			string[] commands = GetCommands(builder);
			return commands[commands.Length - 1];
		}

		/// <summary>
		/// Helper method to get a string array of the commands issued by a client.
		/// </summary>
		/// <param name="builder">The builder to get the commands from</param>
		/// <returns>A string array where each entry is a command</returns>
		private static string[] GetCommands(string builder)
		{
			return builder.Split(new[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries);
		}
	}
}