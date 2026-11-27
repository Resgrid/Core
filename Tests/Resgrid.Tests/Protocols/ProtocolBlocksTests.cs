using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using File = System.IO.File;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P3 block model: rendering with an anchor per block, sanitization of every field, stable anchors across edits, P1 html
	/// versions untouched (same HTML, same hash), "Convert to blocks", the medication block (shown as authored, never computed, with the
	/// display-time dosing-tool link), steps for step-through, and the structured allow-list used on output.
	/// </summary>
	[TestFixture]
	public class ProtocolBlocksTests
	{
		private static ProtocolContentDocument Doc(params ProtocolContentBlock[] blocks) =>
			new ProtocolContentDocument { Schema = ProtocolContentDocument.BlockSchema, Blocks = blocks.ToList() };

		private static ProtocolContentDocument Sample() => Doc(
			new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = "h1", Text = "Scene safety", Level = 2 },
			new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "p1", Html = "<p>Stage until the scene is <strong>secured</strong>.</p>" },
			new ProtocolContentBlock
			{
				Type = ProtocolBlockTypes.Steps, Key = "s1", Title = "Primary survey",
				Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "a", Html = "Airway" }, new ProtocolStepItem { Key = "b", Html = "Breathing" } }
			},
			new ProtocolContentBlock { Type = ProtocolBlockTypes.Gate, Key = "g1", Html = "Before a second dose.", Contact = "Base hospital, channel 4" },
			new ProtocolContentBlock
			{
				Type = ProtocolBlockTypes.Medication, Key = "m1",
				Medication = new ProtocolMedication { Drug = "Epinephrine 1 mg/mL", DosePerKg = "0.01 mg/kg IM", MaxSingleDose = "0.5 mg", Route = "IM", RepeatInterval = "5 min" },
				Scopes = new List<ProtocolScopeTag> { new ProtocolScopeTag { Id = 3, Name = "AEMT" } }
			});

		#region Rendering and anchors

		[Test]
		public void A_block_document_renders_every_block_with_its_anchor_and_the_steps_with_theirs()
		{
			var html = ProtocolBlocks.Normalize(Sample()).Render();

			html.Should().StartWith(StructuredHtml.DocumentMarker);
			html.Should().Contain("<h2 id=\"b-h1\" class=\"rgp-heading\">Scene safety</h2>");
			html.Should().Contain("id=\"b-p1\"");
			html.Should().Contain("<li id=\"b-s1-a\" class=\"rgp-step\">Airway</li>");
			html.Should().Contain("id=\"b-g1\"");
			html.Should().Contain("Contact medical control");
			html.Should().Contain("id=\"b-m1\"");
			html.Should().Contain("AEMT", "the scope-of-practice tag is shown on the block");
		}

		[Test]
		public void Anchors_stay_the_same_when_other_blocks_change_and_when_blocks_are_reordered()
		{
			var first = ProtocolBlocks.Normalize(Sample());
			var edited = ProtocolContentDocument.Parse(first.ToJson(), null);
			edited.Blocks[1].Html = "<p>Completely rewritten paragraph.</p>";
			edited.Blocks.Reverse();
			edited.Blocks.Insert(0, new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Html = "New intro" });
			var again = ProtocolBlocks.Normalize(edited);

			again.Blocks.Where(b => b.Key != null).Select(b => b.Key).Should().Contain(new[] { "h1", "p1", "s1", "g1", "m1" });
			again.Blocks[0].Key.Should().MatchRegex("^[a-z0-9]{8}$", "a block without a key is issued one");
			var html = again.Render();
			foreach (var anchor in new[] { "b-h1", "b-p1", "b-s1-a", "b-s1-b", "b-g1", "b-m1" })
				html.Should().Contain("id=\"" + anchor + "\"");
		}

		[Test]
		public void Duplicate_and_invalid_keys_get_fresh_ones_so_every_anchor_is_unique()
		{
			var doc = ProtocolBlocks.Normalize(Doc(
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "same", Html = "one" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "same", Html = "two" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "\"><script>", Html = "three" }));

			doc.Blocks.Select(b => b.Key).Should().OnlyHaveUniqueItems();
			doc.Blocks[0].Key.Should().Be("same");
			doc.Blocks.All(b => ProtocolBlocks.IsValidKey(b.Key)).Should().BeTrue();
		}

		[Test]
		public void The_outline_is_the_heading_blocks_with_their_block_anchors()
		{
			var doc = ProtocolBlocks.Normalize(Sample());
			var outline = ProtocolContentDocument.OutlineFor(doc.ToJson(), doc.Render());

			outline.Should().ContainSingle();
			outline[0].Id.Should().Be("b-h1");
			outline[0].Text.Should().Be("Scene safety");
		}

		#endregion Rendering and anchors

		#region Sanitization

		[Test]
		public void Script_style_frames_and_event_handlers_never_survive_in_any_rich_field()
		{
			var hostile = "<p onclick=\"steal()\">Hi<script>alert(1)</script><style>p{x:1}</style><img src=\"x\" onerror=\"alert(2)\"><iframe src=\"https://evil\"></iframe></p>";
			var doc = ProtocolBlocks.Normalize(Doc(
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "p", Html = hostile },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Caution, Key = "c", Html = hostile },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "s", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "i", Html = hostile } } },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Medication, Key = "m", Medication = new ProtocolMedication { Drug = "X", Notes = hostile } }));

			var html = doc.Render();
			html.Should().NotContainAny("<script", "alert(1)", "<style", "onclick", "onerror", "<iframe", "steal()");
		}

		[Test]
		public void Plain_fields_are_encoded_and_angle_brackets_in_text_stay_readable()
		{
			var doc = ProtocolBlocks.Normalize(Doc(
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = "h", Text = "<script>alert(1)</script> & co" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "p", Html = "pt <unconscious> bp 80" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Table, Key = "t", Rows = new List<List<string>> { new List<string> { "<b onmouseover=x>cell</b>" } } }));

			var html = doc.Render();
			html.Should().NotContain("<script");
			html.Should().Contain("&lt;script&gt;");
			html.Should().Contain("pt &lt;unconscious&gt; bp 80");
			html.Should().NotContain("onmouseover=x>", "a table cell is plain text");
		}

		[Test]
		public void Links_videos_and_images_accept_only_web_addresses()
		{
			var doc = ProtocolBlocks.Normalize(Doc(
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Link, Key = "l1", LinkKind = ProtocolLinkKinds.Url, Url = "javascript:alert(1)" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Video, Key = "v1", Url = "data:text/html,boom" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Image, Key = "i1", Url = "vbscript:x" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Link, Key = "l2", LinkKind = ProtocolLinkKinds.Protocol, RefId = "12", Label = "Burns" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Video, Key = "v2", Url = "https://video.example.org/watch?v=1", Label = "Demo" }));

			doc.Blocks.Select(b => b.Key).Should().Equal("l2", "v2");
			var html = doc.Render();
			html.Should().Contain("href=\"/User/Protocols/View/12\"");
			html.Should().Contain("https://video.example.org/watch?v=1");
			html.Should().NotContainAny("javascript:", "data:text", "vbscript:");
		}

		[Test]
		public void The_structured_allow_list_keeps_block_anchors_and_tables_and_strips_everything_else()
		{
			var stored = StructuredHtml.DocumentMarker + "<div id=\"b-ok\" class=\"rgp-block rgp-table\" onclick=\"x()\" style=\"color:red\"><table class=\"rgp-grid\"><tr><td>5 mg</td></tr></table></div>"
				+ "<div id=\"not-an-anchor\" class=\"evil rgp-block\">text</div><script>alert(1)</script><a href=\"javascript:alert(1)\">x</a></div>";

			var clean = StructuredHtml.Sanitize(stored);

			clean.Should().Contain("id=\"b-ok\"");
			clean.Should().Contain("<td>5 mg</td>");
			clean.Should().NotContainAny("onclick", "style=", "not-an-anchor", "evil", "<script", "javascript:");
		}

		[Test]
		public void Sanitizing_the_rendered_document_again_changes_nothing()
		{
			var html = ProtocolBlocks.Normalize(Sample()).Render();
			StructuredHtml.Sanitize(html).Should().Be(html);
			ProtocolContentDocument.DisplayHtml(html).Should().Be(html);
		}

		#endregion Sanitization

		#region P1 html versions untouched

		[Test]
		public void A_stored_html_version_renders_and_hashes_exactly_as_before()
		{
			const string stored = "<h2>Scene safety</h2><p>Stage.</p><ol><li>Airway</li></ol>";
			var hash = ProtocolContentHash.Compute(stored);
			var document = ProtocolContentDocument.Parse(ProtocolContentDocument.FromStoredHtml(stored).ToJson(), stored);

			document.IsBlockModel.Should().BeFalse();
			document.Render().Should().Be(stored);
			ProtocolContentHash.Compute(document.Render()).Should().Be(hash);
			ProtocolContentDocument.DisplayHtml(stored).Should().Contain("id=\"p-scene-safety\"", "P1 html keeps its heading anchors");
			ProtocolContentDocument.OutlineFor(document.ToJson(), stored).Single().Id.Should().Be("p-scene-safety");
		}

		[Test]
		public void Convert_to_blocks_splits_headings_steps_and_paragraphs_and_leaves_the_source_alone()
		{
			const string stored = "<p>Intro</p><h2>Primary survey</h2><ol><li>Airway</li><li>Breathing</li></ol><p>Then reassess.</p><h3>Notes</h3><p>Done.</p>";
			var source = ProtocolContentDocument.FromStoredHtml(stored);
			var sourceJson = source.ToJson();

			var converted = source.ConvertToBlocks();

			source.ToJson().Should().Be(sourceJson);
			converted.IsBlockModel.Should().BeTrue();
			converted.Blocks.Select(b => b.Type).Should().Equal(ProtocolBlockTypes.Paragraph, ProtocolBlockTypes.Heading, ProtocolBlockTypes.Steps,
				ProtocolBlockTypes.Paragraph, ProtocolBlockTypes.Heading, ProtocolBlockTypes.Paragraph);
			converted.Blocks[2].Items.Select(i => StringHelpers.HtmlToPlainText(i.Html)).Should().Equal("Airway", "Breathing");
			converted.Blocks[4].Level.Should().Be(3);
			ProtocolSteps.Extract(converted.ToJson(), converted.Render()).Select(s => s.Text).Should().Equal("Airway", "Breathing");
		}

		#endregion P1 html versions untouched

		#region Medication: shown as authored, never computed

		[Test]
		public void A_medication_shows_its_fields_exactly_as_authored_with_the_notice_and_a_dosing_slot()
		{
			var html = ProtocolBlocks.Normalize(Sample()).Render();

			html.Should().Contain("<td>0.01 mg/kg IM</td>");
			html.Should().Contain("<td>0.5 mg</td>");
			html.Should().Contain("Nothing is calculated");
			html.Should().Contain("rgp-dosing-slot");
		}

		[Test]
		public void The_dosing_tool_link_is_added_at_display_time_only_for_an_https_address()
		{
			var stored = ProtocolBlocks.Normalize(Sample()).Render();
			var display = ProtocolContentDocument.DisplayHtml(stored);

			ProtocolContentDocument.InjectDosingLinks(display, "https://dosing.example.org/app?x=1&y=2", "Open the dosing tool").Should()
				.Contain("href=\"https://dosing.example.org/app?x=1&amp;y=2\"").And.Contain("Open the dosing tool");
			ProtocolContentDocument.InjectDosingLinks(display, "javascript:alert(1)", "x").Should().Be(display);
			ProtocolContentDocument.InjectDosingLinks(display, "http://plain.example.org", "x").Should().Be(display);
			ProtocolContentHash.Compute(stored).Should().Be(ProtocolContentHash.Compute(ProtocolBlocks.Normalize(Sample()).Render()),
				"the link is never part of the hashed text, so the department setting never changes an approved version");
		}

		[Test]
		public void Every_medication_field_is_text_so_nothing_can_compute_a_dose()
		{
			typeof(ProtocolMedication).GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.PropertyType != typeof(string)).Select(p => p.Name).Should().BeEmpty();
		}

		/// <summary>
		/// Decision 3 guard: no code path multiplies or divides a dose. Every line of C# and JavaScript that touches a medication dose field
		/// (comments and string literals removed) is free of arithmetic and numeric parsing.
		/// </summary>
		[Test]
		public void No_code_path_multiplies_or_divides_a_dose()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln")))
				root = root.Parent;
			root.Should().NotBeNull();

			var doseFields = new Regex(@"\b(DosePerKg|MaxSingleDose|MaxTotalDose|Concentration|dosePerKg|maxSingleDose|maxTotalDose|concentration)\b", RegexOptions.Compiled);
			var arithmetic = new Regex(@"(?<![/*])[*/](?![/*=])|\b(Parse|TryParse|parseFloat|parseInt|Number\(|Math\.)\b|\bdecimal\b|\bdouble\b", RegexOptions.Compiled);
			var strings = new Regex(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.Compiled);
			var scanned = 0;
			var problems = new List<string>();
			foreach (var folder in new[] { "Core", "Web", "Workers", "Providers", "Repositories" })
			{
				foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, folder), "*.*", SearchOption.AllDirectories)
					.Where(f => (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || (f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && f.Contains(Path.Combine("js", "app"))))
						&& !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
						&& !f.Contains("node_modules")))
				{
					var lines = File.ReadAllLines(file);
					for (var i = 0; i < lines.Length; i++)
					{
						var code = lines[i];
						var comment = code.IndexOf("//", StringComparison.Ordinal);
						if (comment >= 0)
							code = code.Substring(0, comment);
						code = strings.Replace(code, "\"\"");
						if (!doseFields.IsMatch(code))
							continue;
						scanned++;
						if (arithmetic.IsMatch(code))
							problems.Add(Path.GetRelativePath(root.FullName, file) + ":" + (i + 1) + ": " + lines[i].Trim());
					}
				}
			}

			scanned.Should().BeGreaterThan(5, "the scan must see the medication fields, or it proves nothing");
			problems.Should().BeEmpty("doses are displayed as authored, never computed (Protocols plan decision 3)");
		}

		#endregion Medication: shown as authored, never computed

		#region Steps and logs

		[Test]
		public void Steps_are_the_step_items_and_gates_in_reading_order_with_stable_ids()
		{
			var doc = ProtocolBlocks.Normalize(Sample());
			var steps = ProtocolSteps.Extract(doc.ToJson(), doc.Render());

			steps.Select(s => s.Id).Should().Equal("s1-a", "s1-b", "g1");
			steps[0].Group.Should().Be("Primary survey");
			steps[2].Kind.Should().Be(ProtocolSteps.GateKind);
			steps[2].Text.Should().Contain("Base hospital, channel 4");
			steps[0].Anchor.Should().Be("b-s1-a");
		}

		[Test]
		public void Variant_steps_carry_their_variant_and_html_versions_get_steps_from_ordered_lists()
		{
			var doc = ProtocolBlocks.Normalize(Doc(new ProtocolContentBlock
			{
				Type = ProtocolBlockTypes.Variants, Key = "v",
				Variants = new List<ProtocolVariant>
				{
					new ProtocolVariant { Key = "ad", Label = "Adult", Blocks = new List<ProtocolContentBlock> { new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "sa", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "x", Html = "Adult step" } } } } },
					new ProtocolVariant { Key = "pe", Label = "Pediatric", Blocks = new List<ProtocolContentBlock> { new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = "nope", Text = "Not allowed here" }, new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "sp", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "y", Html = "Child step" } } } } }
				}
			}));

			doc.Blocks[0].Variants[1].Blocks.Should().ContainSingle("a heading is not allowed inside a variant");
			ProtocolSteps.Extract(doc.ToJson(), doc.Render()).Select(s => s.Group).Should().Equal("Adult", "Pediatric");
			ProtocolSteps.Extract(null, "<ol><li>One</li><li>Two</li></ol>").Select(s => s.Id).Should().Equal("h1", "h2");
		}

		[Test]
		public void A_step_log_is_append_only_and_a_step_state_is_its_last_event()
		{
			var log = new ProtocolStepLog { StartedOn = DateTime.UtcNow, StartedByUserId = "u1" };
			log.Entries.Add(new ProtocolStepLogEntry { StepId = "s1-a", State = (int)ProtocolStepStates.Done, At = DateTime.UtcNow, UserId = "u1" });
			log.Entries.Add(new ProtocolStepLogEntry { StepId = "s1-a", State = (int)ProtocolStepStates.Pending, At = DateTime.UtcNow, UserId = "u2" });

			var parsed = ProtocolStepLog.Parse(log.ToJson());

			parsed.Entries.Should().HaveCount(2);
			parsed.StateOf("s1-a").Should().Be(ProtocolStepStates.Pending);
			parsed.StateOf("other").Should().Be(ProtocolStepStates.Pending);
			parsed.LastFor("s1-a").UserId.Should().Be("u2");
			ProtocolStepLog.Parse("{not json").Should().BeNull();
		}

		#endregion Steps and logs

		#region Knowledge citations (K2 part, R-S1)

		[Test]
		public void Block_versions_are_chunked_per_block_and_heading_section_citing_block_anchors()
		{
			var doc = ProtocolBlocks.Normalize(Sample());
			var sections = KnowledgeChunker.SplitBlocks(doc);

			sections.Select(s => s.Anchor).Should().Equal("b-h1", "b-s1", "b-g1", "b-m1");
			sections[0].Text.Should().Contain("secured");
			sections[0].HeadingPath.Should().Be("Scene safety");
			sections[3].Text.Should().Contain("Dose per kg: 0.01 mg/kg IM").And.Contain("Scope of practice: AEMT");
			var html = doc.Render();
			foreach (var anchor in sections.Select(s => s.Anchor))
				html.Should().Contain("id=\"" + anchor + "\"", "a citation must open an anchor the reading view has");
		}

		[Test]
		public void Html_versions_keep_their_heading_anchors_and_paragraphs_before_a_heading_cite_their_own_block()
		{
			KnowledgeChunker.SplitBlocks(ProtocolContentDocument.FromStoredHtml("<h2>Scene safety</h2><p>Stage.</p>")).Single().Anchor.Should().Be("p-scene-safety");

			var doc = ProtocolBlocks.Normalize(Doc(new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "intro", Html = "Before any heading." }));
			KnowledgeChunker.SplitBlocks(doc).Single().Anchor.Should().Be("b-intro");
		}

		#endregion Knowledge citations (K2 part, R-S1)

		#region Translations keep the source's structure

		[Test]
		public void A_translation_takes_only_wording_and_never_changes_a_dose_a_link_or_the_structure()
		{
			var source = ProtocolBlocks.Normalize(Sample());
			var given = ProtocolContentDocument.Parse(source.ToJson(), null);
			given.Blocks[0].Text = "Sicherheit an der Einsatzstelle";
			given.Blocks[4].Medication.DosePerKg = "1 mg/kg";
			given.Blocks[4].Medication.Indication = "Anaphylaxie";
			given.Blocks.Add(new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "extra", Html = "smuggled" });
			given.Blocks.RemoveAt(1);

			var aligned = ProtocolTranslationRules.Align(source, given);

			aligned.Blocks.Select(b => b.Key).Should().Equal(source.Blocks.Select(b => b.Key));
			aligned.Blocks[0].Text.Should().Be("Sicherheit an der Einsatzstelle");
			aligned.Blocks[1].Html.Should().Be(source.Blocks[1].Html, "a block the translator left out keeps the source wording");
			aligned.Blocks[4].Medication.DosePerKg.Should().Be("0.01 mg/kg IM");
			aligned.Blocks[4].Medication.Indication.Should().Be("Anaphylaxie");
			aligned.Render().Should().NotContain("smuggled");
		}

		#endregion Translations keep the source's structure
	}
}
