using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>T5 item 3 xAPI out: the outbox (actor = department-scoped account unless opted in), worker 98's delivery with retry and backoff.</summary>
	[TestFixture]
	public class LmsXapiTests
	{
		private static async Task<(LmsStandardsWorld World, LmsCourse Course)> WorldWithCourseAsync()
		{
			var world = new LmsStandardsWorld();
			var (course, _, _) = await world.Lms.PublishedCourseAsync("Ventilation", LmsWorld.Reading("Read"));
			return (world, course);
		}

		private static LmsXapiEvent Completed(LmsCourse course, string userId = LmsWorld.Member) => new LmsXapiEvent
		{
			DepartmentId = LmsWorld.DepartmentId, UserId = userId, VerbId = LmsXapiVerbs.Completed, LmsCourseId = course.LmsCourseId, Completion = true, OccurredOn = DateTime.UtcNow
		};

		[Test]
		public async Task Nothing_is_queued_while_xapi_is_off_or_has_no_endpoint()
		{
			var (world, course) = await WorldWithCourseAsync();
			await world.Xapi.EnqueueAsync(Completed(course));
			world.Outbox.Rows.Should().BeEmpty();

			world.StandardsSetting = new DepartmentSetting { Setting = ObjectSerialization.Serialize(new LmsStandardsSettings { XapiEnabled = true, LrsEndpoint = "http://insecure.test/xapi" }) };
			await world.Xapi.EnqueueAsync(Completed(course));
			world.Outbox.Rows.Should().BeEmpty("an http endpoint is not deliverable");
		}

		[Test]
		public async Task The_actor_is_the_department_scoped_account_and_never_the_email_by_default()
		{
			var (world, course) = await WorldWithCourseAsync();
			world.EnableXapi();
			await world.Xapi.EnqueueAsync(Completed(course));

			var statement = JObject.Parse(world.Outbox.Rows.Single().StatementJson);
			statement["actor"]["mbox"].Should().BeNull();
			((string)statement["actor"]["account"]["name"]).Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member));
			((string)statement["actor"]["account"]["name"]).Should().NotContain(LmsWorld.Member);
			((string)statement["actor"]["account"]["homePage"]).Should().Be(LmsLearnerIdentity.HomePage(LmsWorld.DepartmentId));
			statement.ToString().Should().NotContain("@example.test");
			((string)statement["verb"]["id"]).Should().Be(LmsXapiVerbs.Completed);
			((string)statement["object"]["definition"]["name"]["en-US"]).Should().Be("Ventilation");
			((bool)statement["result"]["completion"]).Should().BeTrue();
			Guid.TryParse((string)statement["id"], out _).Should().BeTrue();
		}

		[Test]
		public async Task The_email_is_the_actor_only_when_the_department_opted_in()
		{
			var (world, course) = await WorldWithCourseAsync();
			world.EnableXapi(shareEmail: true);
			await world.Xapi.EnqueueAsync(Completed(course));

			var statement = JObject.Parse(world.Outbox.Rows.Single().StatementJson);
			((string)statement["actor"]["mbox"]).Should().Be("mailto:" + LmsWorld.Member + "@example.test");
			statement["actor"]["account"].Should().BeNull();
		}

		[Test]
		public async Task Academy_courses_and_other_departments_courses_are_not_reported()
		{
			var (world, _) = await WorldWithCourseAsync();
			world.EnableXapi();
			var academy = new LmsCourse { DepartmentId = 0, PublisherKind = (int)LmsPublisherKinds.System, Title = "Academy" };
			await world.Lms.Courses.InsertAsync(academy, default);
			await world.Xapi.EnqueueAsync(Completed(academy));
			world.Outbox.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Course_completion_and_assessment_results_reach_the_queue_from_the_learner_services()
		{
			var world = new LmsStandardsWorld();
			var (course, _, lessons) = await world.Lms.PublishedCourseAsync("Ladders", LmsWorld.Reading("Read"));
			var enrolled = await world.Lms.Learner.EnrollSelfAsync(world.Lms.Actor(LmsWorld.Member), course.LmsCourseId);
			await world.Lms.Learner.CompleteLessonAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrolled.Enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, new LmsLessonCompletionInput());

			world.Lms.Xapi.Verify(x => x.EnqueueAsync(It.Is<LmsXapiEvent>(e => e.VerbId == LmsXapiVerbs.Completed && e.LmsCourseId == course.LmsCourseId && e.UserId == LmsWorld.Member),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		private static LmsXapiOutboxItem Item(int id, int department = LmsWorld.DepartmentId) => new LmsXapiOutboxItem
		{
			LmsXapiOutboxItemId = id, DepartmentId = department, StatementId = Guid.NewGuid().ToString(), UserId = LmsWorld.Member, VerbId = LmsXapiVerbs.Completed,
			StatementJson = "{\"id\":\"" + id + "\"}", Status = (int)LmsXapiOutboxStatuses.Pending, NextAttemptOn = DateTime.UtcNow.AddMinutes(-1), CreatedOn = DateTime.UtcNow.AddMinutes(-2)
		};

		[Test]
		public async Task Delivery_posts_a_batch_with_basic_auth_and_marks_it_delivered()
		{
			var world = new LmsStandardsWorld();
			world.EnableXapi();
			await world.Outbox.InsertAsync(Item(1), default);
			await world.Outbox.InsertAsync(Item(2), default);
			string url = null, body = null;
			IDictionary<string, string> headers = null;
			world.Transport.Setup(t => t.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
				.Callback((string u, string b, IDictionary<string, string> h, CancellationToken c) => { url = u; body = b; headers = h; })
				.ReturnsAsync(new LmsHttpResult { StatusCode = 200 });

			(await world.Xapi.DeliverDueAsync(DateTime.UtcNow)).Should().Be(2);

			url.Should().Be("https://lrs.example.test/xapi/statements");
			JArray.Parse(body).Should().HaveCount(2);
			headers["X-Experience-API-Version"].Should().Be("1.0.3");
			headers["Authorization"].Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("key:s3cret")), "the secret is decrypted only to send");
			world.Outbox.Rows.Should().OnlyContain(i => i.Status == (int)LmsXapiOutboxStatuses.Delivered && i.DeliveredOn != null);
		}

		[Test]
		public async Task A_server_error_retries_with_backoff_and_gives_up_after_the_last_attempt()
		{
			var world = new LmsStandardsWorld();
			world.EnableXapi();
			await world.Outbox.InsertAsync(Item(1), default);
			world.Transport.Setup(t => t.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsHttpResult { StatusCode = 503 });

			var now = DateTime.UtcNow;
			(await world.Xapi.DeliverDueAsync(now)).Should().Be(0);
			var row = world.Outbox.Rows.Single();
			row.Status.Should().Be((int)LmsXapiOutboxStatuses.Pending);
			row.Attempts.Should().Be(1);
			row.NextAttemptOn.Should().BeAfter(now).And.BeOnOrBefore(now.AddMinutes(1.25));
			row.LastError.Should().Be("HTTP 503");

			(await world.Xapi.DeliverDueAsync(now)).Should().Be(0);
			world.Outbox.Rows.Single().Attempts.Should().Be(1, "not due yet");

			row.Attempts = Resgrid.Config.LmsStandardsConfig.XapiMaxAttempts - 1;
			row.NextAttemptOn = now;
			await world.Xapi.DeliverDueAsync(now);
			world.Outbox.Rows.Single().Status.Should().Be((int)LmsXapiOutboxStatuses.Failed, "out of attempts");

			(await world.Xapi.RetryFailedAsync(world.Lms.AdminActor)).Count.Should().Be(1);
			world.Outbox.Rows.Single().Status.Should().Be((int)LmsXapiOutboxStatuses.Pending);
		}

		[Test]
		public async Task A_bad_request_fails_at_once_and_a_conflict_counts_as_delivered()
		{
			var world = new LmsStandardsWorld();
			world.EnableXapi();
			await world.Outbox.InsertAsync(Item(1), default);
			world.Transport.Setup(t => t.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsHttpResult { StatusCode = 400 });
			await world.Xapi.DeliverDueAsync(DateTime.UtcNow);
			world.Outbox.Rows.Single().Status.Should().Be((int)LmsXapiOutboxStatuses.Failed);

			LmsXapiStatements.Classify(409).Should().Be((true, false));
			LmsXapiStatements.Classify(0).Should().Be((false, true), "network errors retry");
			LmsXapiStatements.Classify(401).Should().Be((false, true), "credentials being fixed");
			LmsXapiStatements.Classify(413).Should().Be((false, false));
		}

		[Test]
		public async Task Statements_queued_before_xapi_was_turned_off_are_never_sent()
		{
			var world = new LmsStandardsWorld();
			await world.Outbox.InsertAsync(Item(1), default);
			await world.Xapi.DeliverDueAsync(DateTime.UtcNow);
			world.Transport.Verify(t => t.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
			world.Outbox.Rows.Single().Status.Should().Be((int)LmsXapiOutboxStatuses.Failed);
			world.Outbox.Rows.Single().LastError.Should().Be("xapi_disabled");
		}

		[Test]
		public void Backoff_doubles_from_a_minute_and_is_capped()
		{
			var fixedRandom = new Random(1);
			LmsXapiStatements.Backoff(1, null).Should().Be(TimeSpan.FromMinutes(1));
			LmsXapiStatements.Backoff(4, null).Should().Be(TimeSpan.FromMinutes(8));
			LmsXapiStatements.Backoff(30, fixedRandom).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(Resgrid.Config.LmsStandardsConfig.XapiMaxBackoffMinutes));
			LmsXapiService.StatementsUrl("https://lrs.test/xapi/").Should().Be("https://lrs.test/xapi/statements");
			LmsXapiService.StatementsUrl("https://lrs.test/xapi/statements").Should().Be("https://lrs.test/xapi/statements");
			LmsXapiService.StatementsUrl("https://lrs.test/xapi?tenant=1").Should().Be("https://lrs.test/xapi/statements?tenant=1");
		}

		[Test]
		public async Task Settings_store_the_secret_encrypted_never_echo_it_and_need_a_manager_with_the_add_on()
		{
			var world = new LmsStandardsWorld();
			var settings = new LmsStandardsSettings { XapiEnabled = true, LrsEndpoint = "https://lrs.example.test/xapi/", LrsKey = "k" };

			(await world.Xapi.SaveSettingsAsync(world.Lms.Actor(LmsWorld.Member), settings, "pw", false)).Error.Should().Be(LmsErrors.NotAuthorized);
			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow };
			(await world.Xapi.SaveSettingsAsync(world.Lms.AdminActor, settings, "pw", false)).Error.Should().Be(LmsErrors.AddonRequired);
			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = true, LastEntitledOn = DateTime.UtcNow };
			(await world.Xapi.SaveSettingsAsync(world.Lms.AdminActor, new LmsStandardsSettings { XapiEnabled = true, LrsEndpoint = "http://plain.test" }, null, false)).Error
				.Should().Be(LmsErrors.InvalidInput);

			(await world.Xapi.SaveSettingsAsync(world.Lms.AdminActor, settings, "pw", false)).Succeeded.Should().BeTrue();
			var stored = await world.Xapi.GetSettingsAsync(LmsWorld.DepartmentId);
			stored.LrsSecretEncrypted.Should().NotBe("pw").And.StartWith("enc:");
			(await world.Xapi.GetOverviewAsync(LmsWorld.DepartmentId)).HasSecret.Should().BeTrue();

			(await world.Xapi.SaveSettingsAsync(world.Lms.AdminActor, settings, null, false)).Succeeded.Should().BeTrue();
			(await world.Xapi.GetSettingsAsync(LmsWorld.DepartmentId)).LrsSecretEncrypted.Should().Be(stored.LrsSecretEncrypted, "an empty secret keeps the stored one");
			world.Events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.LmsStandardsSettingsChanged && !a.After.Contains("enc:"))), Times.AtLeastOnce);

			var roundTrip = ObjectSerialization.Deserialize<LmsStandardsSettings>(ObjectSerialization.Serialize(new LmsStandardsSettings { XapiEnabled = false, ShareEmailInActor = true }));
			roundTrip.XapiEnabled.Should().BeFalse();
			roundTrip.ShareEmailInActor.Should().BeTrue();
		}

		[Test]
		public async Task Worker_98_runs_every_step_even_when_one_fails()
		{
			var packages = new Mock<ILmsContentPackageService>();
			packages.Setup(p => p.ProcessQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("store down"));
			var xapi = new Mock<ILmsXapiService>();
			xapi.Setup(x => x.DeliverDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);
			xapi.Setup(x => x.PurgeDeliveredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
			var runtime = new Mock<ILmsStandardsRuntimeService>();
			runtime.Setup(r => r.PurgeExpiredSessionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
			var lti = new Mock<ILmsLtiService>();

			var result = await LmsStandardsLogic.Process(packages.Object, xapi.Object, runtime.Object, lti.Object, DateTime.UtcNow);

			result.Failures.Should().Be(1);
			result.StatementsDelivered.Should().Be(3);
			result.Purged.Should().Be(3);
			lti.Verify(l => l.PurgeExpiredTokensAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
