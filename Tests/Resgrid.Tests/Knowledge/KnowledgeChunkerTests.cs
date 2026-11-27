using System;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>K1 chunker: one chunk per heading section, heading paths, and anchors that are exactly the ids the reading view puts on headings.</summary>
	[TestFixture]
	public class KnowledgeChunkerTests
	{
		private const string Html =
			"<p>Read before every call.</p>" +
			"<h2>Scene safety</h2><p>Stage uphill and upwind.</p>" +
			"<h3>PPE</h3><p>Gloves, eye protection and a mask.</p>" +
			"<h3>Hazards</h3><p>Downed lines and traffic.</p>" +
			"<h2>Airway</h2><p>Open the airway with a head tilt.</p>" +
			"<h2></h2><p>Still airway text.</p>" +
			"<h2>Scene safety</h2><p>Second section with the same title.</p>";

		[Test]
		public void Sections_follow_the_headings_with_their_paths_and_the_text_before_the_first_heading_has_no_anchor()
		{
			var sections = KnowledgeChunker.SplitHtml(Html);

			sections.Select(s => s.HeadingPath).Should().Equal(null, "Scene safety", "Scene safety › PPE", "Scene safety › Hazards", "Airway", "Scene safety");
			sections[0].Anchor.Should().BeNull();
			sections[0].Text.Should().Be("Read before every call.");
			sections[2].Text.Should().Be("Gloves, eye protection and a mask.");
			// A heading without text starts no section: its body stays with the section above.
			sections[4].Text.Should().Contain("Open the airway").And.Contain("Still airway text.");
		}

		[Test]
		public void Anchors_are_the_ids_the_reading_view_injects_on_the_headings_including_repeated_titles()
		{
			var sections = KnowledgeChunker.SplitHtml(Html);
			var injected = Regex.Matches(ProtocolContentDocument.InjectAnchors(Html), "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

			sections.Where(s => s.Anchor != null).Select(s => s.Anchor).Distinct().Should().Equal(injected);
			sections.Select(s => s.Anchor).Should().Equal(null, "p-scene-safety", "p-ppe", "p-hazards", "p-airway", "p-scene-safety-2");
			ProtocolContentDocument.Outline(Html).Select(a => a.Id).Should().Equal(injected);
		}

		[Test]
		public void A_long_section_is_cut_into_parts_that_keep_its_anchor_and_path()
		{
			var body = string.Join(" ", Enumerable.Range(1, 120).Select(i => "Sentence number " + i + " explains a step."));
			var sections = KnowledgeChunker.SplitHtml("<h2>Long</h2><p>" + body + "</p>", 400);

			sections.Count.Should().BeGreaterThan(3);
			sections.Should().OnlyContain(s => s.Anchor == "p-long" && s.HeadingPath == "Long" && s.Text.Length <= 400);
			string.Join(" ", sections.Select(s => s.Text)).Should().Be(body);
		}

		[Test]
		public void File_text_is_chunked_by_page_with_a_page_anchor()
		{
			var sections = KnowledgeChunker.SplitPages(new[]
			{
				new KnowledgePageText { AttachmentId = "12", FileName = "SOG.pdf", PageNumber = 2, Text = "Second page." },
				new KnowledgePageText { AttachmentId = "12", FileName = "SOG.pdf", PageNumber = 1, Text = "First page." },
				new KnowledgePageText { AttachmentId = "12", FileName = "SOG.pdf", PageNumber = 3, Text = "   " }
			});

			sections.Select(s => s.Anchor).Should().Equal("page-12-1", "page-12-2");
			sections.Select(s => s.HeadingPath).Should().Equal("SOG.pdf › p. 1", "SOG.pdf › p. 2");
		}

		[Test]
		public void Empty_or_markup_only_content_has_no_sections()
		{
			KnowledgeChunker.SplitHtml(null).Should().BeEmpty();
			KnowledgeChunker.SplitHtml("<h2>Only a heading</h2>").Should().BeEmpty();
			KnowledgeChunker.SplitPages(null).Should().BeEmpty();
		}

		[Test]
		public void The_embedding_input_carries_title_and_path_and_its_hash_is_stable()
		{
			var chunk = new KnowledgeChunk { Title = "SOG 4", HeadingPath = "Scene safety › PPE", Text = "Gloves." };
			KnowledgeChunker.EmbeddingInput(chunk).Should().Be("SOG 4 › Scene safety › PPE\nGloves.");
			KnowledgeChunker.Hash(KnowledgeChunker.EmbeddingInput(chunk)).Should().Be(KnowledgeChunker.Hash("SOG 4 › Scene safety › PPE\nGloves.")).And.HaveLength(64);
		}

		[Test]
		public void Citation_links_open_the_exact_version_and_section_and_refuse_unsafe_anchors()
		{
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Protocol, "12", "34", "p-scene-safety").Should().Be("/User/Protocols/View/12?versionId=34#p-scene-safety");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Training, "5", "r1", "p-airway").Should().Be("/User/Trainings/View?trainingId=5#p-airway");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Protocol, "12", "34", "x\" onclick=\"y").Should().Be("/User/Protocols/View/12?versionId=34");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Academy, "05-calls", "a", "05-calls", "https://academy.resgrid.com/").Should().Be("https://academy.resgrid.com/#lesson=05-calls");
			KnowledgeLinks.WebPath(99, "1", null, null).Should().BeNull();
		}
	}
}
