using System.IO;
using System.Text;
using NUnit.Framework;
using OpenPop.Common;

namespace OpenPopUnitTests.Shared
{
	[TestFixture]
	class StreamUtilityTests
	{
		[Test]
		public void ReadLineCanReadBytesUntilCRLF()
		{
			const string input = "test\r\n";
			Stream stream = new MemoryStream(Encoding.ASCII.GetBytes(input));

			const string expectedOutputString = "test";
			byte[] expectedOutputBytes = Encoding.ASCII.GetBytes(expectedOutputString);

			byte[] outputBytes = StreamUtility.ReadLineAsBytes(stream);
			Assert.AreEqual(expectedOutputBytes, outputBytes);

			// Try again, now reading using ASCII
			stream = new MemoryStream(Encoding.ASCII.GetBytes(input));

			string outputString = StreamUtility.ReadLineAsAscii(stream);
			Assert.AreEqual(expectedOutputString, outputString);
		}

		[Test]
		public void ReadLineCanReadBytesUntilEnd()
		{
			const string input = "test";
			Stream stream = new MemoryStream(Encoding.ASCII.GetBytes(input));

			const string expectedOutputString = "test";
			byte[] expectedOutputBytes = Encoding.ASCII.GetBytes(expectedOutputString);

			byte[] outputBytes = StreamUtility.ReadLineAsBytes(stream);
			Assert.AreEqual(expectedOutputBytes, outputBytes);

			// Try again, now reading using ASCII
			stream = new MemoryStream(Encoding.ASCII.GetBytes(input));

			string outputString = StreamUtility.ReadLineAsAscii(stream);
			Assert.AreEqual(expectedOutputString, outputString);
		}

		[Test]
		public void ReadLineCanReadBytesUntilLF()
		{
			const string input = "test\n";
			Stream stream = new MemoryStream(Encoding.ASCII.GetBytes(input));

			const string expectedOutputString = "test";
			byte[] expectedOutputBytes = Encoding.ASCII.GetBytes(expectedOutputString);

			byte[] outputBytes = StreamUtility.ReadLineAsBytes(stream);
			Assert.AreEqual(expectedOutputBytes, outputBytes);

			// Try again, now reading using ASCII
			stream = new MemoryStream(Encoding.ASCII.GetBytes(input));

			string outputString = StreamUtility.ReadLineAsAscii(stream);
			Assert.AreEqual(expectedOutputString, outputString);
		}
	}
}