using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// T5 item 3 runtime: the SCORM data model the server enforces, commits that persist and resume, cmi5 statements accepted only with the
	/// launch's auth token, and completion through the learner service (gate and lapse rules: fails closed).
	/// </summary>
	[TestFixture]
	public class LmsRuntimeTests
	{
		#region SCORM data model

		[Test]
		public void Only_writable_elements_with_valid_values_are_accepted()
		{
			var v12 = (int)LmsRuntimeKinds.Scorm12;
			var v2004 = (int)LmsRuntimeKinds.Scorm2004;
			LmsScormRuntime.IsWritable(v12, "cmi.core.lesson_status", "passed").Should().BeTrue();
			LmsScormRuntime.IsWritable(v12, "cmi.core.lesson_status", "not attempted").Should().BeFalse("a SCO cannot set not attempted in 1.2");
			LmsScormRuntime.IsWritable(v12, "cmi.core.student_id", "someone-else").Should().BeFalse("read-only");
			LmsScormRuntime.IsWritable(v12, "cmi.suspend_data", new string('x', 4096)).Should().BeTrue();
			LmsScormRuntime.IsWritable(v12, "cmi.suspend_data", new string('x', 4097)).Should().BeFalse();
			LmsScormRuntime.IsWritable(v12, "cmi.core.score.raw", "").Should().BeTrue("CMIBlank");
			LmsScormRuntime.IsWritable(v12, "cmi.interactions.3.id", "q3").Should().BeTrue();
			LmsScormRuntime.IsWritable(v12, "cmi.interactions.250.id", "q").Should().BeFalse("collections are bounded");
			LmsScormRuntime.IsWritable(v2004, "cmi.score.scaled", "1.5").Should().BeFalse();
			LmsScormRuntime.IsWritable(v2004, "cmi.suspend_data", new string('x', 64000)).Should().BeTrue();
			LmsScormRuntime.IsWritable(v2004, "cmi.learner_id", "x").Should().BeFalse();
			LmsScormRuntime.IsWritable(v2004, "cmi.completion_status", "completed").Should().BeTrue();
		}

		[Test]
		public void Scorm_12_status_and_the_mastery_score_decide_satisfaction()
		{
			var item = new LmsPackageItem { Identifier = "i", MasteryScore = 80 };
			var sco = new LmsScoState();
			LmsScormRuntime.Apply((int)LmsRuntimeKinds.Scorm12, sco, new Dictionary<string, string> { ["cmi.core.lesson_status"] = "completed", ["cmi.core.score.raw"] = "70" }, false, item, 60);
			sco.Satisfied.Should().BeFalse("70 is below the mastery score, so the LMS records failed");
			sco.Success.Should().Be("failed");
			sco.ScorePercent.Should().Be(70);

			LmsScormRuntime.Apply((int)LmsRuntimeKinds.Scorm12, sco, new Dictionary<string, string> { ["cmi.core.score.raw"] = "85" }, false, item, 60);
			sco.Satisfied.Should().BeTrue();
			sco.Success.Should().Be("passed");
		}

		[Test]
		public void Scorm_2004_completion_threshold_and_scaled_passing_score_decide_satisfaction()
		{
			var item = new LmsPackageItem { Identifier = "i", MasteryScore = 0.7m, CompletionThreshold = 0.9m };
			var sco = new LmsScoState();
			LmsScormRuntime.Apply((int)LmsRuntimeKinds.Scorm2004, sco, new Dictionary<string, string> { ["cmi.completion_status"] = "completed", ["cmi.progress_measure"] = "0.5" }, false, item, 60);
			sco.Completion.Should().Be("incomplete", "the threshold decides completion when progress is reported");
			sco.Satisfied.Should().BeFalse();

			LmsScormRuntime.Apply((int)LmsRuntimeKinds.Scorm2004, sco, new Dictionary<string, string> { ["cmi.progress_measure"] = "1", ["cmi.score.scaled"] = "0.6" }, false, item, 60);
			sco.Success.Should().Be("failed");
			sco.Satisfied.Should().BeFalse("completed but failed");

			LmsScormRuntime.Apply((int)LmsRuntimeKinds.Scorm2004, sco, new Dictionary<string, string> { ["cmi.score.scaled"] = "0.75" }, false, item, 60);
			sco.Satisfied.Should().BeTrue();
			sco.ScorePercent.Should().Be(75);
		}

		[Test]
		public void Session_time_counts_once_on_finish_and_never_more_than_really_elapsed()
		{
			var sco = new LmsScoState();
			LmsScormRuntime.Apply((int)LmsRuntimeKinds.Scorm12, sco, new Dictionary<string, string> { ["cmi.core.session_time"] = "0001:00:00.00", ["cmi.core.exit"] = "suspend" }, true, null, 600);
			sco.TotalSeconds.Should().Be(600, "an hour claimed in ten minutes counts ten minutes");
			sco.Suspended.Should().BeTrue();
			sco.Values.Should().NotContainKey("cmi.core.session_time");

			LmsScormRuntime.ParseTimespan("0002:30:15.50").Should().Be(9015);
			LmsScormRuntime.ParseDuration("PT1H2M3.5S").Should().Be(3723);
			LmsScormRuntime.ParseDuration("P").Should().BeNull();
			LmsScormRuntime.FormatDuration(3723).Should().Be("PT1H2M3S");
			LmsScormRuntime.FormatTimespan(3723).Should().Be("0001:02:03.00");
		}

		#endregion SCORM data model

		#region SCORM runtime

		[Test]
		public async Task Scorm_commits_persist_and_the_next_launch_resumes_from_them()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId);

			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test");
			launch.Succeeded.Should().BeTrue(launch.Error.ToString());
			launch.Url.Should().StartWith("/lms-content/").And.EndWith("/index.html");
			var token = LmsStandardsWorld.TokenOf(launch.Url);
			world.Sessions.Rows.Single().TokenHash.Should().Be(LmsLearnerIdentity.HashToken(token)).And.NotBe(token, "only the hash is stored");

			var first = await world.Runtime.GetScormConfigAsync(token, "/commit");
			first.Version.Should().Be("1.2");
			first.Values["cmi.core.entry"].Should().Be("ab-initio");
			first.Values["cmi.core.student_id"].Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member));
			first.Values["cmi.core.student_name"].Should().Be("Member, Pat");
			first.Values["cmi.launch_data"].Should().Be("start=1");

			var commit = await world.Runtime.CommitScormAsync(token, new LmsScormCommit
			{
				Values = { ["cmi.core.lesson_location"] = "page-4", ["cmi.suspend_data"] = "{\"p\":4}", ["cmi.core.lesson_status"] = "incomplete", ["cmi.core.exit"] = "suspend",
					["cmi.core.student_id"] = "forged" },
				Finish = true
			});
			commit.Succeeded.Should().BeTrue();
			((List<string>)commit.Value).Should().Equal("cmi.core.student_id");
			(await world.Runtime.CommitScormAsync(token, new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "passed" } })).Status.Should().Be(409, "the session finished");

			var again = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test");
			var resumed = await world.Runtime.GetScormConfigAsync(LmsStandardsWorld.TokenOf(again.Url), "/commit");
			resumed.Values["cmi.core.entry"].Should().Be("resume");
			resumed.Values["cmi.core.lesson_location"].Should().Be("page-4");
			resumed.Values["cmi.suspend_data"].Should().Be("{\"p\":4}");
			resumed.Values["cmi.core.student_id"].Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member));
			world.Registrations.Rows.Should().ContainSingle("one registration per enrollment and lesson");
			world.Lms.Progress.Rows.Single(p => p.LmsLessonId == lesson.LmsLessonId).Status.Should().NotBe((int)LmsLessonProgressStatuses.Completed);
		}

		[Test]
		public async Task A_passing_sco_completes_the_lesson_and_the_course_with_a_learning_record()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId);
			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test");

			var outcome = await world.Runtime.CommitScormAsync(LmsStandardsWorld.TokenOf(launch.Url), new LmsScormCommit
			{
				Values = { ["cmi.core.lesson_status"] = "passed", ["cmi.core.score.raw"] = "92", ["cmi.core.session_time"] = "0000:05:00.00" },
				Finish = true
			});

			outcome.LessonSatisfied.Should().BeTrue();
			outcome.CourseCompleted.Should().BeTrue();
			world.Registrations.Rows.Single().ScorePercent.Should().Be(92);
			world.Lms.Progress.Rows.Single(p => p.LmsLessonId == lesson.LmsLessonId).Status.Should().Be((int)LmsLessonProgressStatuses.Completed);
			world.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId).Status.Should().Be((int)LmsEnrollmentStatuses.Completed);
			world.Lms.Records.Rows.Should().ContainSingle(r => r.UserId == LmsWorld.Member && r.SourceKind == (int)LearningRecordSourceKinds.LmsCourseCompletion);
		}

		[Test]
		public async Task A_runtime_lesson_is_never_completed_by_the_learners_button()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId);

			var result = await world.Lms.Learner.CompleteLessonAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, new LmsLessonCompletionInput());
			result.Error.Should().Be(LmsErrors.RequirementNotMet);
		}

		[Test]
		public async Task Launch_and_completion_fail_closed_when_the_lms_is_dark_lapsed_or_the_lesson_is_locked()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId, readingFirst: true);

			(await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://s")).Error
				.Should().Be(LmsErrors.LessonLocked, "the reading before it is not done");
			(await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member2, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://s")).Error
				.Should().Be(LmsErrors.NotFound, "another member's enrollment");

			var reading = world.Lms.Lessons.Rows.Single(l => l.Kind == (int)LmsLessonKinds.Blocks && l.LmsCourseVersionId == lesson.LmsCourseVersionId);
			(await world.Lms.Learner.CompleteLessonAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, reading.LmsLessonId, new LmsLessonCompletionInput())).Succeeded.Should().BeTrue();
			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://s");
			launch.Succeeded.Should().BeTrue();

			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = false };
			(await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://s")).Succeeded.Should().BeFalse("the flag is off");
			var refused = await world.Runtime.CommitScormAsync(LmsStandardsWorld.TokenOf(launch.Url), new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "passed" }, Finish = true });
			refused.LessonSatisfied.Should().BeFalse("the learner service refuses the completion while the LMS is dark");
			refused.Error.Should().Be(LmsErrors.AddonRequired);
			world.Lms.Progress.Rows.Should().NotContain(p => p.LmsLessonId == lesson.LmsLessonId && p.Status == (int)LmsLessonProgressStatuses.Completed);
		}

		[Test]
		public async Task Content_resolves_only_for_a_live_token_and_reads_the_launchs_own_package()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId);
			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://s");
			var token = LmsStandardsWorld.TokenOf(launch.Url);

			var session = await world.Runtime.ResolveContentAsync(token);
			session.Package.LmsContentPackageId.Should().Be(package.LmsContentPackageId);
			(await world.Runtime.OpenContentAsync(session, "shared/style.css")).Should().NotBeNull();
			(await world.Runtime.OpenContentAsync(session, "../other/secret.html")).Should().BeNull();
			(await world.Runtime.OpenContentAsync(session, "missing.html")).Should().BeNull();
			(await world.Runtime.ResolveContentAsync("not-a-token")).Should().BeNull();

			LmsStandardsRuntimeService.ClearCache();
			world.Sessions.Rows.Single().ExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			(await world.Runtime.ResolveContentAsync(token)).Should().BeNull("expired");
		}

		#endregion SCORM runtime

		#region cmi5

		private static JObject Statement(string verb, string auId, string registration, string sessionId, int departmentId, string userId, JObject result = null)
		{
			var statement = new JObject
			{
				["actor"] = LmsLearnerIdentity.AccountActor(departmentId, userId),
				["verb"] = new JObject { ["id"] = verb },
				["object"] = new JObject { ["id"] = auId, ["objectType"] = "Activity" },
				["context"] = new JObject
				{
					["registration"] = registration,
					["contextActivities"] = new JObject { ["category"] = new JArray(new JObject { ["id"] = LmsCmi5Rules.CategoryCmi5 }) },
					["extensions"] = new JObject { [LmsCmi5Rules.SessionIdExtension] = sessionId }
				}
			};
			if (result != null)
				statement["result"] = result;
			return statement;
		}

		private static async Task<(LmsStandardsWorld World, LmsContentLaunch Launch, string Fetch, LmsLesson Lesson, LmsEnrollment Enrollment)> Cmi5LaunchAsync()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Cmi5Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Cmi5, package.LmsContentPackageId);
			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test");
			launch.Succeeded.Should().BeTrue(launch.Error.ToString());
			var query = System.Web.HttpUtility.ParseQueryString(launch.Url.Substring(launch.Url.IndexOf('?')));
			return (world, launch, query["fetch"].Substring(query["fetch"].LastIndexOf('/') + 1), lesson, enrollment);
		}

		[Test]
		public async Task A_cmi5_launch_carries_the_endpoint_fetch_url_actor_registration_and_activity()
		{
			var (world, launch, _, _, _) = await Cmi5LaunchAsync();
			var query = System.Web.HttpUtility.ParseQueryString(launch.Url.Substring(launch.Url.IndexOf('?')));

			query["endpoint"].Should().Be("https://site.test/lms-content/xapi/");
			query["fetch"].Should().StartWith("https://site.test/lms-content/cmi5/fetch/");
			query["registration"].Should().Be(launch.Registration.RegistrationKey);
			query["activityId"].Should().Be("https://example.test/au/1");
			var actor = JObject.Parse(query["actor"]);
			((string)actor["account"]["name"]).Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member));
			actor["mbox"].Should().BeNull("no e-mail address leaves Resgrid");
			world.Statements.Rows.Should().ContainSingle(s => s.VerbId == LmsCmi5Rules.Launched, "the LMS records launched");
		}

		[Test]
		public async Task The_fetch_url_hands_out_its_auth_token_once()
		{
			var (world, _, fetch, _, _) = await Cmi5LaunchAsync();

			var first = await world.Runtime.FetchCmi5TokenAsync(fetch);
			first.Succeeded.Should().BeTrue();
			((string)((JObject)first.Value)["auth-token"]).Should().NotBeNullOrEmpty();
			var second = await world.Runtime.FetchCmi5TokenAsync(fetch);
			second.Status.Should().Be(400);
			((string)((JObject)second.Value)["error-code"]).Should().Be("1");
			(await world.Runtime.FetchCmi5TokenAsync("unknown")).Status.Should().Be(401);
		}

		[Test]
		public async Task Statements_are_accepted_only_with_the_launchs_auth_token_actor_registration_and_session()
		{
			var (world, launch, fetch, _, _) = await Cmi5LaunchAsync();
			var auth = (string)((JObject)(await world.Runtime.FetchCmi5TokenAsync(fetch)).Value)["auth-token"];
			var session = world.Sessions.Rows.Single();
			var registration = launch.Registration.RegistrationKey;
			const string au = "https://example.test/au/1";
			string Body(JObject s) => s.ToString();

			var initialized = Statement(LmsCmi5Rules.Initialized, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member);
			(await world.Runtime.SaveCmi5StatementsAsync("wrong-token", Body(initialized), null)).Status.Should().Be(401);
			(await world.Runtime.SaveCmi5StatementsAsync(LmsStandardsWorld.TokenOf(launch.Url), Body(initialized), null)).Status.Should().Be(401, "the content token is not the LRS token");
			(await world.Runtime.SaveCmi5StatementsAsync(auth, Body(Statement(LmsCmi5Rules.Initialized, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member2)), null))
				.Status.Should().Be(403, "another member as the actor");
			(await world.Runtime.SaveCmi5StatementsAsync(auth, Body(Statement(LmsCmi5Rules.Initialized, au, Guid.NewGuid().ToString(), session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member)), null))
				.Status.Should().Be(403, "another registration");
			(await world.Runtime.SaveCmi5StatementsAsync(auth, Body(Statement(LmsCmi5Rules.Initialized, au, registration, Guid.NewGuid().ToString(), LmsWorld.DepartmentId, LmsWorld.Member)), null))
				.Status.Should().Be(403, "another session");
			(await world.Runtime.SaveCmi5StatementsAsync(auth, Body(Statement(LmsCmi5Rules.Satisfied, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member)), null))
				.Status.Should().Be(403, "satisfied is the LMS's own verb");
			(await world.Runtime.SaveCmi5StatementsAsync(auth, Body(Statement(LmsCmi5Rules.Completed, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member)), null))
				.Status.Should().Be(400, "completed needs result.completion");

			var accepted = await world.Runtime.SaveCmi5StatementsAsync(auth, Body(initialized), null);
			accepted.Succeeded.Should().BeTrue();
			((List<string>)accepted.Value).Should().ContainSingle();
			world.Statements.Rows.Count(s => s.VerbId == LmsCmi5Rules.Initialized).Should().Be(1);
		}

		[Test]
		public async Task Completed_and_passed_meet_move_on_and_complete_the_lesson_then_terminated_ends_the_session()
		{
			var (world, launch, fetch, lesson, enrollment) = await Cmi5LaunchAsync();
			var auth = (string)((JObject)(await world.Runtime.FetchCmi5TokenAsync(fetch)).Value)["auth-token"];
			var session = world.Sessions.Rows.Single();
			var registration = launch.Registration.RegistrationKey;
			const string au = "https://example.test/au/1";

			var batch = new JArray(
				Statement(LmsCmi5Rules.Initialized, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member),
				Statement(LmsCmi5Rules.Completed, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member, new JObject { ["completion"] = true }));
			var first = await world.Runtime.SaveCmi5StatementsAsync(auth, batch.ToString(), null);
			first.Succeeded.Should().BeTrue();
			first.LessonSatisfied.Should().BeFalse("moveOn is CompletedAndPassed");

			var passed = await world.Runtime.SaveCmi5StatementsAsync(auth, Statement(LmsCmi5Rules.Passed, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member,
				new JObject { ["success"] = true, ["score"] = new JObject { ["scaled"] = 0.9 } }).ToString(), null);
			passed.LessonSatisfied.Should().BeTrue();
			passed.CourseCompleted.Should().BeTrue();
			world.Registrations.Rows.Single().ScorePercent.Should().Be(90);
			world.Statements.Rows.Should().Contain(s => s.VerbId == LmsCmi5Rules.Satisfied);
			world.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId).Status.Should().Be((int)LmsEnrollmentStatuses.Completed);

			(await world.Runtime.SaveCmi5StatementsAsync(auth, Statement(LmsCmi5Rules.Terminated, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member).ToString(), null))
				.Succeeded.Should().BeTrue();
			world.Sessions.Rows.Single().EndedOn.Should().NotBeNull();
			(await world.Runtime.SaveCmi5StatementsAsync(auth, Statement(LmsCmi5Rules.Initialized, au, registration, session.SessionKey, LmsWorld.DepartmentId, LmsWorld.Member).ToString(), null))
				.Status.Should().Be(403, "nothing after terminated");
		}

		[Test]
		public async Task The_state_api_serves_launch_data_and_keeps_the_aus_own_documents()
		{
			var (world, launch, fetch, _, _) = await Cmi5LaunchAsync();
			var auth = (string)((JObject)(await world.Runtime.FetchCmi5TokenAsync(fetch)).Value)["auth-token"];
			var agent = LmsLearnerIdentity.AccountActor(LmsWorld.DepartmentId, LmsWorld.Member).ToString();
			const string au = "https://example.test/au/1";
			var registration = launch.Registration.RegistrationKey;

			var data = await world.Runtime.GetCmi5StateAsync(auth, au, LmsCmi5Rules.LaunchDataStateId, registration, agent);
			var launchData = JObject.Parse((string)data.Value);
			((string)launchData["moveOn"]).Should().Be("CompletedAndPassed");
			((decimal)launchData["masteryScore"]).Should().Be(0.8m);
			((string)launchData["contextTemplate"]["extensions"][LmsCmi5Rules.SessionIdExtension]).Should().Be(world.Sessions.Rows.Single().SessionKey);

			(await world.Runtime.SaveCmi5StateAsync(auth, au, LmsCmi5Rules.LaunchDataStateId, registration, agent, "application/json", "{}", false)).Status.Should().Be(403, "read-only");
			(await world.Runtime.SaveCmi5StateAsync(auth, au, "bookmark", registration, agent, "application/json", "{\"page\":2}", false)).Status.Should().Be(204);
			(await world.Runtime.SaveCmi5StateAsync(auth, au, "bookmark", registration, agent, "application/json", "{\"slide\":5}", true)).Status.Should().Be(204);
			JToken.DeepEquals(JObject.Parse((string)(await world.Runtime.GetCmi5StateAsync(auth, au, "bookmark", registration, agent)).Value), JObject.Parse("{\"page\":2,\"slide\":5}"))
				.Should().BeTrue("POST merges into the stored document");
			var otherAgent = LmsLearnerIdentity.AccountActor(LmsWorld.DepartmentId, LmsWorld.Member2).ToString();
			(await world.Runtime.GetCmi5StateAsync(auth, au, "bookmark", registration, otherAgent)).Status.Should().Be(403);
			(await world.Runtime.DeleteCmi5StateAsync(auth, au, "bookmark", registration, agent)).Status.Should().Be(204);
			(await world.Runtime.GetCmi5StateAsync(auth, au, "bookmark", registration, agent)).Status.Should().Be(404);
		}

		[Test]
		public void Move_on_rules_follow_cmi5()
		{
			var completedOnly = new LmsCmi5AuState { Completed = true };
			LmsCmi5Rules.IsSatisfied("Completed", completedOnly).Should().BeTrue();
			LmsCmi5Rules.IsSatisfied("Passed", completedOnly).Should().BeFalse();
			LmsCmi5Rules.IsSatisfied("CompletedOrPassed", completedOnly).Should().BeTrue();
			LmsCmi5Rules.IsSatisfied("CompletedAndPassed", completedOnly).Should().BeFalse();
			LmsCmi5Rules.IsSatisfied("NotApplicable", new LmsCmi5AuState()).Should().BeTrue();
		}

		#endregion cmi5
	
		#region Round 10 A7 (LMS seats)

		[Test]
		public async Task A_scorm_lesson_launches_and_completes_only_for_a_member_holding_an_lms_seat()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId);
			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test");
			launch.Succeeded.Should().BeTrue(launch.Error.ToString());

			world.Lms.SeatedMembers.Remove(LmsWorld.Member);

			(await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test")).Error
				.Should().Be(LmsErrors.NotLicensed);
			var outcome = await world.Runtime.CommitScormAsync(LmsStandardsWorld.TokenOf(launch.Url), new LmsScormCommit
			{
				Values = { ["cmi.core.lesson_status"] = "passed", ["cmi.core.score.raw"] = "92", ["cmi.core.session_time"] = "0000:05:00.00" },
				Finish = true
			});
			outcome.LessonSatisfied.Should().BeFalse("the runtime data is kept, the lesson stays open without a seat");
			outcome.CourseCompleted.Should().BeFalse();
			outcome.Error.Should().Be(LmsErrors.NotLicensed);
			world.Lms.Records.Live.Should().BeEmpty();
		}

		#endregion Round 10 A7 (LMS seats)

		#region Round 10 A7 follow-up (content tokens)

		[Test]
		public async Task A_launch_token_stops_serving_the_package_once_the_seat_is_removed_or_the_lapse_window_ends()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId);
			var launch = await world.Runtime.LaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, "https://site.test");
			launch.Succeeded.Should().BeTrue(launch.Error.ToString());
			var token = LmsStandardsWorld.TokenOf(launch.Url);
			var start = DateTime.UtcNow;
			world.Runtime.Clock = () => start;
			(await world.Runtime.ResolveContentAsync(token)).Should().NotBeNull();

			world.Lms.SeatedMembers.Remove(LmsWorld.Member);
			world.Runtime.Clock = () => start.AddMinutes(2);
			(await world.Runtime.ResolveContentAsync(token)).Should().BeNull("the token is re-checked against the player's rules at least once a minute");

			world.Lms.SeatedMembers.Add(LmsWorld.Member);
			(await world.Runtime.ResolveContentAsync(token)).Should().NotBeNull("a restored seat reopens the same session");

			// The add-on lapsed long enough ago that the undated course's 30-day window has passed.
			var stored = world.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId);
			stored.AssignedOn = DateTime.UtcNow.AddDays(-60);
			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-40) };
			world.Runtime.Clock = () => start.AddMinutes(4);
			(await world.Runtime.ResolveContentAsync(token)).Should().BeNull("an ended lapse window closes the package too");
		}

		#endregion Round 10 A7 follow-up (content tokens)
}
}
