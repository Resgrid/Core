using System.Text;
using NUnit.Framework;
using OpenPop.Mime.Decode;

namespace OpenPopUnitTests.Mime.Decode
{
	/// <summary>
	/// This is a class that tests the <see cref="QuotedPrintable"/> class
	/// </summary>
	[TestFixture]
	public class QuotedPrintableTest
	{
		/// <summary>
		/// Make a test that an encoded equal sign can be decoded
		/// </summary>
		[Test]
		public void CanDecodeEqualSign()
		{
			const string input = "=3D";
			const string expectedOutput = "=";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);  
		}

		/// <summary>
		/// Make a test that an encoded equal sign can be decoded two times
		/// </summary>
		[Test]
		public void CanDecodeEqualSignTwoTimes()
		{
			const string input = "=3D=3D";
			const string expectedOutput = "==";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// Make sure that the decoder does not first decode, and then decode that once again
		/// </summary>
		[Test]
		public void DoNotDoubleDecode()
		{
			const string input = "=3D3D";
			const string expectedOutput = "=3D"; // Checks that the output itself is not decoded, as this is encoded equal sign

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2047">Section 4.2 bullet 2</see>
		/// "
		/// The 8-bit hexadecimal value 20 (e.g., ISO-8859-1 SPACE) may be represented
		/// as "_" (underscore, ASCII 95.).  (This character may not pass through some
		/// internetwork mail gateways, but its use will greatly enhance readability
		/// of "Q" encoded data with mail readers that do not support this encoding.)
		/// The "_" always represents hexadecimal 20, even if the SPACE
		/// character occupies a different code position in the character set in use
		/// "
		/// </summary>
		[Test]
		public void CanDecodeUnderscoreToSpace()
		{
			// Space is represented as an _

			const string input = "_";
			const string expectedOutput = " ";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// (Soft Line Breaks)
		/// The Quoted-Printable encoding REQUIRES that encoded
		/// lines be no more than 76 characters long.  If longer lines are to be
		/// encoded with the Quoted-Printable encoding, "soft" line breaks must be
		/// used.  An equal sign as the last character on a encoded line indicates
		/// such a non-significant ("soft") line break in the encoded text.
		/// "
		/// </summary>
		[Test]
		public void CanDecodeSoftLineBreak()
		{
			const string input = "=\r\n";
			const string expectedOutput = "";

			// =20 should be a space
			// = just after should be a soft line break

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// White Space)
		/// Octets with values of 9 and 32 MAY be represented as US-ASCII
		/// TAB (HT) and SPACE characters, respectively, but MUST NOT be so
		/// represented at the end of an encoded line.  Any TAB (HT) or SPACE
		/// characters on an encoded line MUST thus be followed on that line by a
		/// printable character. In particular, an "=" at the end of an encoded line,
		/// indicating a soft line break (see rule #5) may follow one or more TAB (HT)
		/// or SPACE characters.  It follows that an octet with decimal value 9 or 32
		/// appearing at the end of an encoded line must be represented according to
		/// Rule #1.  This rule is necessary because some MTAs (Message Transport
		/// Agents, programs which transport messages from one user to another, or
		/// perform a portion of such transfers) are known to pad lines of text with
		/// SPACEs, and others are known to remove "white space" characters from the
		/// end of a line.  Therefore, when decoding a Quoted-Printable body, any
		/// trailing white space on a line must be deleted, as it will necessarily
		/// have been added by intermediate transport agents.
		/// "
		/// </summary>
		[Test]
		public void LiteralSpaceAndTabSupported()
		{
			const string input = "Test for space\tand\ttabs";
			const string expectedOutput = input; // Nothing should happen, spaces and tabs should be kept

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <see cref="http://en.wikipedia.org/wiki/MIME#Multipart_messages"/>
		[Test]
		public void CanDecodeSpanishSentence()
		{
			// Space is represented as an _ in

			const string input = "=A1Hola,_se=F1or!";
			const string expectedOutput = "¡Hola, señor!";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// (Literal representation)
		/// Octets with decimal values of 33 through 60
		/// inclusive, and 62 through 126, inclusive, MAY be represented as the
		/// US-ASCII characters which correspond to those octets (EXCLAMATION POINT
		/// through LESS THAN, and GREATER THAN through TILDE, respectively).
		/// "
		/// Notice that <see cref="http://tools.ietf.org/html/rfc2047"/> section 4.2 bullet overrules the _ as a literal.
		/// </summary>
		[Test]
		public void DoNotTouchLiterals()
		{
			const string input = "!\"#$%&'()*+,-./0123456789:;<>@ABCDEFGHIJKLMNIOQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
			const string expectedOutput = "!\"#$%&'()*+,-./0123456789:;<>@ABCDEFGHIJKLMNIOQRSTUVWXYZ[\\]^ `abcdefghijklmnopqrstuvwxyz{|}~"; // Only change is that _ delimits SPACE

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <see cref="http://en.wikipedia.org/wiki/Quoted-printable#Example"/>
		/// This also tests
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// Encoded lines must not be longer than 76 characters,
		/// not counting the trailing CRLF. If longer lines are
		/// found in incoming, encoded data, a robust
		/// implementation might nevertheless decode the lines, and
		/// might report the erroneous encoding to the user.
		/// "
		[Test]
		public void CanDecodeLongSentence()
		{
			// Includes a space (=20 is SPACE) and and a soft line break (=\r\n is soft line break)
			const string input = "If you believe that truth=3Dbeauty, then surely=20=\r\nmathematics is the most beautiful branch of philosophy.";
			const string expectedOutput = "If you believe that truth=beauty, then surely mathematics is the most beautiful branch of philosophy.";
			
			// No exceptions
			Assert.DoesNotThrow(delegate { QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1")); });

			// And output is to be decoded anyway
			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7">below last bullet</see>
		/// "
		/// Thus if the "raw" form of the line is a single unencoded line that says:
		///
		/// Now is the time for all folk to come to the aid of their country.
		///
		/// This can be represented, in the Quoted-Printable encoding, as:
		/// Now is the time =
		/// for all folk to come=
		///  to the aid of their country.
		/// "
		/// </summary>
		[Test]
		public void RFCExample()
		{
			const string input = "Now is the time =\r\nfor all folk to come=\r\n to the aid of their country.";
			const string expectedOutput = "Now is the time for all folk to come to the aid of their country.";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// Control characters other than TAB, or CR and LF as
        /// parts of CRLF pairs, must not appear. The same is true
        /// for octets with decimal values greater than 126.  If
        /// found in incoming quoted-printable data by a decoder, a
        /// robust implementation might exclude them from the
        /// decoded data and warn the user that illegal characters
        /// were discovered.
        /// "
		/// </summary>
		[Test]
		public void ImplementationShouldBeRobustControlCharacters()
		{
			const char bellAlert = '\a';
			const char backSpace = '\b';
			const char formFeed = '\f';
			const char nullChar = '\0';
			const char carrigeReturn = '\r'; // Allowed if used with \r\n
			const char newline = '\n'; // Allowed if used with \r\n
			const char horizontalTab = '\t'; // Allowed
			const char deleteChar = '\u007F';
			string input = "" + bellAlert + backSpace + formFeed + nullChar + carrigeReturn + newline + horizontalTab + deleteChar;
			const string expectedOutput = "\r\n\t"; // All other illegal control characters should have been deleted

			// Do not throw exceptions
			Assert.DoesNotThrow(delegate { QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1")); });

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			// And output should be correct
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// Control characters other than TAB, or CR and LF as
		/// parts of CRLF pairs, must not appear. The same is true
		/// for octets with decimal values greater than 126.  If
		/// found in incoming quoted-printable data by a decoder, a
		/// robust implementation might exclude them from the
		/// decoded data and warn the user that illegal characters
		/// were discovered.
		/// "
		/// </summary>
		[Test]
		public void ImplementationShouldBeRobustControlCharactersCarriageReturnNewlineNotPair()
		{
			const string input = "\n\runit\ned\r"; // Notice the ordering is wrong with the first pair, and therefore not allowed
			const string expectedOutput = "united"; // All illegal control characters should have been deleted

			// Do not throw exceptions
			Assert.DoesNotThrow(delegate { QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1")); });

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			// And output should be correct
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// An "=" cannot be the ultimate or penultimate character in an encoded
		/// object.  This could be handled as in case (2) above.
		/// "
		/// 
		/// Case (2) is:
		/// "
		/// An "=" followed by a character that is neither a
		/// hexadecimal digit (including "abcdef") nor the CR character of a CRLF pair
		/// is illegal.  This case can be the result of US-ASCII text having been
		/// included in a quoted-printable part of a message without itself having
		/// been subjected to quoted-printable encoding.  A reasonable approach by a
		/// robust implementation might be to include the "=" character and the
		/// following character in the decoded data without any transformation and, if
		/// possible, indicate to the user that proper decoding was not possible at
		/// this point in the data.
		/// "
		/// </summary>
		[Test]
		public void ImplementationShouldBeRobustNothingAfterEqual()
		{
			const string input = "="; // This is clearly illigal input, as the RFC says there MUST be something after
			// the equal sign. It also states that the parser should be robust. Therefore no exceptions must be thrown

			Assert.DoesNotThrow(delegate { QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1")); }); // Maybe exception thrown?

			// The RFC says that the input should be though of as not encoded at all
			const string expectedOutput = "=";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			// And output should be correct
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// An "=" followed by two hexadecimal digits, one or both of which are
		/// lowercase letters in "abcdef", is formally illegal. A robust
		/// implementation might choose to recognize them as the corresponding
		/// uppercase letters.
		/// "
		/// </summary>
		[Test]
		public void ImplementationShouldBeRobustSmallHexDigitsAfterEqual()
		{
			const string input = "=3d=a1";
			const string expectedOutput = "=¡"; // Should simply be decoded as if hex characters were uppercase

			// Therefore no exceptions must be thrown
			Assert.DoesNotThrow(delegate { QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1")); });

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			// And output should be correct
			Assert.AreEqual(expectedOutput, output);
		}

		/// <summary>
		/// <see cref="http://tools.ietf.org/html/rfc2045#section-6.7"/>
		/// "
		/// An "=" followed by a character that is neither a hexadecimal digit
		/// (including "abcdef") nor the CR character of a CRLF pair is illegal.  This
		/// case can be the result of US-ASCII text having been included in a
		/// quoted-printable part of a message without itself having been subjected to
		/// quoted-printable encoding.  A reasonable approach by a robust
		/// implementation might be to include the "=" character and the following
		/// character in the decoded data without any transformation and, if possible,
		/// indicate to the user that proper decoding was not possible at this point
		/// in the data.
		/// "
		/// </summary>
		[Test]
		public void ImplementationShouldBeRobustNoHexAfterEqual()
		{
			const string input = "=PK"; // This is clearly illigal input, as the RFC says there MUST be a HEX string after the
			// equal sign.
			
			// It also states that the parser should be robust, and that in such case the input is not to be touched.
			const string expectedOutput = input;

			// Therefore no exceptions must be thrown
			Assert.DoesNotThrow(delegate { QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1")); });

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding("iso-8859-1"));

			// And output should be equal input
			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void CanHandleWindows1252Encoding()
		{
			const string input = "=C5=F7=96";

			// http://en.wikipedia.org/wiki/Windows-1254
			const string expectedOutput = "Å÷–";

			string output = QuotedPrintable.DecodeEncodedWord(input, Encoding.GetEncoding(1252));
			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestUnderscoresNotConvertedToSpacesInContentTransferEncoding()
		{
			const string input = "a_b_c_d_e_f";

			const string expectedOutput = "a_b_c_d_e_f";

			string output = Encoding.ASCII.GetString(QuotedPrintable.DecodeContentTransferEncoding(input));
			Assert.AreEqual(expectedOutput, output);
		}
	}
}