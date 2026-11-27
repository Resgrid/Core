using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// Training T2 (courses and assessments): the pure rules. The separation rule (§17.3), the assessment engine (random draw, server-side
	/// scoring, keys never reaching the learner, the attempt revision), seat time, the lapse rules, the media state machine, crew
	/// codes and item analysis.
	/// </summary>
	[TestFixture]
	public class LmsRulesTests
	{
		#region Separation (§17.3)

		/// <summary>Types that predate T2 and carry the Lms prefix (T0/T1): baseline may use them, they are not course types.</summary>
		private static readonly HashSet<string> EarlierLmsTypes = new HashSet<string>(StringComparer.Ordinal)
		{
			"ILmsAccessService", "LmsAccessService", "LmsRunnableOrigins", "ILmsTrainingService", "LmsTrainingService",
			"ILmsCertificateTemplatesRepository", "LmsCertificateTemplatesRepository", "LmsCertificateTemplate",
			// LMS billing and seats (round 3 F): the add-on's entitlement, packs and seats. Baseline billing and personnel screens show them
			// (the add-on card, the seat switch); they are not course types and carry no course data.
			"ILmsAddonLapsesRepository", "ILmsAddonStatesRepository", "ILmsBillingService", "ILmsMemberSeatsRepository", "ILmsReactivationService",
			"ILmsSeatService", "LmsAddonLapse", "LmsAddonState", "LmsBillingCheckout", "LmsBillingStatus", "LmsDueItem", "LmsLapseReasons",
			"LmsLapseRebase", "LmsMemberSeat", "LmsPackChangeResult", "LmsPackPricing", "LmsRebasedDue", "LmsSeatBulkResult",
			"LmsSeatChangeResult", "LmsSeatOutcomes", "LmsSeatRules", "LmsSeatSummary", "LmsSubscriptionSnapshot", "LmsSubscriptionStates",
			// Round 10 A3: sponsored seats (T5 item 2a) are the seat ledger seen from the paying side. The set, link and sponsorship screens use them
			// (who pays, how many seats); they carry no course data, and course copies stay behind ILmsCourseService in the Lms-named source.
			"ILmsSponsorshipService", "ILmsSponsorshipsRepository", "LmsSponsorship", "LmsSponsorshipView", "LmsSponsorshipGrant", "LmsSponsorshipErrors",
			"LmsSponsorshipStatuses", "LmsSponsorshipEndReasons", "LmsSponsorshipRules", "LmsSponsorshipWorkflowChanges", "LmsSponsorshipWorkflowEvent"
		};

		/// <summary>The one seam baseline code may call into (Protocols P2 asks whether a member passed a course version).</summary>
		private static readonly HashSet<Type> SanctionedSeams = new HashSet<Type> { typeof(ILmsCourseResults) };

		private static bool IsCourseType(Type type)
		{
			if (type == null || SanctionedSeams.Contains(type) || EarlierLmsTypes.Contains(type.Name))
				return false;
			if (type.Namespace == "Resgrid.Model.Lms")
				return true;
			return type.Assembly == typeof(LmsCourse).Assembly && (type.Name.StartsWith("ILms", StringComparison.Ordinal) || type.Name.StartsWith("Lms", StringComparison.Ordinal));
		}

		/// <summary>Code that belongs to the LMS layer itself (it may use course types).</summary>
		private static bool IsLmsLayer(Type type)
		{
			for (var t = type; t != null; t = t.DeclaringType)
			{
				var name = t.Name;
				if (name.StartsWith("Lms", StringComparison.Ordinal) || name.StartsWith("ILms", StringComparison.Ordinal) || name.StartsWith("Academy", StringComparison.Ordinal) ||
					name == "CoursesController" || name.Contains("_Views_Courses_") || (t.FullName ?? string.Empty).Contains("_Views_Courses_") ||
					name.Contains("_Views_LmsPrograms_") || (t.FullName ?? string.Empty).Contains("_Views_LmsPrograms_") ||
					// T3: the Programs pages (Views/LmsRequirements) are LMS views by their folder, like the Courses pages.
					name.Contains("_Views_LmsRequirements_") || (t.FullName ?? string.Empty).Contains("_Views_LmsRequirements_") ||
					// T4 part 1: the Classes pages (Views/LmsClasses) are LMS views by their folder too.
					name.Contains("_Views_LmsClasses_") || (t.FullName ?? string.Empty).Contains("_Views_LmsClasses_") ||
					// T4 part 2: the Skills pages (Views/LmsSkills) are LMS views by their folder.
					name.Contains("_Views_LmsSkills_") || (t.FullName ?? string.Empty).Contains("_Views_LmsSkills_") ||
					// T5 data exit and CE reporting (round 6D): the Data export and CE reporting pages (Views/LmsExports) are LMS views by their folder.
					name.Contains("_Views_LmsExports_") || (t.FullName ?? string.Empty).Contains("_Views_LmsExports_") ||
					// T5: the External learners pages (Areas/User/Views/LmsExternalLearners) and the learner portal (Views/LmsLearn).
					name.Contains("_Views_LmsExternalLearners_") || (t.FullName ?? string.Empty).Contains("_Views_LmsExternalLearners_") ||
					name.StartsWith("Views_LmsLearn_", StringComparison.Ordinal) || (t.FullName ?? string.Empty).Contains("Views_LmsLearn_") ||
					// T5 (round 6F): the course studio pages (Views/LmsStudio: starter library, AI drafts, flashcards) are LMS views by their folder.
					name.Contains("_Views_LmsStudio_") || (t.FullName ?? string.Empty).Contains("_Views_LmsStudio_") ||
					// T5 standards: the Standards pages and the player / lesson-field partials (Views/LmsStandards) are LMS views by their folder.
					name.Contains("_Views_LmsStandards_") || (t.FullName ?? string.Empty).Contains("_Views_LmsStandards_"))
					return true;
				var ns = t.Namespace ?? string.Empty;
				if (ns == "Resgrid.Model.Lms" || ns.EndsWith(".Courses", StringComparison.Ordinal) || ns.EndsWith(".Lms", StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		private static IEnumerable<Type> Flatten(Type type)
		{
			if (type == null)
				yield break;
			if (type.HasElementType)
			{
				foreach (var inner in Flatten(type.GetElementType()))
					yield return inner;
				yield break;
			}
			yield return type;
			if (type.IsGenericType)
				foreach (var argument in type.GetGenericArguments())
					foreach (var inner in Flatten(argument))
						yield return inner;
		}

		private static IEnumerable<(string Member, Type Type)> Signature(Type type)
		{
			const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
			if (type.BaseType != null)
				yield return ("base", type.BaseType);
			foreach (var i in type.GetInterfaces())
				yield return ("implements", i);
			foreach (var field in type.GetFields(all))
				yield return (field.Name, field.FieldType);
			foreach (var property in type.GetProperties(all))
				yield return (property.Name, property.PropertyType);
			foreach (var method in type.GetMethods(all))
			{
				yield return (method.Name, method.ReturnType);
				foreach (var parameter in method.GetParameters())
					yield return (method.Name + "(" + parameter.Name + ")", parameter.ParameterType);
			}
			foreach (var constructor in type.GetConstructors(all))
				foreach (var parameter in constructor.GetParameters())
					yield return ("ctor(" + parameter.Name + ")", parameter.ParameterType);
		}

		private static IEnumerable<Type> LoadableTypes(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				return ex.Types.Where(t => t != null);
			}
		}

		[Test]
		public void No_baseline_type_references_a_course_type()
		{
			var assemblies = new[]
			{
				typeof(LmsCourse).Assembly,                                                               // Resgrid.Model
				typeof(Resgrid.Services.LmsCourseService).Assembly,                                       // Resgrid.Services
				typeof(Resgrid.Repositories.DataRepository.LmsCoursesRepository).Assembly,                // Resgrid.Repositories.DataRepository
				typeof(Resgrid.Web.Areas.User.Controllers.CoursesController).Assembly,                    // Resgrid.Web (views included)
				typeof(Resgrid.Web.Services.Controllers.v4.LmsController).Assembly                        // Resgrid.Web.Services
			};

			var offences = new List<string>();
			foreach (var type in assemblies.SelectMany(LoadableTypes).Where(t => !IsLmsLayer(t) && !IsCourseType(t)))
				foreach (var (member, referenced) in Signature(type))
					foreach (var inner in Flatten(referenced).Where(IsCourseType))
						offences.Add(type.FullName + "." + member + " -> " + inner.Name);

			offences.Should().BeEmpty("baseline code never depends on LMS course types (only the LMS layer does), so the LMS can stay dark or be removed");
		}

		[Test]
		public void The_course_types_are_the_ones_the_rule_guards()
		{
			IsCourseType(typeof(LmsCourse)).Should().BeTrue();
			IsCourseType(typeof(Resgrid.Model.Repositories.ILmsCoursesRepository)).Should().BeTrue();
			IsCourseType(typeof(ILmsLearnerService)).Should().BeTrue();
			IsCourseType(typeof(Resgrid.Model.Providers.ILmsMediaStore)).Should().BeTrue();
			IsCourseType(typeof(ILmsAccessService)).Should().BeFalse("the T1 gate is shared with baseline training screens");
			IsCourseType(typeof(ILmsCourseResults)).Should().BeFalse("the P2 seam is sanctioned");
		}

		#endregion Separation (§17.3)

		#region Assessment engine

		private static LmsQuestion Choice(int id, string text, string correct, params string[] keys)
		{
			var question = new LmsQuestion { LmsQuestionId = id, Kind = (int)LmsQuestionKinds.SingleChoice, Text = text, Points = 1, SortOrder = id, Tags = id % 2 == 0 ? "even" : "odd" };
			question.Options = keys.Select(k => new LmsQuestionOption { Key = k, Text = text + " option " + k, IsCorrect = k == correct }).ToList();
			return question;
		}

		private static List<LmsQuestion> Pool(int count) => Enumerable.Range(1, count).Select(i => Choice(i, "Q" + i, "b", "a", "b", "c", "d")).ToList();

		[Test]
		public void Draw_takes_the_requested_number_from_live_questions_with_the_tags_asked_for()
		{
			var pool = Pool(10);
			pool[0].IsRetired = true;

			var snapshot = LmsAssessmentEngine.Draw(pool, 3, "even", true, 80, new Random(7));

			snapshot.Questions.Should().HaveCount(3);
			snapshot.Questions.Select(q => q.QuestionId).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id % 2 == 0);
			snapshot.PassMark.Should().Be(80);
			LmsAssessmentEngine.Draw(pool, 0, null, false, 80, new Random(1)).Questions.Should().HaveCount(9, "0 draws every live question");
		}

		[Test]
		public void Different_seeds_draw_different_questions_and_orders()
		{
			var pool = Pool(20);
			var draws = Enumerable.Range(0, 8).Select(seed => string.Join(",", LmsAssessmentEngine.Draw(pool, 5, null, true, 80, new Random(seed)).Questions.Select(q => q.QuestionId))).ToList();
			draws.Distinct().Count().Should().BeGreaterThan(1);
		}

		[Test]
		public void The_learner_view_and_its_json_never_carry_a_key_or_an_explanation()
		{
			var pool = Pool(4);
			pool[0].Explanation = "SECRET-EXPLANATION";
			var ordering = new LmsQuestion { LmsQuestionId = 50, Kind = (int)LmsQuestionKinds.Ordering, Text = "Order", Points = 1 };
			ordering.Options = new List<LmsQuestionOption> { new LmsQuestionOption { Key = "a", Text = "First" }, new LmsQuestionOption { Key = "b", Text = "Second" }, new LmsQuestionOption { Key = "c", Text = "Third" } };
			pool.Add(ordering);

			var snapshot = LmsAssessmentEngine.Draw(pool, 0, null, true, 80, new Random(3));
			var view = LmsAssessmentEngine.ToLearnerView(snapshot);
			var json = JsonConvert.SerializeObject(view);

			json.Should().NotContain("IsCorrect").And.NotContain("CorrectKeys").And.NotContain("CorrectOrder").And.NotContain("SECRET-EXPLANATION");
			typeof(LmsLearnerQuestion).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "CorrectKeys", "CorrectOrder", "Explanation" });
			typeof(LmsLearnerOption).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(new[] { "Key", "Text" });
			var shownOrder = view.Single(q => q.QuestionId == 50).Options.Select(o => o.Key).ToList();
			shownOrder.Should().NotEqual(new[] { "a", "b", "c" }, "an ordering question is never shown already in order");
		}

		[Test]
		public void Scoring_happens_on_the_snapshot_and_awaits_a_grader_for_short_answers()
		{
			var pool = Pool(3);
			var free = new LmsQuestion { LmsQuestionId = 9, Kind = (int)LmsQuestionKinds.ShortAnswer, Text = "Explain", Points = 2 };
			pool.Add(free);
			var snapshot = LmsAssessmentEngine.Draw(pool, 0, null, true, 75, new Random(5));

			var answers = new List<LmsAttemptAnswer>
			{
				new LmsAttemptAnswer { QuestionId = 1, Keys = new List<string> { "b" } },
				new LmsAttemptAnswer { QuestionId = 2, Keys = new List<string> { "a" } },
				new LmsAttemptAnswer { QuestionId = 3, Keys = new List<string> { "b", "c" } },
				new LmsAttemptAnswer { QuestionId = 9, Text = "Because the hydrant pressure drops." }
			};
			var pending = LmsAssessmentEngine.Score(snapshot, answers);
			pending.PendingGrading.Should().BeTrue();
			pending.Passed.Should().BeNull("nothing is decided while a grader has work");
			pending.MaxScore.Should().Be(5);
			pending.Score.Should().Be(1, "only question 1 is all and only the correct keys");

			var graded = LmsAssessmentEngine.Score(snapshot, answers, new[] { new LmsManualGrade { QuestionId = 9, Awarded = 5 } });
			graded.PendingGrading.Should().BeFalse();
			graded.Results.Single(r => r.QuestionId == 9).Awarded.Should().Be(2, "a grade is capped at the question's points");
			graded.Percent.Should().Be(60);
			graded.Passed.Should().BeFalse();

			var empty = LmsAssessmentEngine.Score(snapshot, answers.Where(a => a.QuestionId != 9));
			empty.PendingGrading.Should().BeFalse("an empty short answer scores zero without waiting for anyone");
		}

		[Test]
		public void The_revision_hash_ignores_draw_order_and_changes_with_the_key()
		{
			var pool = Pool(5);
			var a = LmsAssessmentEngine.Draw(pool, 0, null, true, 80, new Random(1));
			var b = LmsAssessmentEngine.Draw(pool, 0, null, true, 80, new Random(99));
			a.Questions.Select(q => q.QuestionId).Should().NotEqual(b.Questions.Select(q => q.QuestionId), "the seeds shuffle differently");
			LmsAssessmentEngine.RevisionHash(a).Should().Be(LmsAssessmentEngine.RevisionHash(b));

			pool[2].Options = pool[2].Options.Select(o => new LmsQuestionOption { Key = o.Key, Text = o.Text, IsCorrect = o.Key == "c" }).ToList();
			var rekeyed = LmsAssessmentEngine.Draw(pool, 0, null, true, 80, new Random(1));
			LmsAssessmentEngine.RevisionHash(rekeyed).Should().NotBe(LmsAssessmentEngine.RevisionHash(a));
		}

		[Test]
		public void A_snapshot_round_trips_through_json_and_scores_the_same_after_the_bank_changes()
		{
			var pool = Pool(3);
			var snapshot = LmsAssessmentEngine.Draw(pool, 0, null, false, 50, new Random(2));
			var stored = JsonConvert.SerializeObject(snapshot);

			// The author re-keys question 1 after the attempt started: the attempt keeps the key it was drawn with.
			pool[0].Options = pool[0].Options.Select(o => new LmsQuestionOption { Key = o.Key, Text = o.Text, IsCorrect = o.Key == "d" }).ToList();
			var answers = new[] { new LmsAttemptAnswer { QuestionId = 1, Keys = new List<string> { "b" } } };
			LmsAssessmentEngine.Score(LmsAttemptSnapshot.Parse(stored), answers).Results.Single(r => r.QuestionId == 1).Correct.Should().BeTrue();
		}

		[Test]
		public void Translations_replace_question_and_option_text_for_the_learner_locale()
		{
			var question = Choice(1, "Which?", "a", "a", "b");
			question.TranslationsJson = JsonConvert.SerializeObject(new Dictionary<string, LmsQuestionTranslation>
			{
				["de"] = new LmsQuestionTranslation { Text = "Welche?", Options = new Dictionary<string, string> { ["a"] = "Erste", ["b"] = "Zweite" } }
			});
			var snapshot = LmsAssessmentEngine.Draw(new[] { question }, 0, null, false, 80, new Random(1), "de-AT");
			snapshot.Questions[0].Text.Should().Be("Welche?");
			snapshot.Questions[0].Options.Select(o => o.Text).Should().BeEquivalentTo(new[] { "Erste", "Zweite" });
			snapshot.Questions[0].CorrectKeys.Should().Equal("a");
		}

		[TestCase(LmsShowAnswersPolicies.Never, true, false, false)]
		[TestCase(LmsShowAnswersPolicies.CorrectnessOnly, false, true, false)]
		[TestCase(LmsShowAnswersPolicies.AfterPassing, false, false, false)]
		[TestCase(LmsShowAnswersPolicies.AfterPassing, true, true, true)]
		[TestCase(LmsShowAnswersPolicies.Always, false, true, true)]
		public void The_answer_review_policy_decides_what_a_learner_sees(LmsShowAnswersPolicies policy, bool passed, bool correctness, bool answers)
		{
			LmsAssessmentEngine.MayShowCorrectness(policy, passed).Should().Be(correctness);
			LmsAssessmentEngine.MayShowAnswers(policy, passed).Should().Be(answers);
		}

		#endregion Assessment engine

		#region Item analysis

		private static LmsAssessmentAttempt Finished(int id, decimal percent, params (int Question, bool Correct, string Key)[] results)
		{
			var snapshot = new LmsAttemptSnapshot
			{
				Questions = results.Select(r => new LmsSnapshotQuestion { QuestionId = r.Question, Kind = (int)LmsQuestionKinds.SingleChoice, Text = "Q" + r.Question, Points = 1 }).ToList()
			};
			return new LmsAssessmentAttempt
			{
				LmsAssessmentAttemptId = id,
				Status = (int)LmsAttemptStatuses.Scored,
				Percent = percent,
				SnapshotJson = JsonConvert.SerializeObject(snapshot),
				ResultsJson = JsonConvert.SerializeObject(results.Select(r => new LmsQuestionResult { QuestionId = r.Question, Correct = r.Correct, Points = 1, Awarded = r.Correct ? 1 : 0 })),
				AnswersJson = JsonConvert.SerializeObject(results.Select(r => new LmsAttemptAnswer { QuestionId = r.Question, Keys = new List<string> { r.Key } }))
			};
		}

		[Test]
		public void Item_analysis_reports_difficulty_and_upper_lower_discrimination()
		{
			// Ten finished attempts: question 1 is answered correctly by the five best and wrongly by the five worst.
			var attempts = Enumerable.Range(1, 10).Select(i => Finished(i, 100 - i * 5, (1, i <= 5, i <= 5 ? "a" : "b"), (2, true, "c"))).ToList();
			attempts.Add(new LmsAssessmentAttempt { LmsAssessmentAttemptId = 99, Status = (int)LmsAttemptStatuses.PendingGrading, ResultsJson = "[]" });

			var stats = LmsAssessmentEngine.ItemAnalysis(attempts);

			var q1 = stats.Single(s => s.QuestionId == 1);
			q1.Responses.Should().Be(10, "attempts awaiting a grader are left out");
			q1.Difficulty.Should().Be(0.5m);
			// Groups of round(10 * 0.27) = 3: top 3 all correct, bottom 3 all wrong.
			q1.Discrimination.Should().Be(1m);
			q1.OptionCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["a"] = 5, ["b"] = 5 });

			var q2 = stats.Single(s => s.QuestionId == 2);
			q2.Difficulty.Should().Be(1m);
			q2.Discrimination.Should().Be(0m);
		}

		[Test]
		public void Discrimination_needs_enough_attempts()
		{
			var attempts = Enumerable.Range(1, LmsAssessmentEngine.MinimumAttemptsForDiscrimination - 1).Select(i => Finished(i, i * 10, (1, i % 2 == 0, "a"))).ToList();
			var stat = LmsAssessmentEngine.ItemAnalysis(attempts).Single();
			stat.Discrimination.Should().BeNull();
			stat.Difficulty.Should().Be(Math.Round(4m / 9m, 4));
		}

		#endregion Item analysis

		#region Seat time and attempts

		[Test]
		public void Seat_time_never_outruns_the_wall_clock()
		{
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			LmsSeatTime.Accrue(null, now, 600).Should().Be(LmsSeatTime.FirstHeartbeatSeconds);
			LmsSeatTime.Accrue(now.AddSeconds(-15), now, 15).Should().Be(15);
			LmsSeatTime.Accrue(now.AddSeconds(-15), now, 600).Should().Be(17, "elapsed plus two seconds of slack");
			LmsSeatTime.Accrue(now.AddHours(-1), now, 3600).Should().Be(LmsSeatTime.MaxHeartbeatSeconds);
			LmsSeatTime.Accrue(now.AddSeconds(5), now, 15).Should().Be(0, "a heartbeat from the future adds nothing");
			LmsSeatTime.Accrue(now.AddSeconds(-15), now, -4).Should().Be(0);
		}

		[Test]
		public void Attempt_limits_cooldowns_and_passes_decide_whether_a_new_attempt_may_start()
		{
			var now = DateTime.UtcNow;
			var lesson = new LmsLesson { Kind = (int)LmsLessonKinds.Assessment, AttemptLimit = 2, RetakeCooldownHours = 4 };
			LmsAttemptRules.CanStart(lesson, new List<LmsAssessmentAttempt>(), now).Error.Should().Be(LmsErrors.None);

			var failed = new LmsAssessmentAttempt { LmsAssessmentAttemptId = 1, Status = (int)LmsAttemptStatuses.Scored, Passed = false, SubmittedOn = now.AddHours(-1) };
			var (error, next) = LmsAttemptRules.CanStart(lesson, new[] { failed }, now);
			error.Should().Be(LmsErrors.CooldownActive);
			next.Should().BeCloseTo(now.AddHours(3), TimeSpan.FromSeconds(1));

			failed.SubmittedOn = now.AddHours(-5);
			LmsAttemptRules.CanStart(lesson, new[] { failed }, now).Error.Should().Be(LmsErrors.None);

			var timedOut = new LmsAssessmentAttempt { LmsAssessmentAttemptId = 2, Status = (int)LmsAttemptStatuses.InProgress, DeadlineOn = now.AddMinutes(-1) };
			LmsAttemptRules.CanStart(lesson, new[] { failed, timedOut }, now).Error.Should().Be(LmsErrors.AttemptLimitReached, "walking away from a timed attempt still uses it");

			var passed = new LmsAssessmentAttempt { LmsAssessmentAttemptId = 3, Status = (int)LmsAttemptStatuses.Scored, Passed = true, SubmittedOn = now.AddDays(-2) };
			LmsAttemptRules.CanStart(new LmsLesson { Kind = (int)LmsLessonKinds.Assessment }, new[] { passed }, now).Error.Should().Be(LmsErrors.AlreadySubmitted);

			var grading = new LmsAssessmentAttempt { LmsAssessmentAttemptId = 4, Status = (int)LmsAttemptStatuses.PendingGrading, SubmittedOn = now.AddDays(-2) };
			LmsAttemptRules.CanStart(new LmsLesson { Kind = (int)LmsLessonKinds.Assessment }, new[] { grading }, now).Error.Should().Be(LmsErrors.RequirementNotMet);
		}

		[Test]
		public void Only_an_untimed_unlimited_non_certifying_quiz_may_be_answered_offline()
		{
			var quiz = new LmsLesson { Kind = (int)LmsLessonKinds.Assessment };
			LmsAttemptRules.MayAnswerOffline(quiz, new LmsCourseVersion()).Should().BeTrue();
			LmsAttemptRules.MayAnswerOffline(new LmsLesson { Kind = (int)LmsLessonKinds.Assessment, TimeLimitMinutes = 10 }, null).Should().BeFalse();
			LmsAttemptRules.MayAnswerOffline(new LmsLesson { Kind = (int)LmsLessonKinds.Assessment, AttemptLimit = 1 }, null).Should().BeFalse();
			LmsAttemptRules.MayAnswerOffline(new LmsLesson { Kind = (int)LmsLessonKinds.Assessment, IsPreTest = true }, null).Should().BeFalse();
			LmsAttemptRules.MayAnswerOffline(quiz, new LmsCourseVersion { IssuesCertificationTypeId = 4 }).Should().BeFalse();
		}

		#endregion Seat time and attempts

		#region Gate and lapse rules

		private static readonly DateTime Now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

		private static LmsEnrollment Assigned(DateTime assignedOn, DateTime? dueOn) => new LmsEnrollment { AssignedOn = assignedOn, DueOn = dueOn, IsOpen = true };

		[Test]
		public void The_gate_fails_closed_for_department_content_and_passes_system_content_with_the_module_on()
		{
			var dark = new LmsDepartmentGate { DepartmentId = 5, ModuleOn = true, FlagOn = false };
			LmsLapseRules.CanRun(dark, false, Assigned(Now.AddDays(-1), null), Now).Should().BeFalse();
			LmsLapseRules.CanAssignNew(dark, false).Should().BeFalse();
			LmsLapseRules.CanRun(dark, true, Assigned(Now.AddDays(-1), null), Now).Should().BeTrue("the Academy runs for every department with Trainings on");
			LmsLapseRules.CanAssignNew(dark, true).Should().BeTrue();

			var unknownBilling = new LmsDepartmentGate { DepartmentId = 5, ModuleOn = true, FlagOn = true, AddonActive = null };
			unknownBilling.CanAuthor.Should().BeFalse("billing that cannot answer is not an active add-on");
			LmsLapseRules.CanRun(unknownBilling, false, Assigned(Now.AddDays(-1), null), Now).Should().BeFalse("no lapse clock, no grace");

			var moduleOff = new LmsDepartmentGate { DepartmentId = 5, ModuleOn = false };
			LmsLapseRules.CanRun(moduleOff, true, Assigned(Now.AddDays(-1), null), Now).Should().BeFalse();
			LmsLapseRules.CanRun(null, true, null, Now).Should().BeFalse();

			var paid = new LmsDepartmentGate { DepartmentId = 5, ModuleOn = true, FlagOn = true, AddonActive = true, LastEntitledOn = Now };
			paid.CanAuthor.Should().BeTrue();
			LmsLapseRules.CanRun(paid, false, Assigned(Now, null), Now).Should().BeTrue();
		}

		[Test]
		public void Lapse_rule_3_stops_authoring_and_new_assignments()
		{
			var lapsed = new LmsDepartmentGate { ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = Now.AddDays(-3) };
			lapsed.IsLapsed.Should().BeTrue();
			lapsed.CanAuthor.Should().BeFalse();
			LmsLapseRules.CanAssignNew(lapsed, false).Should().BeFalse();
			LmsLapseRules.CanUploadMedia(lapsed).Should().BeFalse();
			LmsLapseRules.CanRun(lapsed, false, Assigned(Now.AddDays(-1), Now.AddDays(10)), Now).Should().BeFalse("assigned after the lapse");
		}

		[Test]
		public void Lapse_rule_4_keeps_dated_assignments_takeable_until_their_due_date()
		{
			var lapsedOn = Now.AddDays(-60);
			var lapsed = new LmsDepartmentGate { ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = lapsedOn };
			LmsLapseRules.CanRun(lapsed, false, Assigned(lapsedOn.AddDays(-5), Now.AddDays(1)), Now).Should().BeTrue();
			LmsLapseRules.CanRun(lapsed, false, Assigned(lapsedOn.AddDays(-5), Now.AddDays(-1)), Now).Should().BeFalse();
		}

		[Test]
		public void Lapse_rule_11_keeps_undated_courses_takeable_for_30_days()
		{
			var lapsedOn = Now.AddDays(-29);
			var gate = new LmsDepartmentGate { ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = lapsedOn };
			LmsLapseRules.CanRun(gate, false, Assigned(lapsedOn.AddDays(-5), null), Now).Should().BeTrue();
			gate.LastEntitledOn = Now.AddDays(-31);
			LmsLapseRules.CanRun(gate, false, Assigned(Now.AddDays(-40), null), Now).Should().BeFalse();
		}

		[Test]
		public void Lapse_rule_6_streams_hosted_video_for_90_days_then_archives_it()
		{
			var gate = new LmsDepartmentGate { ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = Now.AddDays(-89) };
			LmsLapseRules.CanStreamHostedVideo(gate, Now).Should().BeTrue();
			LmsLapseRules.ShouldArchiveHostedMedia(gate, Now).Should().BeFalse();
			gate.LastEntitledOn = Now.AddDays(-91);
			LmsLapseRules.CanStreamHostedVideo(gate, Now).Should().BeFalse();
			LmsLapseRules.ShouldArchiveHostedMedia(gate, Now).Should().BeTrue();
			gate.AddonActive = null;
			LmsLapseRules.ShouldArchiveHostedMedia(gate, Now).Should().BeFalse("billing that cannot answer never archives anything");
		}

		#endregion Gate and lapse rules

		#region Media and crew codes

		[Test]
		public void The_media_state_machine_moves_only_along_its_edges()
		{
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Uploading, LmsMediaStatuses.Queued).Should().BeTrue();
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Queued, LmsMediaStatuses.Transcoding).Should().BeTrue();
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Transcoding, LmsMediaStatuses.Ready).Should().BeTrue();
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Ready, LmsMediaStatuses.Archived).Should().BeTrue();
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Archived, LmsMediaStatuses.Ready).Should().BeTrue();
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Queued, LmsMediaStatuses.Ready).Should().BeFalse();
			LmsMediaStateMachine.CanMove(LmsMediaStatuses.Ready, LmsMediaStatuses.Transcoding).Should().BeFalse();
			LmsMediaStateMachine.AfterFailure(1).Should().Be(LmsMediaStatuses.Queued);
			LmsMediaStateMachine.AfterFailure(LmsMediaStateMachine.MaxAttempts).Should().Be(LmsMediaStatuses.Failed);
		}

		[Test]
		public void A_crew_code_is_valid_in_its_slice_and_the_next_only()
		{
			var secret = LmsCrewCodes.NewSecret();
			var start = new DateTime(2026, 11, 25, 12, 0, 1, DateTimeKind.Utc);
			var code = LmsCrewCodes.CurrentCode(secret, start);
			code.Should().MatchRegex("^[0-9a-f]{12}$");
			LmsCrewCodes.IsValid(secret, code, start.AddSeconds(20)).Should().BeTrue();
			LmsCrewCodes.IsValid(secret, code.ToUpperInvariant(), start.AddSeconds(40)).Should().BeTrue("the previous slice still counts");
			LmsCrewCodes.IsValid(secret, code, start.AddSeconds(65)).Should().BeFalse();
			LmsCrewCodes.IsValid(LmsCrewCodes.NewSecret(), code, start).Should().BeFalse();
			LmsCrewCodes.IsValid(secret, "", start).Should().BeFalse();
		}

		[Test]
		public void A_hosted_playlist_is_rewritten_to_signed_segment_urls_only()
		{
			var playlist = "#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:6.0,\nseg_000.ts\n#EXTINF:6.0,\n../other/seg.ts\nhttps://evil.example/x.ts\n/abs.ts\nseg_001.ts\n#EXT-X-ENDLIST\n";
			var rewritten = Resgrid.Services.LmsMediaService.RewritePlaylist(playlist, "lms/5/9/hls/", key => "https://signed/" + key + "?sig=1");
			rewritten.Should().Contain("https://signed/lms/5/9/hls/seg_000.ts?sig=1").And.Contain("https://signed/lms/5/9/hls/seg_001.ts?sig=1");
			rewritten.Should().NotContain("evil.example").And.NotContain("../other").And.NotContain("/abs.ts");
			rewritten.Should().Contain("#EXT-X-ENDLIST");
		}

		[Test]
		public void Captions_must_be_webvtt()
		{
			Resgrid.Services.LmsMediaService.IsWebVtt(System.Text.Encoding.UTF8.GetBytes("WEBVTT\n\n00:00.000 --> 00:01.000\nHello")).Should().BeTrue();
			Resgrid.Services.LmsMediaService.IsWebVtt(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("WEBVTT\n")).ToArray()).Should().BeTrue();
			Resgrid.Services.LmsMediaService.IsWebVtt(System.Text.Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\nSRT")).Should().BeFalse();
		}

		#endregion Media and crew codes

		#region Course content

		[Test]
		public void The_course_hash_changes_with_lesson_content_and_not_with_ids_of_the_rows()
		{
			var version = new LmsCourseVersion { LmsCourseVersionId = 1, Title = "Pump ops", Hours = 1.5m };
			var module = new LmsCourseModule { LmsCourseModuleId = 3, Title = "Module", SortOrder = 1 };
			var lesson = new LmsLesson { LmsLessonId = 7, LmsCourseModuleId = 3, Kind = (int)LmsLessonKinds.Blocks, Title = "Intro", ContentHtml = "<p>Hello</p>", SortOrder = 1 };
			var hash = LmsCourseContent.ComputeHash(version, new[] { module }, new[] { lesson });

			// A copy of the same content in a new draft (new row ids) hashes the same.
			var copiedModule = new LmsCourseModule { LmsCourseModuleId = 30, Title = "Module", SortOrder = 1 };
			var copiedLesson = new LmsLesson { LmsLessonId = 70, LmsCourseModuleId = 30, Kind = (int)LmsLessonKinds.Blocks, Title = "Intro", ContentHtml = "<p>Hello</p>", SortOrder = 1 };
			LmsCourseContent.ComputeHash(new LmsCourseVersion { LmsCourseVersionId = 2, Title = "Pump ops", Hours = 1.5m }, new[] { copiedModule }, new[] { copiedLesson })
				.Should().Be(hash);

			lesson.ContentHtml = "<p>Hello, crew</p>";
			LmsCourseContent.ComputeHash(version, new[] { module }, new[] { lesson }).Should().NotBe(hash);
			SignaturePolicy.ShortCode(hash).Should().NotBeNullOrEmpty();
		}

		[Test]
		public void Course_versions_number_like_protocols()
		{
			ProtocolLifecycle.NextNumber(0, 0, false).Should().Be((1, 0));
			ProtocolLifecycle.NextNumber(1, 0, false).Should().Be((1, 1));
			ProtocolLifecycle.NextNumber(1, 3, true).Should().Be((2, 0));
		}

		#endregion Course content
	}
}
