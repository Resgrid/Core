using System;
using System.Text;
using NUnit.Framework;
using OpenPop.Mime.Decode;

namespace OpenPopUnitTests.Mime.Decode
{
	[TestFixture]
	public class EncodingFinderTests
	{
		/// <summary>
		/// This test class is special.
		/// Notice that EncodingFinder is a static class, but it
		/// has state in the form of the Dictionary mapping
		/// from names to encodings.
		/// 
		/// Therefore - be warned - the test cases are not isolated from eachother, and
		/// therefore the Reset() method must be called if some internal state is changed
		/// in the EncodingFinder class!
		/// This method does this before and after calling any testcases in this class.
		/// </summary>
		[SetUp]
		[TearDown]
		public void TearDown()
		{
			//
			// Remember to reset the configuration of the EncondingFinder
			EncodingFinder.Reset();
		}

		[Test]
		public void TestUtf8()
		{
			Assert.AreEqual(Encoding.UTF8, EncodingFinder.FindEncoding("utf8"));
		}

		[Test]
		public void TestUtfHyphen8()
		{
			Assert.AreEqual(Encoding.UTF8, EncodingFinder.FindEncoding("utf-8"));
		}
		
		[Test]
		public void TestUtfHyphen8UpperCase()
		{
			Assert.AreEqual(Encoding.UTF8, EncodingFinder.FindEncoding("UTF-8"));
		}

		[Test]
		public void TestCodePage1255()
		{
			Assert.AreEqual(Encoding.GetEncoding(1255), EncodingFinder.FindEncoding("cp-1255"));
		}

		[Test]
		public void TestCodePage950()
		{
			Assert.AreEqual(Encoding.GetEncoding(950), EncodingFinder.FindEncoding("cp-950"));
		}

		[Test]
		public void TestCodePage950Windows()
		{
			Assert.AreEqual(Encoding.GetEncoding(950), EncodingFinder.FindEncoding("windows-950"));
		}

		[Test]
		public void TestIso88591()
		{
			Assert.AreEqual(Encoding.GetEncoding("iso-8859-1"), EncodingFinder.FindEncoding("iso-8859-1"));
		}

		[Test]
		public void TestIso88599()
		{
			Assert.AreEqual(Encoding.GetEncoding("iso-8859-9"), EncodingFinder.FindEncoding("iso-8859-9"));
		}

		[Test]
		public void TestMap()
		{
			Encoding obscureEncoding = Encoding.UTF32;
			EncodingFinder.AddMapping("some_obscure_encoding", obscureEncoding);

			Assert.AreEqual(obscureEncoding, EncodingFinder.FindEncoding("some_obscure_encoding"));

			// Should work for uppercase as well
			Assert.AreEqual(obscureEncoding, EncodingFinder.FindEncoding("SOME_OBSCURE_ENCODING"));

			// Should work for mixed case as well
			Assert.AreEqual(obscureEncoding, EncodingFinder.FindEncoding("sOME_ObScUrE_enCOdiNg"));
		}

		[Test]
		public void TestMapOther()
		{
			Encoding ascii = Encoding.ASCII;
			EncodingFinder.AddMapping("ASCII DASCii", ascii);

			Assert.AreEqual(ascii, EncodingFinder.FindEncoding("ASCII DASCii"));

			// Should work for uppercase as well
			Assert.AreEqual(ascii, EncodingFinder.FindEncoding("ascii DASCii"));

			// Should work for mixed case as well
			Assert.AreEqual(ascii, EncodingFinder.FindEncoding("AsciI dAsCii"));
		}

		[Test]
		public void TestNoNullArgumentsToAddMapping()
		{
			Assert.Throws<ArgumentNullException>(delegate { EncodingFinder.AddMapping("foobar", null); });
			Assert.Throws<ArgumentNullException>(delegate { EncodingFinder.AddMapping(null, Encoding.UTF8); });
		}

		[Test]
		public void TestMappingOverridesNormalCode()
		{
			Assert.AreEqual(Encoding.GetEncoding("iso-8859-1"), EncodingFinder.FindEncoding("iso-8859-1"));

			EncodingFinder.AddMapping("iso-8859-1", Encoding.UTF8);
			Assert.AreEqual(Encoding.UTF8, EncodingFinder.FindEncoding("iso-8859-1"));
		}

		[Test]
		public void TestUnknownMappingThrowsException()
		{
			Assert.Throws<ArgumentException>(delegate { EncodingFinder.FindEncoding("unknown_special_encoding_yoyo"); } );
		}

		[Test]
		public void TestFallbackDecoder()
		{
			bool wasCalled = false;
			EncodingFinder.FallbackDecoder = delegate(string characterSet)
			{
				Assert.AreEqual("unknown_special_encoding_yoyo", characterSet);
				wasCalled = true;
				return Encoding.UTF8; 
			};

			Assert.AreEqual(Encoding.UTF8, EncodingFinder.FindEncoding("unknown_special_encoding_yoyo"));
			Assert.IsTrue(wasCalled);
		}
	}
}
