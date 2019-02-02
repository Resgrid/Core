using System.Collections.Generic;
using NUnit.Framework;
using OpenPop.Mime.Header;

namespace OpenPopUnitTests.Mime.Header
{
	[TestFixture]
	public class HeaderExtractorTests
	{
		/// <summary>
		/// Tests the ability of the method to parse out the message header name when the content has been split to another line
		/// </summary>
		[Test]
		public void TestGetHeadersValueSplitLine()
		{
			const string expectedName = "MessageID";
			KeyValuePair<string, string> header = HeaderExtractor.SeparateHeaderNameAndValue( string.Format("{0}:",expectedName) );
			Assert.AreEqual( expectedName, header.Key, "Header Name" );
			Assert.AreEqual( string.Empty, header.Value, "Header Value" );
		}

		/// <summary>
		/// Tests the ability of the method to parse out the header and content from a single line
		/// </summary>
		[Test]
		public void TestGetHeadersValueSingleLine()
		{
			const string expectedName = "MessageID";
			const string expectedValue = "<1234567890>";
			KeyValuePair<string, string> header = HeaderExtractor.SeparateHeaderNameAndValue(string.Format("{0}: {1}", expectedName, expectedValue));
			Assert.AreEqual( expectedName, header.Key, "Header Name" );
			Assert.AreEqual( expectedValue, header.Value, "Header Value" );
		}
	}
}