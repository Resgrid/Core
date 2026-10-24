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
		public void SanitizeHtmlInString_ShouldDropStyleBlocksWithTheirCss()
		{
			var result = StringHelpers.SanitizeHtmlInString("<html><head><style type=\"text/css\">p { color: red; }</style></head><body><p>Fire at Main</p></body></html>");

			result.Should().Be("<p>Fire at Main</p>");
		}

		[TestCase("<p onclick=\"alert(1)\">a<script>alert(2)</script>b</p><img src=\"x\" onerror=\"alert(3)\"><iframe src=\"https://evil.example\">i</iframe>")]
		[TestCase("<svg onload=\"alert(1)\"><circle r=\"1\"/></svg><p>a</p><p>b</p>")]
		[TestCase("<a href=\"javascript:alert(1)\">a</a><b>b</b>")]
		[TestCase("<p style=\"background:url(javascript:alert(1))\">a</p><object data=\"x\"></object><embed src=\"x\"><p>b</p>")]
		[TestCase("<math><mtext><style><img src=x onerror=alert(1)></style></mtext></math><p>a</p><p>b</p>")]
		[TestCase("<form action=\"https://evil.example\"><input name=\"x\"><button>a</button></form><p>b</p>")]
		public void SanitizeHtmlForDisplay_ShouldRemoveScriptHandlersAndActiveContent(string input)
		{
			var result = StringHelpers.SanitizeHtmlForDisplay(input);

			result.Should().NotContainAny("script", "alert", "onerror", "onload", "onclick", "iframe", "javascript:", "<svg", "<object", "<embed", "<form", "<input", "style=");
			result.Should().Contain("b");
		}

		[Test]
		public void SanitizeHtmlForDisplay_ShouldKeepFormattingAndReturnEmptyInsteadOfAPlaceholder()
		{
			StringHelpers.SanitizeHtmlForDisplay("<div dir=\"ltr\">Just a test!<div><br></div><div>Shawn</div></div>")
				.Should().Contain("Just a test!").And.Contain("Shawn").And.NotContain("dir=");
			StringHelpers.SanitizeHtmlForDisplay("<p>The <u>NORA</u> <strong>program</strong></p>").Should().Be("<p>The <u>NORA</u> <strong>program</strong></p>");
			StringHelpers.SanitizeHtmlForDisplay("<script>alert(1)</script>").Should().BeEmpty();
			StringHelpers.SanitizeHtmlForDisplay(null).Should().BeEmpty();
			StringHelpers.SanitizeHtmlForDisplay("   ").Should().BeEmpty();
		}

		[TestCase("Structure fire, 2nd floor & attic")]
		[TestCase("Pressure 90 > 60")]
		[TestCase("Plain text with no markup")]
		public void SanitizeHtmlForDisplay_ShouldReturnTextWithoutMarkupUnchanged(string input)
		{
			StringHelpers.SanitizeHtmlForDisplay(input).Should().Be(input);
		}

		[TestCase("pt <unconscious> bp 80", "pt &lt;unconscious&gt; bp 80")]
		[TestCase("SpO2 <90, x<y", "SpO2 &lt;90, x&lt;y")]
		[TestCase("<img src=x onerror=alert(1)", "&lt;img src=x onerror=alert(1)")]
		public void SanitizeHtmlForDisplay_ShouldShowPlainTextAngleBracketsLiterally(string input, string expected)
		{
			// The allow-list sanitizer deletes unknown elements with their text; a radioed or texted nature is not HTML.
			StringHelpers.SanitizeHtmlForDisplay(input).Should().Be(expected);
		}

		[TestCase("<x-el autofocus onfocus=\"alert(1)\">hi</x-el><p>ok</p>")]
		[TestCase("<unknown tabindex=1 onfocus=alert(1)>hi</unknown> ok")]
		[TestCase("<o:p onclick=\"alert(1)\">hi</o:p> ok")]
		public void SanitizeHtmlForDisplay_ShouldTreatTagsWithAttributesAsMarkup(string input)
		{
			StringHelpers.SanitizeHtmlForDisplay(input).Should().NotContainAny("alert", "onfocus", "onclick").And.Contain("ok");
		}

		[Test]
		public void SanitizeHtml_ShouldKeepNonAsciiTextAsCharacters()
		{
			// Exports, voice prompts and texts strip tags without decoding entities; "&#233;" would be read out or printed.
			var input = "<p>Incendie à l'école — Пожар 🚒 &amp; ñ</p>";

			StringHelpers.SanitizeHtmlForStorage(input).Should().Be("<p>Incendie à l'école — Пожар 🚒 &amp; ñ</p>");
			StringHelpers.SanitizeHtmlForDisplay(input).Should().Be("<p>Incendie à l'école — Пожар 🚒 &amp; ñ</p>");
		}

		[Test]
		public void SanitizeHtmlForStorage_ShouldCleanMarkupAndLeaveTextAndEnvelopesAlone()
		{
			StringHelpers.SanitizeHtmlForStorage("<p>Smoke<img src=x onerror=alert(1)><script>alert(2)</script></p>")
				.Should().StartWith("<p>Smoke").And.NotContainAny("onerror", "alert", "script");
			StringHelpers.SanitizeHtmlForStorage("<script>alert(1)</script>").Should().BeEmpty();
			StringHelpers.SanitizeHtmlForStorage("pt <unconscious> bp 80").Should().Be("pt <unconscious> bp 80");
			StringHelpers.SanitizeHtmlForStorage("Fire at 5th & Main").Should().Be("Fire at 5th & Main");
			StringHelpers.SanitizeHtmlForStorage(Resgrid.Model.ProtectedDataEnvelope.Prefix + "AAAA").Should().Be(Resgrid.Model.ProtectedDataEnvelope.Prefix + "AAAA");
			StringHelpers.SanitizeHtmlForStorage(null).Should().BeNull();

			var quill = "<p>The <u>NORA</u> <strong>program</strong></p>";
			StringHelpers.SanitizeHtmlForStorage(quill).Should().Be(quill);
			StringHelpers.SanitizeHtmlForStorage(StringHelpers.SanitizeHtmlForStorage(quill)).Should().Be(quill);
		}

		[TestCase("<div dir=\"ltr\">Just a test!<div><br></div><div>Shawn</div></div>", "Just a test! Shawn")]
		[TestCase("<div dir=\"ltr\">JUST A TEST!<div><br></div><div>Shawn</div></div>", "JUST A TEST! Shawn")]
		[TestCase("<p>Door <b>two</b> sticks</p>", "Door two sticks")]
		[TestCase("<p>Sh<b>aw</b>n</p><p>Next</p>", "Shawn Next")]
		[TestCase("<html><head><title>Fwd</title><style>p{color:red}</style></head><body><p>Smoke&nbsp;showing</p><script>alert(1)</script></body></html>", "Smoke showing")]
		[TestCase("<!-- Word comment --><p>A &amp; B &lt;c&gt;</p>", "A & B <c>")]
		[TestCase("Pressure < 90 and x<y", "Pressure < 90 and x<y")]
		[TestCase("  plain\r\n\r\ntext  ", "plain text")]
		[TestCase(null, "")]
		public void HtmlToPlainText_ShouldReturnReadableText(string input, string expected)
		{
			StringHelpers.HtmlToPlainText(input).Should().Be(expected);
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
