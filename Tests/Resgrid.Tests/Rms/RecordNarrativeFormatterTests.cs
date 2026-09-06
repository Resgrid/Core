using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordNarrativeFormatterTests
	{
		[TestCase("<p onclick='steal()'>Safe <strong>text</strong><img src='https://evil.invalid/a' onerror='steal()'></p><script>secret()</script>")]
		[TestCase("<svg><a><script>secret()</script></a></svg><p>Safe <strong>text</strong></p><iframe src='https://evil.invalid'></iframe>")]
		public void Rich_text_keeps_basic_formatting_and_drops_active_content_attributes_and_external_resources(string input)
		{
			var html = RecordNarrativeFormatter.ForStorage(input);
			html.Should().Contain("<strong>text</strong>").And.NotContain("onclick").And.NotContain("img").And.NotContain("script").And.NotContain("secret").And.NotContain("evil").And.NotContain("iframe");
			RecordNarrativeFormatter.Render(html).Should().Be(html);
		}
		[TestCase("<p><br></p>")]
		[TestCase("<p>&nbsp;</p>")]
		[TestCase("<script>only active content</script>")]
		public void Empty_rich_text_does_not_satisfy_required_narrative(string input) => RecordNarrativeFormatter.HasText(input).Should().BeFalse();
		[Test]
		public void Existing_plain_text_and_comparisons_keep_their_stored_value()
		{
			const string text = "Crew: 2 < 3 & 5 > 4\nAll safe.";
			RecordNarrativeFormatter.ForStorage(text).Should().Be(text);
			RecordNarrativeFormatter.Render(text).Should().Contain("&lt;").And.Contain("&amp;");
		}
	}
}
