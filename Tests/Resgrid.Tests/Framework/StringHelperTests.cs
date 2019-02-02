using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Vereyon.Web;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class StringHelperTests
	{
		[Test]
		public void TestHtmlSanatizer_OnCallNameWithWordHtml()
		{
			var input = "";
			using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "\\Data\\Strings\\BadWordHtmlCallName.txt"))
			{
				input = reader.ReadToEnd();
			}

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().NotBeNullOrEmpty();
			//result.Should().Be("Email Call High MEDICAL EMERGENCY");
		}

		[Test]
		public void TestHtmlSanatizer_OnCallNameWithWordHtml2()
		{
			var input = "";
			using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "\\Data\\Strings\\BadWordHtmlCallName2.txt"))
			{
				input = reader.ReadToEnd();
			}

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().NotBeNullOrEmpty();
			//result.Should().Be("Email Call High Fire");
		}

		[Test]
		public void TestHtmlSanatizer_OnCallNotesWithWordHtml()
		{
			var input = "";
			using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "\\Data\\Strings\\BadWordHtmlCallNotes.txt"))
			{
				input = reader.ReadToEnd();
			}

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().NotBeNullOrEmpty();
			//result.Should().Be("burning complaint lougheed");
		}

		[Test]
		public void TestHtmlSanatizer_OnCallNotesWithWordHtmlFromEmailFW()
		{
			var input = "";
			using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "\\Data\\Strings\\BadWordHtmlCallNotesFW.txt"))
			{
				input = reader.ReadToEnd();
			}

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().NotBeNullOrEmpty();
			//result.Should().Be("burning complaint lougheed");
		}

		[Test]
		public void TestHtmlSanatizer_OnCallNotesWithWordHtmlFromEmailFW_NoTitle()
		{
			var input = "";
			using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "\\Data\\Strings\\BadWordHtmlCallNotesFWNoTitle.txt"))
			{
				input = reader.ReadToEnd();
			}

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().NotBeNullOrEmpty();
			//result.Should().Be("burning complaint lougheed");
		}
	}
}