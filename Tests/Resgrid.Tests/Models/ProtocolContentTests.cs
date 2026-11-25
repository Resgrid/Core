using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// The pure rules under the protocol library (P1): which version status may move to which, how a published version is
	/// numbered, when reviews fall due and which reminder stage is due, what a user may never delete; the block document's
	/// round trip (its rendered HTML and hash are stable, and display anchors never change the stored text); the redline's
	/// word marks; the content hash; setting 134's retention; and who may decide an approval step.
	/// </summary>
	[TestFixture]
	public class ProtocolContentTests
	{
		#region Lifecycle

		private static readonly (ProtocolVersionStatuses From, ProtocolVersionStatuses To)[] Allowed =
		{
			(ProtocolVersionStatuses.Draft, ProtocolVersionStatuses.InReview),
			(ProtocolVersionStatuses.InReview, ProtocolVersionStatuses.Approved),
			(ProtocolVersionStatuses.InReview, ProtocolVersionStatuses.Draft),
			(ProtocolVersionStatuses.Approved, ProtocolVersionStatuses.Published),
			(ProtocolVersionStatuses.Approved, ProtocolVersionStatuses.Draft),
			(ProtocolVersionStatuses.Published, ProtocolVersionStatuses.Superseded),
			(ProtocolVersionStatuses.Published, ProtocolVersionStatuses.Retired)
		};

		[Test]
		public void CanMove_allows_exactly_the_lifecycle_transitions_and_nothing_else()
		{
			var statuses = Enum.GetValues(typeof(ProtocolVersionStatuses)).Cast<ProtocolVersionStatuses>().ToList();

			foreach (var from in statuses)
				foreach (var to in statuses)
					ProtocolLifecycle.CanMove((int)from, (int)to).Should().Be(Allowed.Contains((from, to)), $"{from} -> {to}");
		}

		[TestCase(ProtocolVersionStatuses.Draft, ProtocolVersionStatuses.Published)]
		[TestCase(ProtocolVersionStatuses.Draft, ProtocolVersionStatuses.Approved)]
		[TestCase(ProtocolVersionStatuses.InReview, ProtocolVersionStatuses.Published)]
		[TestCase(ProtocolVersionStatuses.Published, ProtocolVersionStatuses.Draft)]
		[TestCase(ProtocolVersionStatuses.Superseded, ProtocolVersionStatuses.Published)]
		[TestCase(ProtocolVersionStatuses.Retired, ProtocolVersionStatuses.Published)]
		[TestCase(ProtocolVersionStatuses.Legacy, ProtocolVersionStatuses.Draft)]
		public void CanMove_refuses_skipping_review_and_reviving_final_versions(ProtocolVersionStatuses from, ProtocolVersionStatuses to)
		{
			ProtocolLifecycle.CanMove((int)from, (int)to).Should().BeFalse();
		}

		[Test]
		public void CanMove_refuses_an_unknown_status()
		{
			ProtocolLifecycle.CanMove(42, (int)ProtocolVersionStatuses.Published).Should().BeFalse();
		}

		[Test]
		public void NextNumber_starts_at_1_0_and_counts_minor_and_major_revisions()
		{
			ProtocolLifecycle.NextNumber(null, false).Should().Be((1, 0));
			ProtocolLifecycle.NextNumber(null, true).Should().Be((1, 0));
			// Legacy text (0.n) is not a published number: the first real publish is still 1.0.
			ProtocolLifecycle.NextNumber(new ProtocolVersion { Major = 0, Minor = 3 }, false).Should().Be((1, 0));

			ProtocolLifecycle.NextNumber(new ProtocolVersion { Major = 1, Minor = 0 }, false).Should().Be((1, 1));
			ProtocolLifecycle.NextNumber(new ProtocolVersion { Major = 1, Minor = 3 }, true).Should().Be((2, 0));
			ProtocolLifecycle.NextNumber(new ProtocolVersion { Major = 2, Minor = 4 }, false).Should().Be((2, 5));
		}

		[Test]
		public void NextReview_adds_the_interval_and_is_null_without_one()
		{
			var from = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

			ProtocolLifecycle.NextReview(from, 365).Should().Be(new DateTime(2027, 11, 24, 12, 0, 0, DateTimeKind.Utc));
			ProtocolLifecycle.NextReview(from, 30).Should().Be(from.AddDays(30));
			ProtocolLifecycle.NextReview(from, null).Should().BeNull();
			ProtocolLifecycle.NextReview(from, 0).Should().BeNull();
			ProtocolLifecycle.NextReview(from, -7).Should().BeNull();
		}

		[Test]
		public void IsRetained_keeps_everything_but_an_unsubmitted_draft()
		{
			foreach (ProtocolVersionStatuses status in Enum.GetValues(typeof(ProtocolVersionStatuses)))
				ProtocolLifecycle.IsRetained((int)status).Should().Be(status != ProtocolVersionStatuses.Draft, status.ToString());
		}

		[Test]
		public void IsContentEditable_only_for_a_draft()
		{
			foreach (ProtocolVersionStatuses status in Enum.GetValues(typeof(ProtocolVersionStatuses)))
				ProtocolLifecycle.IsContentEditable((int)status).Should().Be(status == ProtocolVersionStatuses.Draft, status.ToString());
		}

		[Test]
		public void Label_is_shown_only_once_a_version_has_a_number()
		{
			new ProtocolVersion { Status = (int)ProtocolVersionStatuses.Draft, Major = 1, Minor = 2 }.Label.Should().BeNull();
			new ProtocolVersion { Status = (int)ProtocolVersionStatuses.InReview }.Label.Should().BeNull();
			new ProtocolVersion { Status = (int)ProtocolVersionStatuses.Approved, Major = 0 }.Label.Should().BeNull();
			// Scheduled: approved with its number chosen.
			new ProtocolVersion { Status = (int)ProtocolVersionStatuses.Approved, Major = 1, Minor = 1 }.Label.Should().Be("1.1");
			new ProtocolVersion { Status = (int)ProtocolVersionStatuses.Published, Major = 2, Minor = 0 }.Label.Should().Be("2.0");
			new ProtocolVersion { Status = (int)ProtocolVersionStatuses.Legacy, Major = 0, Minor = 4 }.Label.Should().Be("0.4");
		}

		#endregion Lifecycle

		#region Review reminders

		private static readonly DateTime ReviewDue = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);

		[Test]
		public void DueStage_is_nothing_before_thirty_days_out()
		{
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddDays(-31)).Should().Be(ProtocolReviewReminderStages.None);
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddDays(-30).AddSeconds(-1)).Should().Be(ProtocolReviewReminderStages.None);
		}

		[Test]
		public void DueStage_is_thirty_days_from_T_minus_30_until_T_minus_7()
		{
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddDays(-30)).Should().Be(ProtocolReviewReminderStages.ThirtyDays);
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddDays(-8)).Should().Be(ProtocolReviewReminderStages.ThirtyDays);
		}

		[Test]
		public void DueStage_is_seven_days_from_T_minus_7_until_due()
		{
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddDays(-7)).Should().Be(ProtocolReviewReminderStages.SevenDays);
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddSeconds(-1)).Should().Be(ProtocolReviewReminderStages.SevenDays);
		}

		[Test]
		public void DueStage_is_overdue_from_the_due_moment_on()
		{
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue).Should().Be(ProtocolReviewReminderStages.Overdue);
			ProtocolReviewReminders.DueStage(ReviewDue, ReviewDue.AddDays(90)).Should().Be(ProtocolReviewReminderStages.Overdue);
		}

		#endregion Review reminders

		#region Content document

		private const string CleanHtml = "<h2>Scene safety</h2><p>Stay upwind of the spill.</p><h3>Isolation</h3><p>Deny entry.</p>";

		[Test]
		public void FromHtml_renders_clean_html_unchanged_and_survives_a_json_round_trip()
		{
			var document = ProtocolContentDocument.FromHtml(CleanHtml);
			var rendered = document.Render();

			rendered.Should().Be(CleanHtml);
			document.Blocks.Should().ContainSingle().Which.Type.Should().Be(ProtocolContentDocument.HtmlBlockType);

			var json = document.ToJson();
			var parsed = ProtocolContentDocument.Parse(json);

			parsed.Render().Should().Be(rendered);
			parsed.ToJson().Should().Be(json);
			ProtocolContentHash.Compute(parsed.Render()).Should().Be(ProtocolContentHash.Compute(rendered));
			// Sanitizing is idempotent: saving the saved text again changes nothing, so the hash does not drift on resave.
			ProtocolContentDocument.FromHtml(rendered).Render().Should().Be(rendered);
		}

		[Test]
		public void FromHtml_strips_script_before_it_is_stored_and_hashed()
		{
			var document = ProtocolContentDocument.FromHtml("<p>Deny entry.</p><script>alert(1)</script><img src=x onerror=alert(2)>");

			var rendered = document.Render();

			rendered.Should().NotContain("<script");
			rendered.Should().NotContain("onerror");
			rendered.Should().Contain("Deny entry.");
		}

		[Test]
		public void FromStoredHtml_never_re_sanitizes_so_the_stored_hash_stays_valid()
		{
			// Whitespace and attribute order a sanitizer could normalize: stored text must come back byte for byte.
			const string stored = "<p  class=\"note\" >Stay   upwind</p>\n";

			ProtocolContentDocument.FromStoredHtml(stored).Render().Should().Be(stored);
			ProtocolContentDocument.Parse(null, stored).Render().Should().Be(stored);
			ProtocolContentDocument.Parse("{ not json", stored).Render().Should().Be(stored);
			ProtocolContentDocument.Parse(null, null).Render().Should().BeEmpty();
		}

		[Test]
		public void Outline_derives_unique_anchors_from_the_headings()
		{
			const string html = "<h2>Scene safety</h2><p>x</p><h3>Scene safety</h3><h2>Caf\x00e9 &amp; d\x00e9cor</h2><h4> </h4><h5>Too deep</h5>";

			var anchors = ProtocolContentDocument.Outline(html, "b1");

			anchors.Select(a => a.Id).Should().Equal("p-scene-safety", "p-scene-safety-2", "p-cafe-decor");
			anchors.Select(a => a.Level).Should().Equal(2, 3, 2);
			anchors[2].Text.Should().Be("Caf\x00e9 & d\x00e9cor");
			anchors.Should().OnlyContain(a => a.Block == "b1");
		}

		[Test]
		public void Anchors_are_added_only_at_display_and_never_change_the_stored_html_or_its_hash()
		{
			var document = ProtocolContentDocument.FromStoredHtml(CleanHtml);
			var stored = document.Render();
			var hash = ProtocolContentHash.Compute(stored);

			var outline = ProtocolContentDocument.Outline(stored, "b1");
			var display = ProtocolContentDocument.InjectAnchors(stored);

			display.Should().Contain("<h2 id=\"p-scene-safety\" tabindex=\"-1\">Scene safety</h2>");
			display.Should().Contain("<h3 id=\"p-isolation\" tabindex=\"-1\">Isolation</h3>");
			outline.Select(a => a.Id).Should().Equal("p-scene-safety", "p-isolation");
			document.Render().Should().Be(stored);
			ProtocolContentHash.Compute(document.Render()).Should().Be(hash);
			document.Anchors.Select(a => a.Id).Should().Equal(outline.Select(a => a.Id));
		}

		[Test]
		public void A_block_type_this_reader_does_not_know_renders_nothing_but_is_kept()
		{
			const string json = "{\"schema\":1,\"blocks\":[{\"type\":\"html\",\"key\":\"b1\",\"html\":\"<p>Deny entry.</p>\"},{\"type\":\"steps\",\"key\":\"b2\"}],\"anchors\":[]}";

			var document = ProtocolContentDocument.Parse(json);

			document.Render().Should().Be("<p>Deny entry.</p>");
			document.Blocks.Should().HaveCount(2);
			document.ToJson().Should().Contain("\"steps\"");
		}

		[Test]
		public void Slug_folds_accents_and_never_comes_back_empty()
		{
			ProtocolContentDocument.Slug("Caf\x00e9 d\x00e9cor!").Should().Be("cafe-decor");
			ProtocolContentDocument.Slug("!!!").Should().Be("section");
			ProtocolContentDocument.Slug(new string('a', 80)).Should().HaveLength(60);
		}

		#endregion Content document

		#region Version diff

		[Test]
		public void Compare_marks_inserted_words_inside_a_changed_paragraph()
		{
			var diff = ProtocolVersionDiff.Compare("<p>Stay upwind of the spill.</p>", "<p>Stay uphill and upwind of the spill.</p>");

			diff.HasChanges.Should().BeTrue();
			diff.WordsAdded.Should().Be(2);
			diff.WordsRemoved.Should().Be(0);
			var paragraph = diff.Paragraphs.Should().ContainSingle().Subject;
			paragraph.Changed.Should().BeTrue();
			paragraph.Segments.Select(s => (s.Kind, s.Text)).Should().Equal(
				(ProtocolDiffKinds.Equal, "Stay"),
				(ProtocolDiffKinds.Inserted, "uphill and"),
				(ProtocolDiffKinds.Equal, "upwind of the spill."));
		}

		[Test]
		public void Compare_marks_deleted_words_inside_a_changed_paragraph()
		{
			var diff = ProtocolVersionDiff.Compare("<p>Call the duty officer now.</p>", "<p>Call the officer now.</p>");

			diff.WordsRemoved.Should().Be(1);
			diff.WordsAdded.Should().Be(0);
			diff.Paragraphs.Single().Segments.Should().ContainSingle(s => s.Kind == ProtocolDiffKinds.Deleted).Which.Text.Should().Be("duty");
		}

		[Test]
		public void Compare_marks_whole_added_and_removed_paragraphs_and_flags_headings()
		{
			var diff = ProtocolVersionDiff.Compare(
				"<h2>Scene safety</h2><p>Stay upwind.</p><p>Old note here.</p>",
				"<h2>Scene safety</h2><p>Stay upwind.</p>");

			diff.WordsRemoved.Should().Be(3);
			diff.Paragraphs.Should().HaveCount(3);
			diff.Paragraphs[0].IsHeading.Should().BeTrue();
			diff.Paragraphs[0].Changed.Should().BeFalse();
			diff.Paragraphs[2].Segments.Should().ContainSingle().Which.Kind.Should().Be(ProtocolDiffKinds.Deleted);

			var added = ProtocolVersionDiff.Compare(null, "<p>First issue.</p>");
			added.WordsAdded.Should().Be(2);
			added.Paragraphs.Single().Segments.Single().Kind.Should().Be(ProtocolDiffKinds.Inserted);
		}

		[Test]
		public void Compare_of_the_same_text_has_no_changes_and_segments_are_plain_text()
		{
			var same = ProtocolVersionDiff.Compare(CleanHtml, CleanHtml);
			same.HasChanges.Should().BeFalse();
			same.Paragraphs.Should().OnlyContain(p => !p.Changed);

			var marked = ProtocolVersionDiff.Compare("<p>Use gloves</p>", "<p>Use <b>nitrile</b> gloves &amp; goggles</p>");
			marked.Paragraphs.SelectMany(p => p.Segments).Should().OnlyContain(s => !s.Text.Contains("<"));
			marked.Paragraphs.Single().Segments.Where(s => s.Kind == ProtocolDiffKinds.Inserted).Select(s => s.Text)
				.Should().Equal("nitrile", "& goggles");
		}

		#endregion Version diff

		#region Content hash

		[Test]
		public void ProtocolContentHash_is_lower_case_sha256_hex_of_the_utf8_text()
		{
			ProtocolContentHash.Compute("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
			ProtocolContentHash.Compute(null).Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
			ProtocolContentHash.Compute(string.Empty).Should().Be(ProtocolContentHash.Compute(null));

			const string text = "<p>Caf\x00e9 \x2013 \x05e9\x05dc\x05d5\x05dd</p>";
			var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
			var hash = ProtocolContentHash.Compute(text);
			hash.Should().Be(expected);
			hash.Should().MatchRegex("^[0-9a-f]{64}$");
			// No normalization: one character of difference is a different hash.
			ProtocolContentHash.Compute(text + " ").Should().NotBe(hash);
		}

		#endregion Content hash

		#region Settings 134

		[Test]
		public void KeepDaysFor_returns_a_kinds_retention_or_null_to_keep()
		{
			var settings = new ProtocolLibrarySettings
			{
				Retention = new List<ProtocolKindRetention>
				{
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Sog, KeepDays = 365 },
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Policy, KeepDays = 0 }
				}
			};

			settings.KeepDaysFor((int)ProtocolKinds.Sog).Should().Be(365);
			settings.KeepDaysFor((int)ProtocolKinds.Policy).Should().BeNull();
			settings.KeepDaysFor((int)ProtocolKinds.Dispatch).Should().BeNull();
			new ProtocolLibrarySettings { Retention = null }.KeepDaysFor((int)ProtocolKinds.Sog).Should().BeNull();
		}

		[Test]
		public void Normalize_clamps_the_interval_and_keeps_one_valid_retention_per_known_kind()
		{
			var settings = new ProtocolLibrarySettings
			{
				DefaultReviewIntervalDays = 5000,
				Retention = new List<ProtocolKindRetention>
				{
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Policy, KeepDays = 30 },
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Policy, KeepDays = 90 },
					new ProtocolKindRetention { Kind = 99, KeepDays = 10 },
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Sog, KeepDays = 0 },
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Reference, KeepDays = 99999 },
					null
				}
			}.Normalize();

			settings.DefaultReviewIntervalDays.Should().Be(ProtocolLibrarySettings.MaxReviewIntervalDays);
			settings.Retention.Select(r => (r.Kind, r.KeepDays)).Should().Equal(((int)ProtocolKinds.Policy, 90), ((int)ProtocolKinds.Reference, 36500));
			new ProtocolLibrarySettings { DefaultReviewIntervalDays = -4 }.Normalize().DefaultReviewIntervalDays.Should().Be(0);
		}

		[Test]
		public void Settings_survive_the_protobuf_round_trip_they_are_stored_with()
		{
			var settings = new ProtocolLibrarySettings
			{
				RequirePasskeyForApproval = true,
				DefaultReviewIntervalDays = 365,
				LegalHold = true,
				Retention = new List<ProtocolKindRetention> { new ProtocolKindRetention { Kind = (int)ProtocolKinds.Sog, KeepDays = 730 } }
			};

			var restored = ObjectSerialization.Deserialize<ProtocolLibrarySettings>(ObjectSerialization.Serialize(settings));

			restored.RequirePasskeyForApproval.Should().BeTrue();
			restored.DefaultReviewIntervalDays.Should().Be(365);
			restored.LegalHold.Should().BeTrue();
			restored.KeepDaysFor((int)ProtocolKinds.Sog).Should().Be(730);
		}

		#endregion Settings 134

		#region Approval rights

		[Test]
		public void Reader_rights_need_a_current_membership()
		{
			var approver = new ProtocolReader { DepartmentId = 10, UserId = "u1", IsCurrentMember = true, CanApprove = true };
			approver.IsManager.Should().BeTrue();
			approver.MayApprove.Should().BeTrue();
			approver.MayAuthor.Should().BeFalse();

			var admin = new ProtocolReader { DepartmentId = 10, UserId = "u2", IsCurrentMember = true, IsDepartmentAdmin = true };
			admin.MayAuthor.Should().BeTrue();
			admin.MayApprove.Should().BeTrue();

			var former = new ProtocolReader { DepartmentId = 10, UserId = "u3", IsCurrentMember = false, CanAuthor = true, CanApprove = true, IsDepartmentAdmin = true };
			former.IsManager.Should().BeFalse();
			former.MayAuthor.Should().BeFalse();
			former.MayApprove.Should().BeFalse();

			ProtocolReader.Nobody(10).IsManager.Should().BeFalse();
		}

		[Test]
		public void CanDecide_respects_named_approvers_roles_and_one_decision_per_person()
		{
			var reader = new ProtocolReader { DepartmentId = 10, UserId = "approver-1", IsCurrentMember = true, CanApprove = true };
			var open = new ProtocolVersionApproval { ProtocolVersionApprovalId = 2, StepOrder = 2, Decision = (int)ProtocolApprovalDecisions.Pending };

			ProtocolAccessRules.CanDecide(reader, open, null, new[] { open }).Should().BeTrue();

			var named = new ProtocolVersionApproval { Decision = (int)ProtocolApprovalDecisions.Pending, ApproverUserId = "APPROVER-1" };
			ProtocolAccessRules.CanDecide(reader, named, null, null).Should().BeTrue();
			ProtocolAccessRules.CanDecide(reader, new ProtocolVersionApproval { ApproverUserId = "approver-9" }, null, null).Should().BeFalse();

			var role = new ProtocolVersionApproval { Decision = (int)ProtocolApprovalDecisions.Pending, ApproverRoleId = 7 };
			ProtocolAccessRules.CanDecide(reader, role, new[] { 3 }, null).Should().BeFalse();
			ProtocolAccessRules.CanDecide(reader, role, new[] { 3, 7 }, null).Should().BeTrue();

			// Whoever approved an earlier step of the same round does not approve again.
			var earlier = new ProtocolVersionApproval { ProtocolVersionApprovalId = 1, StepOrder = 1, Decision = (int)ProtocolApprovalDecisions.Approved, DecidedByUserId = "Approver-1" };
			ProtocolAccessRules.CanDecide(reader, open, null, new[] { earlier, open }).Should().BeFalse();

			ProtocolAccessRules.CanDecide(reader, new ProtocolVersionApproval { Decision = (int)ProtocolApprovalDecisions.Approved }, null, null).Should().BeFalse();
			ProtocolAccessRules.CanDecide(new ProtocolReader { UserId = "author", IsCurrentMember = true, CanAuthor = true }, open, null, null).Should().BeFalse();
		}

		#endregion Approval rights
	}
}
