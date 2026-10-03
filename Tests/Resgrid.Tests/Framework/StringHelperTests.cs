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
			using (var reader = new StreamReader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Strings", "BadWordHtmlCallName.txt")))
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
			using (var reader = new StreamReader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Strings", "BadWordHtmlCallName2.txt")))
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
			using (var reader = new StreamReader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Strings", "BadWordHtmlCallNotes.txt")))
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
			using (var reader = new StreamReader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Strings", "BadWordHtmlCallNotesFW.txt")))
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
			using (var reader = new StreamReader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Strings", "BadWordHtmlCallNotesFWNoTitle.txt")))
			{
				input = reader.ReadToEnd();
			}

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().NotBeNullOrEmpty();
			//result.Should().Be("burning complaint lougheed");
		}

		// #181 - training articles lost URLs and short words like "NORA" on save.

		[Test]
		public void SanitizeHtmlInString_ShouldKeepUnderlinedText()
		{
			var result = StringHelpers.SanitizeHtmlInString("<p>The <u>NORA</u> program</p>");

			result.Should().Be("<p>The <u>NORA</u> program</p>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldKeepPastedLinkText()
		{
			var result = StringHelpers.SanitizeHtmlInString("<p>See <a href=\"https://www.nfpa.org/codes\" target=\"_blank\"><u>https://www.nfpa.org/codes</u></a> now</p>");

			result.Should().Be("<p>See <a href=\"https://www.nfpa.org/codes\" rel=\"nofollow\"><u>https://www.nfpa.org/codes</u></a> now</p>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldKeepEditorFormattingTags()
		{
			var input = "<blockquote>Quote</blockquote><pre class=\"ql-syntax\" spellcheck=\"false\">code line</pre><p>H<sub>2</sub>O x<sup>2</sup> <s>old</s> <code>cmd</code></p><h6>Small heading</h6>";

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().Be("<blockquote>Quote</blockquote><pre>code line</pre><p>H<sub>2</sub>O x<sup>2</sup> <s>old</s> <code>cmd</code></p><h6>Small heading</h6>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldUnwrapPresentationalTagsKeepingText()
		{
			var result = StringHelpers.SanitizeHtmlInString("<p>Meet in <font color=\"red\">Chicago</font> at <st1:place w:st=\"on\">Station 1</st1:place><o:p></o:p></p>");

			result.Should().Be("<p>Meet in Chicago at Station 1</p>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldKeepEscapedAngleBracketTextAsText()
		{
			var result = StringHelpers.SanitizeHtmlInString("<p>Contact NORA &lt;nora@example.org&gt; today</p>");

			result.Should().Be("<p>Contact NORA &lt;nora@example.org&gt; today</p>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldNotStripLettersFromOrdinaryText()
		{
			// The Word clean-up patterns had lost their backslashes and matched literal "nr", "s" and "w" sequences
			var result = StringHelpers.SanitizeHtmlInString("<p>Unrnrated HENRNRY swims with class=wonderful style='solid'</p>");
			var plain = StringHelpers.SanitizeHtmlInString("Structure fire at HENRNRY Rd\r\n\r\n\r\nUnrnrated");

			result.Should().Contain("Unrnrated HENRNRY swims with class=wonderful");
			result.Should().Contain("solid");
			plain.Should().Be("Structure fire at HENRNRY Rd\r\n\r\n\r\nUnrnrated");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldStillRemoveScriptsAndHandlers()
		{
			var result = StringHelpers.SanitizeHtmlInString("<p onclick=\"alert(1)\">a<script>alert(2)</script>b<img src=\"x\" onerror=\"alert(3)\"><iframe src=\"https://evil.example\">i</iframe></p>");

			result.Should().NotContain("script");
			result.Should().NotContain("alert");
			result.Should().NotContain("onclick");
			result.Should().NotContain("onerror");
			result.Should().NotContain("iframe");
			result.Should().Contain("a").And.Contain("b");
		}

		[TestCase("javascript:alert(1)")]
		[TestCase("JaVaScRiPt:alert(1)")]
		[TestCase("javascript :alert(1)")]
		[TestCase("java script:alert(1)")]
		[TestCase("vbscript:msgbox(1)")]
		[TestCase("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
		public void SanitizeHtmlInString_ShouldStillRejectScriptUrls(string href)
		{
			var result = StringHelpers.SanitizeHtmlInString("<p>Click <a href=\"" + href + "\">here</a> or <img src=\"" + href + "\"></p>");

			result.Should().Be("<p>Click here or </p>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldKeepLinkWithSpaceAcrossRepeatedSaves()
		{
			var first = StringHelpers.SanitizeHtmlInString("<p><a href=\"https://example.com/docs/NORA program.pdf\" target=\"_blank\">NORA program</a></p>");
			var second = StringHelpers.SanitizeHtmlInString(first);
			var third = StringHelpers.SanitizeHtmlInString(second);

			first.Should().Be("<p><a href=\"https://example.com/docs/NORA%20program.pdf\" rel=\"nofollow\">NORA program</a></p>");
			second.Should().Be(first);
			third.Should().Be(first);
		}

		[Test]
		public void SanitizeHtmlInString_ShouldKeepPercentEncodedSpaceInLink()
		{
			var input = "<p><a href=\"https://example.com/a%20b.pdf\">doc</a> <img src=\"https://example.com/a%20b.png\"></p>";

			var result = StringHelpers.SanitizeHtmlInString(input);

			result.Should().Be("<p><a href=\"https://example.com/a%20b.pdf\" rel=\"nofollow\">doc</a> <img src=\"https://example.com/a%20b.png\"></p>");
			StringHelpers.SanitizeHtmlInString(result).Should().Be(result);
		}

		[TestCase("mailto:nora@example.org")]
		[TestCase("tel:+15555550100")]
		[TestCase("http://example.com/")]
		public void SanitizeHtmlInString_ShouldKeepAllowedLinkSchemes(string href)
		{
			var result = StringHelpers.SanitizeHtmlInString("<p><a href=\"" + href + "\">link</a></p>");

			result.Should().Be("<p><a href=\"" + href + "\" rel=\"nofollow\">link</a></p>");
		}

		[Test]
		public void SanitizeHtmlInString_ShouldBeStableForQuillContent()
		{
			var input = "<h2>Overview</h2><p class=\"ql-align-center\">The <strong>NORA</strong> <em>program</em> <u>starts</u> <span style=\"color: rgb(230, 0, 0);\">today</span>.</p><ol><li>One</li><li class=\"ql-indent-1\">Two &amp; three</li></ol><p><br></p><p>See <a href=\"https://www.nfpa.org/codes and standards\" target=\"_blank\">NFPA</a>.</p>";

			var once = StringHelpers.SanitizeHtmlInString(input);

			once.Should().Contain("<u>starts</u>").And.Contain("NFPA").And.Contain("Two &amp; three");
			once.Should().Contain("href=\"https://www.nfpa.org/codes%20and%20standards\"");
			StringHelpers.SanitizeHtmlInString(once).Should().Be(once);
		}
	}
}
