using System.IO;
using System.Text;
using NUnit.Framework;
using OpenPop.Pop3;
using OpenPop.Pop3.Exceptions;

namespace OpenPopUnitTests.Pop3
{
	/// <summary>
	/// This TestFixture is testing that when the <see cref="Pop3Client"/> is in an connected state, commands which may not
	/// be used in that state, throws <see cref="InvalidUseException"/>.
	/// 
	/// Also tests that commands which is can be used in this state, does not throw any exceptions
	/// </summary>
	[TestFixture]
	class POPClientConnectedTests
	{
		private const int RandomMessageNumber = 5;
		private const string RandomString = "random";

		private Pop3Client Client;

		[SetUp]
		public void Init()
		{
			Client = new Pop3Client();
		}

		/// <summary>
		/// Let the <see cref="Pop3Client"/> connect.
		/// </summary>
		/// <param name="extraReaderInput">Extra input that the server may read off the reader. String is convert to bytes using ASCII encoding</param>
		private void Connect(string extraReaderInput = "")
		{
			string readerInput = "+OK\r\n" + extraReaderInput; // Always allow connect, which is the first ok
			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes(readerInput));
			Stream outputStream = new MemoryStream();

			// Connect with the client
			Client.Connect(new CombinedStream(inputStream, outputStream));
		}

		[Test]
		public void TestAuthenticateDoesNotThrow()
		{
			Connect("+OK\r\n+OK\r\n"); // Allow username and password
			Assert.DoesNotThrow(delegate { Client.Authenticate(RandomString, RandomString); });
		}

		[Test]
		public void TestDeleteAllMessages()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.DeleteAllMessages(); });
		}

		[Test]
		public void TestDeleteMessage()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.DeleteMessage(RandomMessageNumber); });
		}

		[Test]
		public void TestDisconnectDoesNotThrow()
		{
			Connect("+OK"); // OK to quit command
			Assert.DoesNotThrow(delegate { Client.Disconnect(); });
		}

		[Test]
		public void TestGetMessage()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessage(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageCount()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageCount(); });
		}

		[Test]
		public void TestGetMessageHeaders()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageHeaders(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageSize()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageSize(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageSizes()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageSizes(); });
		}

		[Test]
		public void TestGetMessageUID()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageUid(RandomMessageNumber); });
		}

		[Test]
		public void TestGetMessageUIDs()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.GetMessageUids(); });
		}

		[Test]
		public void TestNOOP()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.NoOperation(); });
		}

		[Test]
		public void TestRSET()
		{
			Connect();
			Assert.Throws(typeof(InvalidUseException), delegate { Client.Reset(); });
		}

		[Test]
		public void TestConnect()
		{
			Connect();

			Stream inputStream = new MemoryStream(Encoding.ASCII.GetBytes("+OK")); // Welcome message
			Stream writer = new MemoryStream();

			// Try connect again
			Assert.Throws(typeof(InvalidUseException), delegate { Client.Connect(new CombinedStream(inputStream, writer)); });
		}
	}
}