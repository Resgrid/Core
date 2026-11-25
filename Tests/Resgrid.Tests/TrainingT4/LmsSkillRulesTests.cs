using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Tests.TrainingT4
{
	/// <summary>
	/// Training T4 part 2 (round 5C): the pure skill sheet engine (critical auto-fail, scoring, ratings, computed and timed steps, Pass All,
	/// submission checks), the template library guard (structure only, every starter publishable), attempts, task progress, digest timing.
	/// </summary>
	[TestFixture]
	public class LmsSkillRulesTests
	{
		private static LmsSkillStep Pf(string key, bool critical = false, decimal weight = 1m) =>
			new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.PassFail, Label = "Step " + key, Critical = critical, Required = true, Weight = weight };

		private static LmsSkillAnswer A(string value) => new LmsSkillAnswer { Status = (int)LmsSkillAnswerStatuses.Answered, Value = value };

		private static LmsSkillAnswers Answers(params (string Key, string Value)[] values)
		{
			var answers = new LmsSkillAnswers();
			foreach (var (key, value) in values)
				answers.Steps[key] = A(value);
			return answers;
		}

		private static LmsSkillGrading Grading(decimal pass = 80m, int limit = 0) => new LmsSkillGrading { PassThreshold = pass, TimeLimitSeconds = limit };

		#region Engine

		[Test]
		public void A_failed_critical_step_fails_the_evaluation_whatever_the_score()
		{
			var content = new LmsSkillSheetContent { Steps = Enumerable.Range(1, 9).Select(i => Pf("s" + i)).Append(Pf("seal", critical: true)).ToList() };
			var answers = Answers(Enumerable.Range(1, 9).Select(i => ("s" + i, "pass")).Append(("seal", "fail")).ToArray());

			var outcome = LmsSkillSheetEngine.Evaluate(content, answers, Grading(80m), 30, null, true);

			outcome.Complete.Should().BeTrue();
			outcome.Score.Should().Be(90m);
			outcome.CriticalFailed.Should().BeTrue();
			outcome.Passed.Should().BeFalse("a critical step auto-fails the evaluation");
			outcome.FailedStepKeys.Should().Equal("seal");
		}

		[Test]
		public void A_non_critical_failure_passes_when_the_score_reaches_the_threshold()
		{
			var content = new LmsSkillSheetContent { Steps = Enumerable.Range(1, 5).Select(i => Pf("s" + i)).ToList() };
			LmsSkillSheetEngine.Evaluate(content, Answers(("s1", "pass"), ("s2", "pass"), ("s3", "pass"), ("s4", "pass"), ("s5", "fail")), Grading(80m), 0, null, true)
				.Passed.Should().BeTrue();
			LmsSkillSheetEngine.Evaluate(content, Answers(("s1", "pass"), ("s2", "pass"), ("s3", "pass"), ("s4", "fail"), ("s5", "fail")), Grading(80m), 0, null, true)
				.Passed.Should().BeFalse();
		}

		[Test]
		public void Ratings_choices_durations_and_computed_values_score_and_pass_by_their_rules()
		{
			var content = new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep>
				{
					new LmsSkillStep { Key = "r1", Type = (int)LmsSkillStepTypes.RatingScale, Label = "R1", RatingMin = 1, RatingMax = 7, PassingRating = 4, Required = true },
					new LmsSkillStep { Key = "r2", Type = (int)LmsSkillStepTypes.RatingScale, Label = "R2", RatingMin = 1, RatingMax = 7, Required = true },
					new LmsSkillStep { Key = "avg", Type = (int)LmsSkillStepTypes.Computed, Label = "Average", ComputeOp = (int)LmsSkillComputeOps.Average, ComputeKeys = new List<string> { "r1", "r2" }, Min = 4 },
					new LmsSkillStep { Key = "time", Type = (int)LmsSkillStepTypes.Duration, Label = "Time", Max = 60, Critical = true, Required = true },
					new LmsSkillStep
					{
						Key = "c", Type = (int)LmsSkillStepTypes.ChooseOne, Label = "Choice", Required = true,
						Options = new List<LmsSkillOption> { new LmsSkillOption { Key = "a", Label = "Best", Points = 2 }, new LmsSkillOption { Key = "b", Label = "Unsafe", Points = 0, Passing = false } }
					},
					new LmsSkillStep { Key = "notes", Type = (int)LmsSkillStepTypes.Text, Label = "Notes" }
				}
			};
			LmsSkillSheetEngine.Validate(content).Should().BeEmpty();

			var good = LmsSkillSheetEngine.Evaluate(content, Answers(("r1", "7"), ("r2", "4"), ("time", "45"), ("c", "a")), Grading(50m), 0, null, true);
			good.Complete.Should().BeTrue();
			good.Computed["avg"].Should().Be(5.5m);
			good.Passed.Should().BeTrue();

			var slow = LmsSkillSheetEngine.Evaluate(content, Answers(("r1", "7"), ("r2", "4"), ("time", "75"), ("c", "a")), Grading(50m), 0, null, true);
			slow.CriticalFailed.Should().BeTrue("the timed station is critical and 75 s is over 60 s");
			slow.Passed.Should().BeFalse();

			var unsafeChoice = LmsSkillSheetEngine.Evaluate(content, Answers(("r1", "2"), ("r2", "3"), ("time", "30"), ("c", "b")), Grading(0m), 0, null, true);
			unsafeChoice.FailedStepKeys.Should().Contain(new[] { "r1", "avg", "c" });
		}

		[Test]
		public void The_time_limit_fails_the_evaluation()
		{
			var content = new LmsSkillSheetContent { Steps = new List<LmsSkillStep> { Pf("a") } };
			LmsSkillSheetEngine.Evaluate(content, Answers(("a", "pass")), Grading(80m, 60), 61, null, true).Should()
				.Match<LmsSkillOutcome>(o => o.TimeExceeded && !o.Passed);
			LmsSkillSheetEngine.Evaluate(content, Answers(("a", "pass")), Grading(80m, 60), 60, null, true).Passed.Should().BeTrue();
		}

		[Test]
		public void Submission_needs_every_required_step_a_note_on_a_failure_that_asks_and_evidence_where_required()
		{
			var content = new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep>
				{
					new LmsSkillStep { Key = "a", Type = (int)LmsSkillStepTypes.PassFail, Label = "A", Required = true, RequireNoteOnFail = true, RequireEvidenceOnFail = true },
					Pf("b"),
					new LmsSkillStep { Key = "na", Type = (int)LmsSkillStepTypes.PassFail, Label = "NA", AllowNotApplicable = true, Required = true }
				}
			};
			var answers = Answers(("a", "fail"));
			answers.Steps["na"] = new LmsSkillAnswer { Status = (int)LmsSkillAnswerStatuses.NotApplicable, NotApplicableReason = "No ladder on scene" };

			var outcome = LmsSkillSheetEngine.Evaluate(content, answers, Grading(), 0, null, true);
			outcome.Errors.Should().Contain(new[] { "lms_skill_step_required:b", "lms_skill_step_note_required:a", "lms_skill_step_evidence_required:a" });
			outcome.Passed.Should().BeFalse();

			answers.Steps["a"].Note = "Did not check the seal";
			answers.Steps["b"] = A("pass");
			var fixedUp = LmsSkillSheetEngine.Evaluate(content, answers, Grading(0m), 0, new HashSet<string> { "a" }, true);
			fixedUp.Complete.Should().BeTrue("N/A with a reason is allowed on that step");

			// Draft grading (not final) never asks for what is still unanswered.
			LmsSkillSheetEngine.Evaluate(content, new LmsSkillAnswers(), Grading(), 0, null, false).Errors.Should().BeEmpty();
		}

		[Test]
		public void Pass_all_passes_only_unanswered_pass_fail_steps()
		{
			var content = new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep> { Pf("a"), Pf("b"), new LmsSkillStep { Key = "t", Type = (int)LmsSkillStepTypes.Text, Label = "T" } }
			};
			var before = Answers(("a", "fail"));
			before.Steps["a"].Note = "kept";

			var after = LmsSkillSheetEngine.PassAll(content, before, DateTime.UtcNow);

			after.Get("a").Value.Should().Be("fail", "an answered step keeps the evaluator's grade");
			after.Get("a").Note.Should().Be("kept");
			after.Get("b").Value.Should().Be("pass");
			after.Get("t").Should().BeNull();
			before.Get("b").Should().BeNull("Pass All works on a copy");
		}

		[Test]
		public void Validation_refuses_bad_steps()
		{
			LmsSkillSheetEngine.Validate(new LmsSkillSheetContent()).Should().Contain("lms_skill_sheet_step_count");
			LmsSkillSheetEngine.Validate(new LmsSkillSheetContent { Steps = new List<LmsSkillStep> { Pf("a"), Pf("a") } }).Should().Contain("lms_skill_sheet_step_key");
			LmsSkillSheetEngine.Validate(new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep> { new LmsSkillStep { Key = "t", Type = (int)LmsSkillStepTypes.Text, Label = "T", Critical = true } }
			}).Should().Contain("lms_skill_sheet_step_critical:t");
			LmsSkillSheetEngine.Validate(new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep> { Pf("a"), new LmsSkillStep { Key = "c", Type = (int)LmsSkillStepTypes.Computed, Label = "C", ComputeKeys = new List<string> { "a" } } }
			}).Should().Contain("lms_skill_sheet_step_compute:c", "a computed step sums number, duration or rating steps only");
			LmsSkillSheetEngine.Validate(new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep> { new LmsSkillStep { Key = "h", Type = (int)LmsSkillStepTypes.Heading, Label = "H" } }
			}).Should().Contain("lms_skill_sheet_no_gradable_step");
		}

		[Test]
		public void The_answers_round_trip_in_a_stable_order()
		{
			var answers = Answers(("b", "pass"), ("a", "fail"));
			var json = answers.ToJson();
			json.IndexOf("\"a\"", StringComparison.Ordinal).Should().BeLessThan(json.IndexOf("\"b\"", StringComparison.Ordinal));
			LmsSkillAnswers.Parse(json).ToJson().Should().Be(json);
			LmsSkillAnswers.Parse("not json").Steps.Should().BeEmpty();
		}

		[Test]
		public void A_task_signoff_has_one_critical_pass_fail_step()
		{
			var content = LmsSkillSheetContent.TaskSignoff("FFT2 task 3");
			LmsSkillSheetEngine.Validate(content).Should().BeEmpty();
			LmsSkillSheetEngine.Evaluate(content, Answers(("task", "fail")), Grading(0m), 0, null, false).CriticalFailed.Should().BeTrue();
		}

		#endregion Engine

		#region Template library

		[Test]
		public void Every_skill_sheet_starter_is_publishable_and_carries_names_not_prose()
		{
			LmsSkillTemplateCatalog.Sheets.Should().NotBeEmpty();
			LmsSkillTemplateCatalog.Sheets.Select(t => t.Key).Should().OnlyHaveUniqueItems();
			foreach (var template in LmsSkillTemplateCatalog.Sheets)
			{
				LmsSkillSheetEngine.Validate(new LmsSkillSheetContent { Steps = template.Steps }).Should().BeEmpty(template.Key);
				foreach (var step in template.Steps)
					step.Label.Length.Should().BeLessThanOrEqualTo(LmsSkillTemplateCatalog.MaxLabelLength, template.Key + ":" + step.Key);
			}
			LmsSkillTemplateCatalog.Sheets.Should().Contain(t => t.Kind == LmsSkillSheetKinds.ObservationReport, "FTO/preceptor daily observation reports are starters");
			LmsSkillTemplateCatalog.FindSheet("SCBA-DONNING").Should().NotBeNull();
		}

		[Test]
		public void Every_task_book_starter_is_valid_and_wildland_books_carry_identifiers_only()
		{
			LmsSkillTemplateCatalog.TaskBooks.Select(t => t.Key).Should().OnlyHaveUniqueItems();
			foreach (var template in LmsSkillTemplateCatalog.TaskBooks)
			{
				LmsSkillRules.ValidateTasks(template.Tasks).Should().BeEmpty(template.Key);
				template.Tasks.Should().OnlyContain(t => t.Title.Length <= LmsSkillTemplateCatalog.MaxLabelLength, template.Key);
			}
			foreach (var wildland in LmsSkillTemplateCatalog.TaskBooks.Where(t => t.Family == LmsTaskBookFamilies.Wildland))
				wildland.Tasks.Should().OnlyContain(t => t.Title == wildland.PositionCode + " task " + t.Number, "NWCG task text is never shipped");
			LmsSkillTemplateCatalog.FindTaskBook("osha-forklift-operator").RecertifyMonths.Should().Be(36);
			LmsSkillTemplateCatalog.TaskBooks.Select(t => t.Family).Should().Contain(new[]
				{ LmsTaskBookFamilies.Officer, LmsTaskBookFamilies.DriverOperator, LmsTaskBookFamilies.OshaOperator, LmsTaskBookFamilies.NccpSkills, LmsTaskBookFamilies.FieldTraining });
		}

		#endregion Template library

		#region Rules

		[Test]
		public void Attempts_count_failures_since_the_last_pass_inside_the_window()
		{
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			var twoFailures = new[] { (now.AddDays(-3), false), (now.AddDays(-1), false) };
			LmsSkillRules.CanAttempt(twoFailures, 2, 30, now).Should().BeFalse();
			LmsSkillRules.CanAttempt(twoFailures, 3, 30, now).Should().BeTrue();
			LmsSkillRules.CanAttempt(twoFailures, 0, 30, now).Should().BeTrue("0 means no limit");
			LmsSkillRules.CanAttempt(new[] { (now.AddDays(-40), false), (now.AddDays(-1), false) }, 2, 30, now).Should().BeTrue("only the window counts");
			LmsSkillRules.CanAttempt(new[] { (now.AddDays(-5), false), (now.AddDays(-4), true), (now.AddDays(-1), false) }, 2, 0, now).Should().BeTrue("a pass resets the count");
		}

		[Test]
		public void A_task_completes_with_passed_completed_evaluations_on_distinct_dates_and_incident_evidence()
		{
			var task = new LmsTaskBookTask { Key = "t1", Title = "T1", RequiredEvaluations = 2, RequiresIncident = true };
			LmsSkillEvaluation E(int day, bool passed = true, int? call = 10, int status = (int)LmsSkillEvaluationStatuses.Completed) => new LmsSkillEvaluation
			{
				TaskKey = "t1", Status = status, Result = passed ? (int)LmsSkillEvaluationResults.Passed : (int)LmsSkillEvaluationResults.Failed,
				CallId = call, StartedOn = new DateTime(2026, 6, day, 9, 0, 0), SubmittedOn = new DateTime(2026, 6, day, 10, 0, 0)
			};

			LmsSkillRules.TaskProgress(task, new[] { E(1), E(1) }).Complete.Should().BeFalse("two evaluations on the same date count once");
			LmsSkillRules.TaskProgress(task, new[] { E(1), E(2, call: null) }).Complete.Should().BeFalse("an incident task needs a call or deployment");
			LmsSkillRules.TaskProgress(task, new[] { E(1), E(2, passed: false) }).Complete.Should().BeFalse();
			LmsSkillRules.TaskProgress(task, new[] { E(1), E(2, status: (int)LmsSkillEvaluationStatuses.AwaitingSignature) }).Complete.Should().BeFalse("unsigned evaluations do not count");
			var done = LmsSkillRules.TaskProgress(task, new[] { E(1), E(3) });
			done.Complete.Should().BeTrue();
			done.Counted.Should().Be(2);
		}

		[Test]
		public void Digest_notices_are_due_now_or_at_the_next_department_local_slot()
		{
			var now = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc); // a Wednesday
			Func<DateTime, DateTime> same = d => d;
			LmsSkillRules.DueOn(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Immediate }, now, same, same).Should().Be(now);
			LmsSkillRules.DueOn(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Daily, DigestHour = 18 }, now, same, same).Should().Be(new DateTime(2026, 11, 25, 18, 0, 0));
			LmsSkillRules.DueOn(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Daily, DigestHour = 7 }, now, same, same).Should().Be(new DateTime(2026, 11, 26, 7, 0, 0));
			LmsSkillRules.DueOn(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Weekly, DigestHour = 7, DigestWeekday = 1 }, now, same, same).Should().Be(new DateTime(2026, 11, 30, 7, 0, 0));
			// A department eight hours behind UTC: 07:00 local is 15:00 UTC.
			LmsSkillRules.DueOn(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Daily, DigestHour = 7 }, now, d => d.AddHours(-8), d => d.AddHours(8))
				.Should().Be(new DateTime(2026, 11, 26, 15, 0, 0));
		}

		[Test]
		public void Settings_survive_a_protobuf_round_trip_with_their_true_defaults_off()
		{
			var stored = Resgrid.Framework.ObjectSerialization.Serialize(new LmsSkillSettings { DigestMode = 2, DigestHour = 0, DigestWeekday = 0, NotifyMembers = false, NotifyOfficers = false });
			var read = Resgrid.Framework.ObjectSerialization.Deserialize<LmsSkillSettings>(stored).Normalized();
			read.DigestMode.Should().Be(2);
			read.DigestHour.Should().Be(0);
			read.DigestWeekday.Should().Be(0);
			read.NotifyMembers.Should().BeFalse();
			read.NotifyOfficers.Should().BeFalse();
		}

		[Test]
		public void Subject_ids_parse_both_kinds_and_nothing_else()
		{
			LmsSkillRules.ParseSubject(LmsSkillRules.EvaluationSubjectId(12)).Should().Be(("evaluation", 12));
			LmsSkillRules.ParseSubject(LmsSkillRules.TaskBookSubjectId(7)).Should().Be(("task-book", 7));
			LmsSkillRules.ParseSubject("evaluation:-1").Should().Be(((string)null, 0));
			LmsSkillRules.ParseSubject("12").Should().Be(((string)null, 0));
			LmsSkillRules.Hours(5400, 30).Should().Be(1.5m);
			LmsSkillRules.Hours(0, 30).Should().Be(0.5m);
			LmsSkillRules.Elapsed(DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Should().Be(86400, "an offline clock cannot claim more than a day");
		}

		[Test]
		public void The_evaluation_hash_moves_when_the_submitted_content_moves()
		{
			var evaluation = new LmsSkillEvaluation { LmsSkillEvaluationId = 4, Title = "SCBA", Code = "SKL-SCBA", StartedOn = new DateTime(2026, 11, 1), AnswersJson = Answers(("a", "pass")).ToJson() };
			var members = new[] { new LmsSkillEvaluationMember { UserId = "U1", AttemptNumber = 1 } };
			var first = LmsSkillSheetEngine.Sha256(LmsSkillSheetEngine.CanonicalEvaluation(evaluation, "h", members, null));
			LmsSkillSheetEngine.Sha256(LmsSkillSheetEngine.CanonicalEvaluation(evaluation, "h", members.Select(m => new LmsSkillEvaluationMember { UserId = "u1", AttemptNumber = 1 }), null))
				.Should().Be(first, "member ids compare case-insensitively");
			evaluation.AnswersJson = Answers(("a", "fail")).ToJson();
			LmsSkillSheetEngine.Sha256(LmsSkillSheetEngine.CanonicalEvaluation(evaluation, "h", members, null)).Should().NotBe(first);
		}

		#endregion Rules
	}
}
