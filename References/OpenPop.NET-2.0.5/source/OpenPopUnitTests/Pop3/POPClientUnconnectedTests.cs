using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenPop.Pop3;
using OpenPop.Pop3.Exceptions;

namespace OpenPopUnitTests.Pop3
{
	/// <summary>
	/// This TestFixture is testing that when the <see cref="Pop3Client"/> is in an unconnected state, commands which may not
	/// be used in that state, throws <see cref="InvalidUseException"/>.
	/// 
	/// Also tests that commands which is can be used in this state, does not throw any exceptions
	/// </summary>
	[TestFixture]
	class POPClientUnconnectedTests
	{
		private const int RandomMessageNumber = 5;
		private const string RandomString = "random";

		private Pop3Client Client;

		[SetUp]
		public void Init()
		{
			Client = new Pop3Client();
		}

		[Test]
		public void TestAuthenticate()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.Authenticate(RandomString, RandomString); });
		}

		[Test]
		public void TestDeleteAllMessages()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.DeleteAllMessages(); });
		}

		[Test]
		public void TestDeleteMessage()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.DeleteMessage(RandomMessageNumber); });
		}

		[Test]
		public void TestDisconnect()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.Disconnect(); });
		}

		[Test]
		public void TestGetMessage()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessage(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageCount()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageCount(); });
		}

		[Test]
		public void TestGetMessageHeaders()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageHeaders(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageSize()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageSize(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageSizes()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageSizes(); });
		}

		[Test]
		public void TestGetMessageUid()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageUid(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageUids()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageUids(); });
		}

		[Test]
		public void TestNoOperation()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.NoOperation(); });
		}

		[Test]
		public void TestReset()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.Reset(); });
		}

		[Test]
		public void TestConnectWorks()
		{
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes("+OK")); // Welcome message
			Stream outputStream = new MemoryStream();

			Assert.DoesNotThrow(delegate { Client.Connect(new CombinedStream(inputStream, outputStream)); });
		}

		[Test]
		public void TestConnectWorksThrowsCorrectExceptionWhenServerDoesNotRespond()
		{
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes("")); // No welcome message
			Stream outputStream = new MemoryStream();

			// There was a problem, where an InvalidUseException was being thrown, if the server
			// did not respond or the stream was closed. That was the wrong exception
			Assert.Throws(typeof(PopServerNotAvailableException), delegate { Client.Connect(new CombinedStream(inputStream, outputStream)); });
		}

		[Test]
		public void TestCapability()
		{
			Assert.Throws(typeof(InvalidUseException), delegate { Client.Capabilities(); });
		}

		[Test]
		public void TestCapabilityDiposed()
		{
			Client.Dispose();
			Assert.Throws(typeof(ObjectDisposedException), delegate { Client.Capabilities(); });
		}
	}
}