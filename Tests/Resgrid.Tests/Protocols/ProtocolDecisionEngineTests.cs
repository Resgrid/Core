using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P4 decision graph and case flow: validation (one root, every path to an outcome, no cycles), the case flow state machine,
	/// the override that needs a reason, the rendering that puts the graph under the content hash, the licensing guard and the
	/// ProtocolOutcome run card condition.
	/// </summary>
	[TestFixture]
	public class ProtocolDecisionEngineTests
	{
		/// <summary>
		/// entry: "Is the patient awake?" yes → key "Breathing normally?" (yes → outcome A, no → instruction → outcome E);
		/// no → number "Age" (&lt; 1 → outcome P, 1+ → outcome E).
		/// </summary>
		public static ProtocolDecisionGraph Sample() => new ProtocolDecisionGraph
		{
			Nodes = new List<ProtocolDecisionNode>
			{
				new ProtocolDecisionNode { Key = "awake", IsRoot = true, Kind = 1, Stage = 0, Label = "Awake", Text = "Is the patient awake?", QuestionType = 1,
					Answers = new List<ProtocolDecisionAnswer> { new ProtocolDecisionAnswer { Key = "yes", Next = "breathing" }, new ProtocolDecisionAnswer { Key = "no", Next = "age" } } },
				new ProtocolDecisionNode { Key = "breathing", Kind = 1, Stage = 1, Text = "Breathing normally?", QuestionType = 1,
					Answers = new List<ProtocolDecisionAnswer> { new ProtocolDecisionAnswer { Key = "yes", Next = "alpha" }, new ProtocolDecisionAnswer { Key = "no", Next = "airway" } } },
				new ProtocolDecisionNode { Key = "age", Kind = 1, Stage = 1, Text = "How old?", QuestionType = 3,
					Answers = new List<ProtocolDecisionAnswer> { new ProtocolDecisionAnswer { Key = "infant", Label = "Under 1", Max = 0.99m, Next = "peds" }, new ProtocolDecisionAnswer { Key = "older", Label = "1 or older", Min = 1, Next = "echo" } } },
				new ProtocolDecisionNode { Key = "airway", Kind = 2, Text = "Tilt the head back and look for breathing.", Next = "echo", Speak = true, SendToCaller = true },
				new ProtocolDecisionNode { Key = "alpha", Kind = 3, Label = "Low acuity", Determinant = "MED-A1", PriorityId = 2, PriorityName = "Low", PostDispatch = "Stay on the line." },
				new ProtocolDecisionNode { Key = "echo", Kind = 3, Label = "Not breathing", Determinant = "MED-E1", PriorityId = 7, PriorityName = "Emergency", ResponseHint = "ALS + engine", PostDispatch = "Start compressions.", SendToCaller = true },
				new ProtocolDecisionNode { Key = "peds", Kind = 3, Determinant = "MED-P1", PriorityId = 7, PriorityName = "Emergency" }
			}
		};

		private static Dictionary<string, string> A(params (string Key, string Value)[] answers) => answers.ToDictionary(a => a.Key, a => a.Value);

		#region Validation

		[Test]
		public void A_well_formed_graph_has_no_problems()
		{
			ProtocolDecisionGraphs.Validate(Sample()).Should().BeEmpty();
			ProtocolDecisionGraphs.Validate(null).Should().BeEmpty("a version without a graph has nothing to validate");
		}

		[Test]
		public void Exactly_one_root_is_required()
		{
			var none = Sample();
			none.Nodes[0].IsRoot = false;
			ProtocolDecisionGraphs.Validate(none).Select(p => p.Code).Should().Contain(ProtocolGraphProblems.RootCount);

			var two = Sample();
			two.Nodes[1].IsRoot = true;
			ProtocolDecisionGraphs.Validate(two).Select(p => p.Code).Should().Contain(ProtocolGraphProblems.RootCount);
		}

		[Test]
		public void Every_path_must_reach_an_outcome()
		{
			var deadEnd = Sample();
			deadEnd.Nodes.Single(n => n.Key == "airway").Next = null;
			ProtocolDecisionGraphs.Validate(deadEnd).Should().Contain(p => p.Code == ProtocolGraphProblems.NextMissing && p.NodeKey == "airway");

			var dangling = Sample();
			dangling.Nodes.Single(n => n.Key == "breathing").Answers[0].Next = "nowhere";
			ProtocolDecisionGraphs.Validate(dangling).Should().Contain(p => p.Code == ProtocolGraphProblems.NextMissing && p.NodeKey == "breathing");

			var noDeterminant = Sample();
			noDeterminant.Nodes.Single(n => n.Key == "peds").Determinant = " ";
			ProtocolDecisionGraphs.Validate(noDeterminant).Should().Contain(p => p.Code == ProtocolGraphProblems.OutcomeDeterminant);

			var branchingOutcome = Sample();
			branchingOutcome.Nodes.Single(n => n.Key == "alpha").Next = "echo";
			ProtocolDecisionGraphs.Validate(branchingOutcome).Should().Contain(p => p.Code == ProtocolGraphProblems.OutcomeBranches);
		}

		[Test]
		public void Cycles_are_refused()
		{
			var loop = Sample();
			loop.Nodes.Single(n => n.Key == "airway").Next = "awake";
			ProtocolDecisionGraphs.Validate(loop).Select(p => p.Code).Should().Contain(ProtocolGraphProblems.Cycle);
		}

		[Test]
		public void Shared_nodes_are_allowed_but_unreachable_ones_are_not()
		{
			// "echo" is reached from both "age" and "airway" (a shared outcome, like the run card graph's OR).
			ProtocolDecisionGraphs.Validate(Sample()).Should().BeEmpty();

			var orphan = Sample();
			orphan.Nodes.Add(new ProtocolDecisionNode { Key = "orphan", Kind = 3, Determinant = "X-1" });
			ProtocolDecisionGraphs.Validate(orphan).Should().ContainSingle(p => p.Code == ProtocolGraphProblems.Unreachable && p.NodeKey == "orphan");
		}

		[Test]
		public void Keys_determinants_and_number_ranges_are_checked()
		{
			var keys = Sample();
			keys.Nodes[1].Key = keys.Nodes[2].Key;
			ProtocolDecisionGraphs.Validate(keys).Select(p => p.Code).Should().Contain(ProtocolGraphProblems.DuplicateKey);

			var determinant = Sample();
			determinant.Nodes.Single(n => n.Key == "peds").Determinant = "<script>";
			ProtocolDecisionGraphs.Validate(determinant).Select(p => p.Code).Should().Contain(ProtocolGraphProblems.DeterminantFormat);

			var overlap = Sample();
			overlap.Nodes.Single(n => n.Key == "age").Answers[1].Min = 0.5m;
			ProtocolDecisionGraphs.Validate(overlap).Select(p => p.Code).Should().Contain(ProtocolGraphProblems.NumberRange);
		}

		[Test]
		public void Normalize_gives_yes_no_questions_exactly_yes_and_no_and_clears_foreign_fields()
		{
			var graph = new ProtocolDecisionGraph
			{
				Nodes = new List<ProtocolDecisionNode>
				{
					new ProtocolDecisionNode { Key = " Q1 ", IsRoot = true, Kind = 1, QuestionType = 1, Text = " Awake? ", Determinant = "ignored",
						Answers = new List<ProtocolDecisionAnswer> { new ProtocolDecisionAnswer { Key = "Y", Next = "O1" }, new ProtocolDecisionAnswer { Key = "N", Next = "o1" }, new ProtocolDecisionAnswer { Key = "x" } } },
					new ProtocolDecisionNode { Key = "o1", Kind = 3, Determinant = "  a   1 ", Next = "q1", PostDispatch = null, SendToCaller = true },
					new ProtocolDecisionNode { Key = "bad", Kind = 9 }
				}
			};

			var clean = ProtocolDecisionGraphs.Normalize(graph);
			clean.Nodes.Should().HaveCount(2, "unknown kinds are dropped");
			clean.Nodes[0].Key.Should().Be("q1");
			clean.Nodes[0].Text.Should().Be("Awake?");
			clean.Nodes[0].Determinant.Should().BeNull();
			clean.Nodes[0].Answers.Select(a => a.Key).Should().Equal("yes", "no");
			clean.Nodes[0].Answers.Select(a => a.Next).Should().Equal("o1", "o1");
			clean.Nodes[1].Determinant.Should().Be("a 1");
			clean.Nodes[1].Next.Should().BeNull("an outcome never branches");
			clean.Nodes[1].SendToCaller.Should().BeFalse("an outcome with nothing to say has nothing to text");
			ProtocolDecisionGraphs.Validate(clean).Should().BeEmpty();
			ProtocolDecisionGraphs.Normalize(new ProtocolDecisionGraph()).Should().BeNull();
		}

		#endregion

		#region Case flow state machine

		[Test]
		public void The_walk_stops_at_the_first_unanswered_question()
		{
			var start = ProtocolCaseFlow.Run(Sample(), A());
			start.Status.Should().Be(ProtocolCaseStatuses.InProgress);
			start.CurrentNodeKey.Should().Be("awake");

			var key = ProtocolCaseFlow.Run(Sample(), A(("awake", "yes")));
			key.Status.Should().Be(ProtocolCaseStatuses.InProgress);
			key.CurrentNodeKey.Should().Be("breathing");
			key.Steps.Should().ContainSingle(s => s.NodeKey == "awake" && s.AnswerKey == "yes");
		}

		[Test]
		public void Entry_then_key_questions_then_instructions_reach_the_outcome()
		{
			var result = ProtocolCaseFlow.Run(Sample(), A(("awake", "YES"), ("breathing", "no")));

			result.Status.Should().Be(ProtocolCaseStatuses.Complete);
			result.Outcome.Determinant.Should().Be("MED-E1");
			result.InstructionKeys.Should().Equal("airway");
			result.Steps.Select(s => s.NodeKey).Should().Equal("awake", "breathing", "airway", "echo");
			result.Steps[1].Stage.Should().Be((int)ProtocolCaseStages.Key);
		}

		[Test]
		public void Number_answers_pick_their_range_and_text_answers_are_kept_as_said()
		{
			ProtocolCaseFlow.Run(Sample(), A(("awake", "no"), ("age", "0.5"))).Outcome.Determinant.Should().Be("MED-P1");
			ProtocolCaseFlow.Run(Sample(), A(("awake", "no"), ("age", "42"))).Outcome.Determinant.Should().Be("MED-E1");

			var free = new ProtocolDecisionGraph
			{
				Nodes = new List<ProtocolDecisionNode>
				{
					new ProtocolDecisionNode { Key = "what", IsRoot = true, Kind = 1, QuestionType = 4, Text = "Tell me what happened.", Next = "out" },
					new ProtocolDecisionNode { Key = "out", Kind = 3, Determinant = "GEN-1" }
				}
			};
			var walked = ProtocolCaseFlow.Run(free, A(("what", "  He fell off a ladder  ")));
			walked.Status.Should().Be(ProtocolCaseStatuses.Complete);
			walked.Steps[0].Answer.Should().Be("He fell off a ladder");
		}

		[Test]
		public void An_answer_that_is_not_the_questions_makes_the_case_invalid()
		{
			var result = ProtocolCaseFlow.Run(Sample(), A(("awake", "maybe")));
			result.Status.Should().Be(ProtocolCaseStatuses.Invalid);
			result.Error.Should().Be(ProtocolCaseErrors.AnswerInvalid);
			result.ErrorNodeKey.Should().Be("awake");

			ProtocolCaseFlow.Run(Sample(), A(("awake", "no"), ("age", "abc"))).Error.Should().Be(ProtocolCaseErrors.AnswerInvalid);
		}

		[Test]
		public void Answers_off_the_path_are_ignored()
		{
			// The dispatcher answered "age" and then changed "awake" to yes: the age answer is no longer on the path.
			var result = ProtocolCaseFlow.Run(Sample(), A(("awake", "yes"), ("age", "0.5"), ("breathing", "yes")));
			result.Outcome.Determinant.Should().Be("MED-A1");
			result.Steps.Select(s => s.NodeKey).Should().NotContain("age");
		}

		[Test]
		public void A_graph_without_one_root_cannot_be_walked()
		{
			var graph = Sample();
			graph.Nodes[0].IsRoot = false;
			ProtocolCaseFlow.Run(graph, A()).Error.Should().Be(ProtocolCaseErrors.NoCaseFlow);
			ProtocolCaseFlow.Run(null, A()).Error.Should().Be(ProtocolCaseErrors.NoCaseFlow);
		}

		#endregion

		#region Override

		private static CallProtocolCase Completed(string overrideDeterminant = null, string reason = null)
		{
			var walked = ProtocolCaseFlow.Run(Sample(), A(("awake", "yes"), ("breathing", "yes")));
			var (record, error) = ProtocolCaseFlow.Record(walked, new ProtocolCaseInput { OverrideDeterminant = overrideDeterminant, OverrideReason = reason, AppliedPriority = 2 },
				11, CallProtocolSources.Manual, "dispatcher", new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));
			error.Should().Be(ProtocolCaseErrors.None);
			return record;
		}

		[Test]
		public void The_record_carries_the_computed_determinant_priority_and_instructions()
		{
			var record = Completed();
			record.ProtocolVersionId.Should().Be(11);
			record.ComputedDeterminant.Should().Be("MED-A1");
			record.Determinant.Should().Be("MED-A1");
			record.Overridden.Should().BeFalse();
			record.SuggestedPriority.Should().Be(2);
			record.AppliedPriority.Should().Be(2);
			record.PostDispatch.Should().Be("Stay on the line.");
			record.CompletedByUserId.Should().Be("dispatcher");
			CallProtocolCase.Parse(record.ToJson()).Determinant.Should().Be("MED-A1");
		}

		[Test]
		public void An_override_needs_a_reason()
		{
			var walked = ProtocolCaseFlow.Run(Sample(), A(("awake", "yes"), ("breathing", "yes")));
			ProtocolCaseFlow.Record(walked, new ProtocolCaseInput { OverrideDeterminant = "MED-E1" }, 11, CallProtocolSources.Manual, "d", DateTime.UtcNow)
				.Error.Should().Be(ProtocolCaseErrors.OverrideReasonRequired);
			ProtocolCaseFlow.Record(walked, new ProtocolCaseInput { OverrideDeterminant = "MED-E1", OverrideReason = "   " }, 11, CallProtocolSources.Manual, "d", DateTime.UtcNow)
				.Error.Should().Be(ProtocolCaseErrors.OverrideReasonRequired);

			var record = Completed("MED-E1", "Caller now reports he stopped breathing");
			record.Determinant.Should().Be("MED-E1");
			record.ComputedDeterminant.Should().Be("MED-A1");
			record.Overridden.Should().BeTrue();
			record.OverrideReason.Should().Be("Caller now reports he stopped breathing");
			record.OverriddenByUserId.Should().Be("dispatcher");
		}

		[Test]
		public void The_computed_determinant_in_any_case_is_not_an_override_and_an_invalid_one_is_refused()
		{
			Completed("med-a1").Overridden.Should().BeFalse("the same determinant needs no reason");

			var walked = ProtocolCaseFlow.Run(Sample(), A(("awake", "yes"), ("breathing", "yes")));
			ProtocolCaseFlow.Record(walked, new ProtocolCaseInput { OverrideDeterminant = "<b>x</b>", OverrideReason = "why" }, 11, CallProtocolSources.Manual, "d", DateTime.UtcNow)
				.Error.Should().Be(ProtocolCaseErrors.DeterminantInvalid);
		}

		[Test]
		public void A_determinant_set_on_an_unfinished_case_is_an_override()
		{
			var walked = ProtocolCaseFlow.Run(Sample(), A(("awake", "yes")));
			ProtocolCaseFlow.Record(walked, new ProtocolCaseInput { OverrideDeterminant = "MED-E1" }, 11, CallProtocolSources.Manual, "d", DateTime.UtcNow)
				.Error.Should().Be(ProtocolCaseErrors.OverrideReasonRequired);

			var (record, error) = ProtocolCaseFlow.Record(walked, new ProtocolCaseInput { OverrideDeterminant = "MED-E1", OverrideReason = "Hung up" }, 11, CallProtocolSources.Manual, "d", DateTime.UtcNow);
			error.Should().Be(ProtocolCaseErrors.None);
			record.Status.Should().Be((int)ProtocolCaseStatuses.InProgress);
			record.ComputedDeterminant.Should().BeNull();
			record.Determinant.Should().Be("MED-E1");
			record.Overridden.Should().BeTrue();
		}

		[Test]
		public void Restoring_the_computed_determinant_clears_the_override()
		{
			var record = Completed("MED-E1", "reason");
			ProtocolCaseFlow.ApplyOverride(record, "MED-A1", null, "other", DateTime.UtcNow).Should().Be(ProtocolCaseErrors.None);
			record.Overridden.Should().BeFalse();
			record.OverrideReason.Should().BeNull();
			record.Determinant.Should().Be("MED-A1");
		}

		#endregion

		#region Rendering and the hash

		[Test]
		public void The_graph_is_rendered_into_the_version_text_so_the_hash_covers_it()
		{
			var document = ProtocolContentDocument.FromHtml("<p>Cardiac arrest</p>");
			var without = document.Render();

			document.Graph = Sample();
			var with = document.Render();
			with.Should().StartWith(without);
			with.Should().Contain("MED-E1").And.Contain("Is the patient awake?").And.Contain("Stay on the line.").And.Contain("Emergency");
			ProtocolContentHash.Compute(with).Should().NotBe(ProtocolContentHash.Compute(without));

			var changed = Sample();
			changed.Nodes.Single(n => n.Key == "echo").Determinant = "MED-E2";
			document.Graph = changed;
			ProtocolContentHash.Compute(document.Render()).Should().NotBe(ProtocolContentHash.Compute(with), "changing the graph changes the version's hash");

			var rewired = Sample();
			rewired.Nodes.Single(n => n.Key == "breathing").Answers[0].Next = "echo";
			document.Graph = rewired;
			ProtocolContentHash.Compute(document.Render()).Should().NotBe(ProtocolContentHash.Compute(with), "a changed pointer changes the text");
		}

		[Test]
		public void A_document_without_a_graph_renders_exactly_as_before()
		{
			var html = "<h2>Scene safety</h2><p>Look first.</p>";
			var document = ProtocolContentDocument.Parse(ProtocolContentDocument.FromHtml(html).ToJson(), null);
			document.Graph.Should().BeNull();
			document.ToJson().Should().NotContain("graph");
			document.Render().Should().Be(StringHelpersRender(html));
		}

		private static string StringHelpersRender(string html) => ProtocolContentDocument.FromHtml(html).Render();

		[Test]
		public void Authored_text_in_the_graph_is_encoded_and_block_documents_keep_their_anchors()
		{
			var graph = Sample();
			graph.Nodes[0].Text = "<script>alert(1)</script> awake?";
			var rendered = ProtocolDecisionGraphs.RenderHtml(graph);
			rendered.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
			rendered.Should().Contain("id=\"b-g-awake\"");

			var blocks = ProtocolBlocks.Normalize(new ProtocolContentDocument { Blocks = new List<ProtocolContentBlock> { new ProtocolContentBlock { Type = "paragraph", Key = "p1", Html = "<p>Text</p>" } } });
			blocks.Graph = Sample();
			var html = blocks.Render();
			html.Should().Contain("rgp-graph").And.Contain("id=\"b-g-echo\"").And.Contain("MED-E1");
			Resgrid.Framework.StructuredHtml.Sanitize(html).Should().Be(html, "the structured allow-list keeps the case flow as rendered");
		}

		[Test]
		public void The_graph_round_trips_in_content_json_and_convert_to_blocks_keeps_it_a_graph()
		{
			var document = ProtocolContentDocument.FromHtml("<p>Body</p>");
			document.Graph = Sample();
			var parsed = ProtocolContentDocument.Parse(document.ToJson(), null);
			parsed.HasGraph.Should().BeTrue();
			parsed.Graph.Nodes.Should().HaveCount(7);
			parsed.Graph.Find("echo").SendToCaller.Should().BeTrue();

			var converted = parsed.ConvertToBlocks();
			converted.Graph.Should().BeSameAs(parsed.Graph);
			converted.Blocks.SelectMany(b => new[] { b.Html, b.Text }).Should().NotContain(t => t != null && t.Contains("MED-E1"), "the case flow never becomes text blocks");
		}

		[Test]
		public void Stripping_a_rendered_case_flow_gives_back_the_editable_text()
		{
			var document = ProtocolContentDocument.FromHtml("<p>Body</p>");
			var body = document.Render();
			document.Graph = Sample();
			ProtocolDecisionGraphs.StripRendered(document.Render()).Should().Be(body);
			ProtocolDecisionGraphs.StripRendered(body).Should().Be(body);
		}

		#endregion

		#region Caller texts and audio text

		[Test]
		public void Only_instructions_and_outcomes_marked_for_the_caller_can_be_texted()
		{
			ProtocolCaseService.CallerText(Sample(), "airway").Should().Be("Tilt the head back and look for breathing.");
			ProtocolCaseService.CallerText(Sample(), "echo").Should().Be("Start compressions.");
			ProtocolCaseService.CallerText(Sample(), "alpha").Should().BeNull("not marked for the caller");
			ProtocolCaseService.CallerText(Sample(), "awake").Should().BeNull("a question is never texted");
			ProtocolCaseService.CallerText(null, "airway").Should().BeNull();
		}

		[Test]
		public void An_instruction_is_spoken_as_plain_text_with_a_full_stop()
		{
			DispatchVoicePromptBuilder.BuildInstructionPrompt("Tilt the head <b>back</b>\n and listen").Should().Be("Tilt the head back and listen.");
			DispatchVoicePromptBuilder.BuildInstructionPrompt("  ").Should().BeNull();
		}

		#endregion

		#region Licensing guard

		[Test]
		public void Licensed_content_is_never_shared_published_or_used_by_ai()
		{
			var open = new DispatchProtocol { Status = (int)ProtocolStatuses.Published, CurrentVersionId = 3 };
			ProtocolLicensing.CanShare(open).Should().BeTrue();
			ProtocolLicensing.CanPublishPublicly(open).Should().BeTrue();
			ProtocolLicensing.CanUseForAi(open).Should().BeTrue();

			var licensed = new DispatchProtocol { Status = (int)ProtocolStatuses.Published, CurrentVersionId = 3, IsLicensedContent = true };
			ProtocolLicensing.CanShare(licensed).Should().BeFalse();
			ProtocolLicensing.CanPublishPublicly(licensed).Should().BeFalse();
			ProtocolLicensing.CanUseForAi(licensed).Should().BeFalse();

			ProtocolLicensing.CanUseForAi(new DispatchProtocol { ExcludeFromAi = true }).Should().BeFalse();
			ProtocolLicensing.CanPublishPublicly(new DispatchProtocol { Status = (int)ProtocolStatuses.Draft }).Should().BeFalse("drafts are never public");
			ProtocolLicensing.CanShare(null).Should().BeFalse();
		}

		#endregion

		#region Run card ProtocolOutcome condition

		[Test]
		public void A_protocol_outcome_condition_compares_the_determinants_on_the_call()
		{
			var determinants = new List<CallProtocolDeterminant>
			{
				new CallProtocolDeterminant { ProtocolId = 4, Determinant = "09-E-01" },
				new CallProtocolDeterminant { ProtocolId = 5, Determinant = "26-A-01" }
			};

			new RunCardProtocolOutcomeParameters { Match = (int)RunCardProtocolOutcomeMatches.StartsWith, Value = "09-e" }.Matches(determinants).Should().BeTrue();
			new RunCardProtocolOutcomeParameters { Match = (int)RunCardProtocolOutcomeMatches.Equals, Value = "09-E" }.Matches(determinants).Should().BeFalse();
			new RunCardProtocolOutcomeParameters { Match = (int)RunCardProtocolOutcomeMatches.Equals, Value = "26-a-01" }.Matches(determinants).Should().BeTrue();
			new RunCardProtocolOutcomeParameters { Match = (int)RunCardProtocolOutcomeMatches.StartsWith, Value = "09", ProtocolId = 5 }.Matches(determinants).Should().BeFalse("only protocol 5 counts");
			new RunCardProtocolOutcomeParameters { Match = (int)RunCardProtocolOutcomeMatches.StartsWith, Value = "09" }.Matches(new List<CallProtocolDeterminant>()).Should().BeFalse();
		}

		[Test]
		public void Determinants_on_the_call_come_from_its_protocols_that_are_still_on_it()
		{
			var determinants = CallProtocolDeterminant.From(new List<CallProtocol>
			{
				new CallProtocol { DispatchProtocolId = 1, Determinant = "A-1" },
				new CallProtocol { DispatchProtocolId = 2, Determinant = "B-1", RemovedOn = DateTime.UtcNow },
				new CallProtocol { DispatchProtocolId = 3 }
			});
			determinants.Should().ContainSingle(d => d.ProtocolId == 1 && d.Determinant == "A-1");
		}

		[Test]
		public void A_run_card_graph_validates_a_protocol_outcome_condition()
		{
			RunCardDecisionNode Outcome(string key) => new RunCardDecisionNode { NodeKey = key, NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = key, AdviceText = "advice" };

			var valid = new List<RunCardDecisionNode>
			{
				new RunCardDecisionNode { NodeKey = "c", IsRoot = true, NodeKind = (int)RunCardDecisionNodeKinds.Condition, ConditionType = (int)RunCardConditionTypes.ProtocolOutcome, Label = "Echo?",
					ParametersJson = new RunCardProtocolOutcomeParameters { Match = (int)RunCardProtocolOutcomeMatches.StartsWith, Value = "09-E" }.ToJson(), YesNodeKey = "y", NoNodeKey = "n" },
				Outcome("y"), Outcome("n")
			};
			RunCardDecisionGraph.Validate(valid).Should().BeEmpty("no time, subject or reference is needed for a determinant");

			valid[0].ParametersJson = null;
			RunCardDecisionGraph.Validate(valid).Should().ContainSingle(e => e.Contains("determinant"));
		}

		#endregion
	}
}
