using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	/// <summary>
	/// The baseline embed allow-list for training text (T0 item 6): YouTube, Vimeo, Google Docs/Sheets, Microsoft Stream/Teams
	/// frames are kept, rebuilt from the normalized address with fixed attributes; every other frame is removed.
	/// </summary>
	[TestFixture]
	public class EmbedAllowListTests
	{
		[TestCase("https://www.youtube.com/embed/dQw4w9WgXcQ?showinfo=0", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
		[TestCase("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
		[TestCase("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
		[TestCase("//www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
		[TestCase("https://www.youtube.com/shorts/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
		[TestCase("https://player.vimeo.com/video/76979871?h=abc", "https://player.vimeo.com/video/76979871")]
		[TestCase("https://vimeo.com/76979871", "https://player.vimeo.com/video/76979871")]
		[TestCase("https://docs.google.com/document/d/abc123/preview", "https://docs.google.com/document/d/abc123/preview")]
		[TestCase("http://docs.google.com/spreadsheets/d/abc/pubhtml?widget=true", "https://docs.google.com/spreadsheets/d/abc/pubhtml?widget=true")]
		[TestCase("https://web.microsoftstream.com/embed/video/1234", "https://web.microsoftstream.com/embed/video/1234")]
		[TestCase("https://contoso.sharepoint.com/sites/x/_layouts/15/embed.aspx?UniqueId=1", "https://contoso.sharepoint.com/sites/x/_layouts/15/embed.aspx?UniqueId=1")]
		[TestCase("https://teams.microsoft.com/l/meetup-join/1", "https://teams.microsoft.com/l/meetup-join/1")]
		public void Allowed_addresses_are_normalized_to_their_https_player(string url, string expected)
		{
			EmbedAllowList.NormalizeEmbedUrl(url).Should().Be(expected);
			EmbedAllowList.IsAllowed(url).Should().BeTrue();
		}

		[TestCase("https://evil.example.com/embed/x")]
		[TestCase("javascript:alert(1)")]
		[TestCase("data:text/html;base64,PHNjcmlwdD4=")]
		[TestCase("https://user:pass@www.youtube.com/embed/dQw4w9WgXcQ")]
		[TestCase("https://www.youtube.com:8443/embed/dQw4w9WgXcQ")]
		[TestCase("https://www.youtube.com/embed/<script>")]
		[TestCase("https://www.youtube.com/feed/trending")]
		[TestCase("https://docs.google.com/a/forms-elsewhere")]
		[TestCase("https://contoso.sharepoint.com/sites/x/page.aspx")]
		[TestCase("https://youtube.com.evil.example/embed/dQw4w9WgXcQ")]
		[TestCase("ftp://player.vimeo.com/video/1")]
		[TestCase("")]
		[TestCase(null)]
		public void Anything_else_is_refused(string url)
		{
			EmbedAllowList.NormalizeEmbedUrl(url).Should().BeNull();
		}

		[Test]
		public void An_allowed_frame_is_rebuilt_with_fixed_attributes_and_the_text_around_it_is_kept()
		{
			var html = "<p>Watch the drill</p><iframe class=\"ql-video\" frameborder=\"0\" allowfullscreen=\"true\" onload=\"alert(1)\" style=\"position:fixed\" " +
				"src=\"https://www.youtube.com/embed/dQw4w9WgXcQ?showinfo=0\"></iframe><p>Then answer the quiz</p>";

			var stored = StringHelpers.SanitizeHtmlWithEmbedsInString(html);

			stored.Should().Contain("Watch the drill").And.Contain("Then answer the quiz");
			stored.Should().Contain(EmbedAllowList.IframeMarkup("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ"));
			stored.Should().Contain("sandbox=\"allow-scripts allow-same-origin allow-presentation allow-popups\"");
			stored.Should().NotContain("onload").And.NotContain("position:fixed").And.NotContain("showinfo");
		}

		[Test]
		public void A_frame_that_is_not_allowed_is_removed_and_scripts_never_survive()
		{
			var html = "<p>Before</p><iframe src=\"https://evil.example.com/x\"></iframe><iframe></iframe><script>alert(1)</script><p>After</p>";

			var stored = StringHelpers.SanitizeHtmlWithEmbedsInString(html);

			stored.Should().Contain("Before").And.Contain("After");
			stored.Should().NotContain("iframe").And.NotContain("evil.example.com").And.NotContain("<script");
		}

		[Test]
		public void A_typed_token_lookalike_is_never_turned_into_a_frame()
		{
			var stored = StringHelpers.SanitizeHtmlWithEmbedsInString("<p>rgembed0000x0x and more</p>");

			stored.Should().NotContain("iframe");
			stored.Should().Contain("rgembed0000x0x");
		}

		[Test]
		public void Display_keeps_allowed_frames_only_where_embeds_are_rendered()
		{
			var html = "<p>Clip</p><iframe src=\"https://player.vimeo.com/video/76979871\"></iframe><iframe src=\"https://evil.example.com\"></iframe>";

			var shown = StringHelpers.SanitizeHtmlWithEmbedsForDisplay(html);
			shown.Should().Contain("https://player.vimeo.com/video/76979871").And.Contain("Clip");
			shown.Should().NotContain("evil.example.com");

			// The ordinary display path still strips every frame, allowed or not.
			StringHelpers.SanitizeHtmlForDisplay(html).Should().NotContain("iframe").And.Contain("Clip");
			StringHelpers.SanitizeHtmlWithEmbedsForDisplay(null).Should().BeEmpty();
			StringHelpers.SanitizeHtmlWithEmbedsForDisplay("<p>No frames</p>").Should().Be(StringHelpers.SanitizeHtmlForDisplay("<p>No frames</p>"));
		}

		[Test]
		public void The_embed_markup_encodes_the_address()
		{
			EmbedAllowList.IframeMarkup("https://docs.google.com/document/d/a/preview?x=1&y=2").Should().Contain("src=\"https://docs.google.com/document/d/a/preview?x=1&amp;y=2\"");
		}
	}
}
