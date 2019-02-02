using System;
using NUnit.Framework;
using OpenPop.Mime.Decode;

namespace OpenPopUnitTests.Mime.Decode
{
	[TestFixture]
	public class Rfc2822DateTimeTests
	{
		[Test]
		public void RfcExample()
		{
			const string inputDate = "Fri, 21 Nov 1997 09:55:06 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 15, 55, 06, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void RfcExample2()
		{
			const string inputDate = "Tue, 1 Jul 2003 10:52:37 +0200";

			// +0200 we need to substract 2 hours when in UTC
			DateTime expectedOutput = new DateTime(2003, 7, 1, 8, 52, 37, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void NestedComments()
		{
			const string inputDate = "Fri, 21 Nov (foo (bar (baz) ) ) 1997 09:55:06 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 15, 55, 06, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void ObsoleteFormat()
		{
			const string inputDate = "21 Nov 97 09:55:06 GMT";

			// GMT = UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 09, 55, 06, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void ObsoleteFormatWithWhitespaceAndComment()
		{
			const string inputDate = "Fri, 21 Nov 1997 09(comment):   55  :  06 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 15, 55, 06, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void DateWithNoSeconds()
		{
			const string inputDate = "20 Apr 1988 18:10 +0133";

			// +0133 we need to substract 1 hour and 33 minutes when in UTC
			DateTime expectedOutput = new DateTime(1988, 4, 20, 16, 37, 00, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void ObsoleteFormatWithLotsOfWhitespaceAndComments()
		{
			const string inputDate = "(comment)  (comment)  20(comment)  \t Apr(comment) 1988(comment) \t18(comment) :(comment)  \t 10(comment)  \t  +0133 (comment)  \t";

			// +0133 we need to substract 1 hour and 33 minutes when in UTC
			DateTime expectedOutput = new DateTime(1988, 4, 20, 16, 37, 00, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void OnlineExample()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 -0500 (CDT)";

			DateTime expectedOutput = new DateTime(2007, 5, 9, 17, 39, 13, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void MilitaryTimeA()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 A";

			// A is equivalent to +0100
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 +0100";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void MilitaryTimeC()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 C";

			// C is equivalent to +0300
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 +0300";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void MilitaryTimeJ()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 J";

			// J is not a military time
			// Therefore it will be interpreted as -0000
			DateTime expectedOutput = new DateTime(2007, 5, 9, 12, 39, 13, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void MilitaryTimeL()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 L";

			// L is equivalent to +1100
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 +1100";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void MilitaryTimeO()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 O";

			// O is equivalent to -0200
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 -0200";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void MilitaryTimeY()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 Y";

			// Y is equivalent to -1200
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 -1200";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void MilitaryTimeZ()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 Z";

			// Z is equivalent to +0000
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 +0000";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void UsTimeZoneEdt()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 EDT";

			// EDT is equivalent to -0400
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 -0400";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void UsTimeZoneGmt()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 GMT";

			// GMT is equivalent to +0000
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 +0000";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void UsTimeZoneUt()
		{
			const string inputDate = "Wed, 9 May 2007 12:39:13 UT";

			// UT is equivalent to +0000
			const string inputDateEquivalent = "Wed, 9 May 2007 12:39:13 +0000";

			DateTime inputDateTime = Rfc2822DateTime.StringToDate(inputDate);
			DateTime inputDateTimeEquivalent = Rfc2822DateTime.StringToDate(inputDateEquivalent);

			Assert.AreEqual(inputDateTime, inputDateTimeEquivalent);
		}

		[Test]
		public void DateWithNoSpace()
		{
			// This is actually an illigal string
			// But such a string was met, and therefore a robust parser should
			// be able to parse it
			const string inputDate = "19 Jan 2011 13:24:54+0000";

			DateTime expectedOutput = new DateTime(2011, 1, 19, 13, 24, 54, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestDateWithOnlyOneDigitForHour()
		{
			// This is actually an illigal string
			// But such a string was met, and therefore a robust parser should be able to parse it
			// The RFC states that 2 digits must be used for hours
			const string inputDate = "Wed, 16 Feb 2011 1:11:19 +0000";

			DateTime expectedOutput = new DateTime(2011, 2, 16, 1, 11, 19, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestDateWithOnlyOneDigitForMinute()
		{
			// This is actually an illigal string
			// But such a string was met, and therefore a robust parser should be able to parse it
			// The RFC states that 2 digits must be used for minutes
			const string inputDate = "Wed, 16 Mar 2011 00:3:41 +0000";

			DateTime expectedOutput = new DateTime(2011, 3, 16, 0, 3, 41, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestDateWithOnlyOneDigitForSecond()
		{
			// This is actually an illigal string
			// But such a string was met, and therefore a robust parser should be able to parse it
			// The RFC states that 2 digits must be used for seconds
			const string inputDate = "Wed, 16 Mar 2011 01:03:1 +0000";

			DateTime expectedOutput = new DateTime(2011, 3, 16, 1, 3, 1, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestCanHandleInvalidTimezone()
		{
			// This is actually an illigal date
			const string inputDate = "Tue, 08 Mar 2011 07:24:27 0";

			// Expect -0000 used as timezone instead
			DateTime expectedOutput = new DateTime(2011, 3, 8, 7, 24, 27, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestInvalidWeekdayIgnored()
		{
			// This is actually an illigal date - 08 mar is a tuesday!
			const string inputDate = "Sun, 08 Mar 2011 16:16:13 -0000";

			DateTime expectedOutput = new DateTime(2011, 3, 8, 16, 16, 13, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestUnparsableReturnsMinDate()
		{
			const string inputDate = "foo";

			DateTime expectedOutput = DateTime.MinValue;
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestInvalidDateThrowsArgumentException()
		{
			Assert.Throws<ArgumentException>(() => Rfc2822DateTime.StringToDate("Sun, 03 Mar 2011 00:77:00 -0000"));
			Assert.Throws<ArgumentException>(() => Rfc2822DateTime.StringToDate("Sun, 03 Mar 2011 77:00:00 -0000"));
			Assert.Throws<ArgumentException>(() => Rfc2822DateTime.StringToDate("Sun, 43 Mar 2011 00:00:00 -0000"));
			Assert.Throws<ArgumentException>(() => Rfc2822DateTime.StringToDate("Sun, 43 Mar 2011 77:77:77 -9999"));
		}

		[Test]
		public void TestWrongFormatParses()
		{
			// This is a valid date, but in a incorrect format
			const string inputDate = "1997-11-21 09:55:06 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 15, 55, 06, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestWrongFormatParsesNoTime()
		{
			// This is a valid date, but in a incorrect format
			const string inputDate = "1997-11-21 09:55 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 15, 55, 00, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestWrongFormatParsesDateWithOneDigit()
		{
			// This is a valid date, but in a incorrect format
			const string inputDate = "1997-1-2 09:55 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 1, 2, 15, 55, 00, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestWrongFormatParsesTimeWithOneDigit()
		{
			// This is a valid date, but in a incorrect format
			const string inputDate = "1997-11-21 9:5:6 -0600";

			// -0600 we need to add 6 hours when in UTC
			DateTime expectedOutput = new DateTime(1997, 11, 21, 15, 5, 6, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void IllegalButStandardFormatWithDashes()
		{
			const string inputDate = "08-May-2012 16:52:30 +0100";

			DateTime expectedOutput = new DateTime(2012, 5, 8, 15, 52, 30, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void IllegalButStandardFormatWithDashes2()
		{
			const string inputDate = "03-jAN-2011 13:13:13 -0200";

			DateTime expectedOutput = new DateTime(2011, 1, 3, 15, 13, 13, DateTimeKind.Utc);
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}

		[Test]
		public void TestUnparseableDateWillNotThrowWhenAdjustingTimezone()
		{
			const string inputDate = "Fri, 19 Oct2012 13:59:12 +0200";

			DateTime expectedOutput = DateTime.MinValue;
			DateTime output = Rfc2822DateTime.StringToDate(inputDate);

			Assert.AreEqual(expectedOutput, output);
		}
	}
}
