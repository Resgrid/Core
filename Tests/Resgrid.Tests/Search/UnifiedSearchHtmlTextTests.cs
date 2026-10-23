using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Hits are plain text: the search page and the palette encode them. Rows projected before the builders stripped markup
	/// still hold the HTML of e-mailed calls (`&lt;div dir="ltr"&gt;...`), so the service strips it when it reads them, and the
	/// tags never show up as literal text in the summary or the excerpt.
	/// </summary>
	public partial class UnifiedSearchServiceTests
	{
		[Test]
		public async Task Stored_html_in_old_rows_reaches_the_hit_as_plain_text()
		{
			_rows[0].Summary = "<div dir=\"ltr\">Just a test!<div><br></div><div>Shawn</div></div> · Fire";
			_rows[0].SearchText = "1 Main St <div dir=\"ltr\">Just a test!<div><br></div><div>Shawn</div></div> Fire &amp; rescue";

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "shawn" }, Principal("Call:View"));

			var hit = result.Hits.Single(h => h.EntityId == "1");
			hit.Summary.Should().Be("Just a test! Shawn · Fire");
			hit.Snippet.Should().Be("1 Main St Just a test! Shawn Fire & rescue");
			hit.Title.Should().Be("Current title");
		}

		[Test]
		public async Task Plain_text_hits_are_returned_unchanged()
		{
			_rows[0].Summary = "Smoke showing · Fire";
			_rows[0].SearchText = "1 Main St Smoke showing pressure < 90";

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "pressure" }, Principal("Call:View"));

			var hit = result.Hits.Single(h => h.EntityId == "1");
			hit.Summary.Should().Be("Smoke showing · Fire");
			hit.Snippet.Should().Be("1 Main St Smoke showing pressure < 90");
		}
	}
}
