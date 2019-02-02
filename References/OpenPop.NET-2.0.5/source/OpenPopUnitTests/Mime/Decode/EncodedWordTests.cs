using NUnit.Framework;
using OpenPop.Mime.Decode;

namespace OpenPopUnitTests.Mime.Decode
{
	[TestFixture]
	public class EncodedWordTests
	{
		[Test]
		public void TestEncodedWordOnSubjectWithSpaces()
		{
			const string subject = 
				"=?iso-8859-1?Q?Re: Yahoo! Mail (comment acc=E9der, restaurer, etc)?=";

			string result = EncodedWord.Decode(subject);

			Assert.AreEqual("Re: Yahoo! Mail (comment accéder, restaurer, etc)", result);
		}

		[Test]
		public void TestEncodedWordOnNonEncodedString()
		{
			const string notEncodedString =
				"test that this is not touched ?! ?=..?= =?...?=";

			string result = EncodedWord.Decode(notEncodedString);

			Assert.AreEqual(notEncodedString, result);
		}

		[Test]
		public void TestEncodedWordDanish()
		{
			const string subject = "=?iso-8859-1?Q?SV:_Ticket(13349550)_-_Sp=F8rgsm=E5l_omkring_CBB_privat?=";

			string result = EncodedWord.Decode(subject);

			Assert.AreEqual("SV: Ticket(13349550) - Spørgsmål omkring CBB privat", result);
		}

		[Test]
		public void TestEncodedWordLithuanian()
		{
			const string input = "=?ISO-8859-13?Q?Fwd=3A_Dvira=E8iai_vasar=E0_vagiami_da=FEniau=2C_bet_draust?=";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("Fwd: Dviračiai vasarą vagiami dažniau, bet draust", result);
		}

		/// <summary>
		/// See http://tools.ietf.org/html/rfc2047#page-12
		/// </summary>
		[Test]
		public void TestWhiteSpaceBetweenEncodingsAreIgnored()
		{
			const string input = "(=?ISO-8859-1?Q?a?= =?ISO-8859-1?Q?b?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(ab)", result);
		}

		[Test]
		public void TestWhiteSpaceBetweenEncodingsAreIgnored2()
		{
			const string input = "(=?ISO-8859-1?Q?a?=  =?ISO-8859-1?Q?b?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(ab)", result);
		}

		[Test]
		public void TestWhiteSpaceBetweenEncodingsAreIgnored3()
		{
			const string input = "(=?ISO-8859-1?Q?a?=        \n  \t    =?ISO-8859-1?Q?b?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(ab)", result);
		}

		[Test]
		public void TestWhiteSpaceBetweenEncodingsAreIgnored4()
		{
			const string input = "(=?ISO-8859-1?Q?a?= =?ISO-8859-2?Q?_b?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(a b)", result);
		}

		[Test]
		public void TestOnlyWhiteSpaceBetweenEncodingsAreIgnored()
		{
			const string input = "(=?ISO-8859-1?Q?a?= =?ISO-8859-1?Q?b?=) (=?ISO-8859-1?Q?a?= =?ISO-8859-1?Q?b?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(ab) (ab)", result);
		}

		[Test]
		public void TestWhiteSpaceBetweenEncodingsAreIgnoredWithMultipleParts()
		{
			const string input = "(=?UTF-8?Q?a?= =?UTF-8?Q?b?= =?UTF-8?Q?c?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(abc)", result);
		}

		[Test]
		public void TestWhiteSpaceBetweenEncodingsAreIgnoredWithMultipleParts2()
		{
			const string input = "(=?UTF-8?Q?a?= =?UTF-8?Q?b?= =?UTF-8?Q?c?= =?UTF-8?Q?a?= =?UTF-8?Q?b?= =?UTF-8?Q?c?=)";

			string result = EncodedWord.Decode(input);
			Assert.AreEqual("(abcabc)", result);
		}
	}
}