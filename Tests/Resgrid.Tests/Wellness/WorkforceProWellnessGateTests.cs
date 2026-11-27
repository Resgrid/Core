using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// Owner decision 2026-11-26: all member wellness and workplace exposure is Workforce Pro. The gate is enforced inside the wellness
	/// services, so the web pages, the v4 API and worker 91 cannot bypass it:
	/// <list type="bullet">
	/// <item>the Workforce.Pro flag off refuses every read and write (the routes 404) while the data is kept, and the GDPR archive and retention
	/// erasure still run;</item>
	/// <item>offered but not licensed (never bought, cancelled or lapsed) is read-only: the member reads and exports their own records, officers
	/// read what their role opens, and nothing is created, edited or deleted; the worker builds no suggestions and sends no reminder or notice;</item>
	/// <item>licensed, a record about a member is created or edited only while that member holds a seat; an officer acting on a seated member
	/// needs no seat; a member whose seat was removed keeps read-only records.</item>
	/// </list>
	/// </summary>
	[TestFixture]
	public class WorkforceProWellnessGateTests
	{
		private const int DepartmentId = WellnessTestWorld.DepartmentId;
		private static readonly DateTime Now = DateTime.UtcNow;
		private bool _doNotBroadcast;

		[SetUp]
		public void Allow_broadcast()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
		}

		[TearDown]
		public void Restore_broadcast() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		private static ResponderExposureInput Entry(string userId = null) => new ResponderExposureInput
		{
			UserId = userId,
			Category = (int)ResponderExposureCategories.Carcinogen,
			OccurredOn = Now.AddDays(-1),
			Substance = "Diesel exhaust"
		};

		private static async Task<string> RefusalAsync(Func<Task> act)
		{
			var thrown = await FluentActions.Awaiting(act).Should().ThrowAsync<WorkforceProAccessException>();
			return thrown.Which.Code;
		}

		#region WL0: the exposure log

		[Test]
		public async Task With_the_flag_off_every_read_and_write_refuses_but_the_gdpr_archive_and_retention_still_run()
		{
			var world = new WellnessTestWorld();
			var entry = world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddYears(-31));
			world.WorkforcePro.SwitchOff();
			var service = world.Service();

			(await RefusalAsync(() => service.GetOverviewAsync(DepartmentId, WellnessTestWorld.Member, WellnessTestWorld.Member))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetEntryAsync(DepartmentId, WellnessTestWorld.Member, entry.RmsResponderExposureId))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetOwnEntriesForExportAsync(DepartmentId, WellnessTestWorld.Member))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetDepartmentSummaryAsync(DepartmentId, WellnessTestWorld.Officer, null, null))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.RecordAsync(DepartmentId, WellnessTestWorld.Member, Entry()))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetRosterAsync(DepartmentId, WellnessTestWorld.Admin))).Should().Be(WorkforceProCodes.Unavailable);

			// The member's data access right and the legal retention do not depend on the add-on.
			var archive = (System.Collections.IEnumerable)await service.BuildGdprExportAsync(WellnessTestWorld.Member, DepartmentId);
			archive.Cast<object>().Should().HaveCount(1);
			(await service.EraseExpiredAsync(Now)).Should().Be(1);
			world.Exposures.Should().BeEmpty();
		}

		[Test]
		public async Task Not_licensed_is_read_only_members_read_and_export_their_own_and_officers_read_the_report()
		{
			var world = new WellnessTestWorld();
			var entry = world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			var suggestion = world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Suggested, Now.AddDays(-2));
			world.WorkforcePro.Lapse();
			var service = world.Service();

			(await service.GetOverviewAsync(DepartmentId, WellnessTestWorld.Member, WellnessTestWorld.Member)).Entries.Should().ContainSingle();
			(await service.GetOwnEntriesForExportAsync(DepartmentId, WellnessTestWorld.Member)).Should().HaveCount(2);
			(await service.GetEntryAsync(DepartmentId, WellnessTestWorld.Officer, entry.RmsResponderExposureId)).Should().NotBeNull("an officer still reads what the role opens");
			(await service.GetDepartmentSummaryAsync(DepartmentId, WellnessTestWorld.Officer, null, null)).Should().ContainSingle();
			(await service.GetOverviewAsync(DepartmentId, WellnessTestWorld.Admin, WellnessTestWorld.Member).ContinueWith(t => t.IsFaulted && t.Exception.InnerException is UnauthorizedAccessException))
				.Should().BeTrue("read-only never widens who reads: administration alone still opens nothing");

			(await RefusalAsync(() => service.RecordAsync(DepartmentId, WellnessTestWorld.Member, Entry()))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.UpdateAsync(DepartmentId, WellnessTestWorld.Member, entry.RmsResponderExposureId, Entry(), null))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.ConfirmAsync(DepartmentId, WellnessTestWorld.Member, suggestion.RmsResponderExposureId, null))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.DismissAsync(DepartmentId, WellnessTestWorld.Member, suggestion.RmsResponderExposureId))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.VoidAsync(DepartmentId, WellnessTestWorld.Officer, entry.RmsResponderExposureId))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.SetRemindersAsync(DepartmentId, WellnessTestWorld.Member, entry.RmsResponderExposureId, true, false))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.SetScreeningFlagAsync(DepartmentId, WellnessTestWorld.Officer, WellnessTestWorld.Member, null, null, "Plan"))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.SaveSettingsAsync(DepartmentId, WellnessTestWorld.Admin, new WellnessSettings()))).Should().Be(WorkforceProCodes.Required);
			world.AddCall(700, CallStates.Closed, Now.AddHours(-2));
			(await RefusalAsync(() => service.FlagCallAsync(DepartmentId, WellnessTestWorld.Officer, 700, (int)ResponderExposureCategories.CriticalIncident))).Should().Be(WorkforceProCodes.Required);

			world.Exposures.Should().HaveCount(2, "nothing was created or deleted");
			entry.RowVersion.Should().Be(1, "nothing was edited");
			suggestion.Status.Should().Be((int)ResponderExposureStatuses.Suggested);
			world.Audits.Should().OnlyContain(a => a.Type == AuditLogTypes.ResponderExposuresExported, "only the member's own export was audited; nothing was written");
		}

		[Test]
		public async Task Licensed_records_are_created_only_for_seat_holders_and_an_officer_needs_no_seat_of_their_own()
		{
			var world = new WellnessTestWorld();
			world.WorkforcePro.OnlySeats(WellnessTestWorld.Member);
			var service = world.Service();

			var own = await service.RecordAsync(DepartmentId, WellnessTestWorld.Member, Entry());
			own.UserId.Should().Be(WellnessTestWorld.Member);
			var byOfficer = await service.RecordAsync(DepartmentId, WellnessTestWorld.Officer, Entry(WellnessTestWorld.Member));
			byOfficer.UserId.Should().Be(WellnessTestWorld.Member, "the officer acts with the role's permission, not a seat");

			(await RefusalAsync(() => service.RecordAsync(DepartmentId, WellnessTestWorld.Peer, Entry()))).Should().Be(WorkforceProCodes.SeatRequired);
			(await RefusalAsync(() => service.RecordAsync(DepartmentId, WellnessTestWorld.Officer, Entry(WellnessTestWorld.Peer)))).Should().Be(WorkforceProCodes.SeatRequired);
			(await RefusalAsync(() => service.SetScreeningFlagAsync(DepartmentId, WellnessTestWorld.Officer, WellnessTestWorld.Peer, null, null, null))).Should().Be(WorkforceProCodes.SeatRequired);
		}

		[Test]
		public async Task A_failed_renewal_inside_its_dunning_grace_keeps_records_editable_for_seat_holders()
		{
			var world = new WellnessTestWorld();
			world.WorkforcePro.OnlySeats(WellnessTestWorld.Member);
			world.WorkforcePro.FailRenewal();
			var service = world.Service();

			var own = await service.RecordAsync(DepartmentId, WellnessTestWorld.Member, Entry());
			own.UserId.Should().Be(WellnessTestWorld.Member, "the seven-day grace keeps Workforce Pro working while the payment is retried");
			(await RefusalAsync(() => service.RecordAsync(DepartmentId, WellnessTestWorld.Peer, Entry()))).Should().Be(WorkforceProCodes.SeatRequired);

			world.WorkforcePro.Lapse();
			(await RefusalAsync(() => world.Service().RecordAsync(DepartmentId, WellnessTestWorld.Member, Entry()))).Should().Be(WorkforceProCodes.Required, "after the grace it is read-only");
		}

		[Test]
		public async Task A_member_whose_seat_was_removed_keeps_read_only_records()
		{
			var world = new WellnessTestWorld();
			var entry = world.AddExposure(WellnessTestWorld.Peer, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-5));
			world.WorkforcePro.OnlySeats(WellnessTestWorld.Member);
			var service = world.Service();

			(await service.GetOverviewAsync(DepartmentId, WellnessTestWorld.Peer, WellnessTestWorld.Peer)).Entries.Should().ContainSingle();
			(await service.GetOwnEntriesForExportAsync(DepartmentId, WellnessTestWorld.Peer)).Should().ContainSingle();
			(await RefusalAsync(() => service.UpdateAsync(DepartmentId, WellnessTestWorld.Peer, entry.RmsResponderExposureId, Entry(), null))).Should().Be(WorkforceProCodes.SeatRequired);
			(await RefusalAsync(() => service.VoidAsync(DepartmentId, WellnessTestWorld.Officer, entry.RmsResponderExposureId))).Should().Be(WorkforceProCodes.SeatRequired);
			world.Exposures.Should().ContainSingle().Which.Status.Should().Be((int)ResponderExposureStatuses.Confirmed);
		}

		[Test]
		public async Task Not_licensed_the_worker_consumes_flags_without_suggestions_and_claims_reminders_without_sending()
		{
			var world = new WellnessTestWorld();
			world.AddCall(500, CallStates.Closed, Now.AddHours(-6));
			world.Statuses.Add(new ActionLog { UserId = WellnessTestWorld.Member, ActionTypeId = (int)ActionTypes.OnScene, Timestamp = Now.AddHours(-5), DepartmentId = DepartmentId });
			world.Flags.Add(new RmsCallExposureFlag { RmsCallExposureFlagId = "flag-1", DepartmentId = DepartmentId, CallId = 500, Category = (int)ResponderExposureCategories.CriticalIncident, FlaggedOn = Now.AddHours(-1), FlaggedByUserId = WellnessTestWorld.Officer });
			var today = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
			var anniversary = world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, new DateTime(2024, 11, 30, 20, 0, 0, DateTimeKind.Utc));
			anniversary.RemindMember = true;
			anniversary.RemindPeerTeam = true;
			anniversary.NextAnniversaryOn = new DateTime(2026, 11, 30);
			world.WorkforcePro.Lapse();

			var result = await WellnessLogic.Process(world.Service(), Now);

			result.FlagsProcessed.Should().Be(1);
			result.SuggestionsCreated.Should().Be(0);
			world.Flags.Single().SuggestionsGeneratedOn.Should().NotBeNull("the flag is consumed so it does not wait forever in the pending queue");
			world.Exposures.Should().ContainSingle("no suggestion was created");

			(await world.Service().SendAnniversaryRemindersAsync(today)).Should().Be(0);
			world.Sent.Should().BeEmpty("no notice goes out without Workforce Pro");
			anniversary.NextAnniversaryOn.Should().Be(new DateTime(2027, 11, 30), "claimed, so nothing bursts out when the add-on comes back");
		}

		[Test]
		public async Task Licensed_the_worker_suggests_and_reminds_only_for_seat_holders()
		{
			var world = new WellnessTestWorld();
			world.AddCall(500, CallStates.Closed, Now.AddHours(-6));
			world.Statuses.Add(new ActionLog { UserId = WellnessTestWorld.Member, ActionTypeId = (int)ActionTypes.OnScene, Timestamp = Now.AddHours(-5), DepartmentId = DepartmentId });
			world.Statuses.Add(new ActionLog { UserId = WellnessTestWorld.Peer, ActionTypeId = (int)ActionTypes.OnScene, Timestamp = Now.AddHours(-5), DepartmentId = DepartmentId });
			world.Flags.Add(new RmsCallExposureFlag { RmsCallExposureFlagId = "flag-1", DepartmentId = DepartmentId, CallId = 500, Category = (int)ResponderExposureCategories.CriticalIncident, FlaggedOn = Now.AddHours(-1), FlaggedByUserId = WellnessTestWorld.Officer });
			var today = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
			var seated = world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, new DateTime(2024, 11, 30, 20, 0, 0, DateTimeKind.Utc));
			seated.RemindMember = true;
			seated.NextAnniversaryOn = new DateTime(2026, 11, 30);
			var unseated = world.AddExposure(WellnessTestWorld.Peer, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, new DateTime(2024, 11, 29, 20, 0, 0, DateTimeKind.Utc));
			unseated.RemindMember = true;
			unseated.NextAnniversaryOn = new DateTime(2026, 11, 29);
			world.WorkforcePro.OnlySeats(WellnessTestWorld.Member);

			var result = await world.Service().ProcessPendingFlagsAsync(Now);
			result.SuggestionsCreated.Should().Be(1);
			world.Exposures.Where(e => e.Status == (int)ResponderExposureStatuses.Suggested).Select(e => e.UserId).Should().Equal(WellnessTestWorld.Member);

			world.Sent.Clear();
			(await world.Service().SendAnniversaryRemindersAsync(today)).Should().Be(1);
			world.Sent.Select(s => s.UserId).Should().Equal(WellnessTestWorld.Member);
		}

		[Test]
		public async Task The_worker_asks_the_gate_once_per_department_with_due_work()
		{
			var world = new WellnessTestWorld();
			world.AddCall(500, CallStates.Closed, Now.AddHours(-6));
			world.AddCall(501, CallStates.Closed, Now.AddHours(-6));
			world.Flags.Add(new RmsCallExposureFlag { RmsCallExposureFlagId = "flag-1", DepartmentId = DepartmentId, CallId = 500, Category = 1, FlaggedOn = Now.AddHours(-1), FlaggedByUserId = WellnessTestWorld.Officer });
			world.Flags.Add(new RmsCallExposureFlag { RmsCallExposureFlagId = "flag-2", DepartmentId = DepartmentId, CallId = 501, Category = 1, FlaggedOn = Now.AddHours(-1), FlaggedByUserId = WellnessTestWorld.Officer });

			await world.Service().ProcessPendingFlagsAsync(Now);

			world.WorkforcePro.Asked.Keys.Should().Equal(DepartmentId);
			world.WorkforcePro.Asked[DepartmentId].Should().BeLessThanOrEqualTo(3, "the pass keeps one answer per department; building suggestions reuses it");
		}

		#endregion WL0

		#region v4 API

		private static Resgrid.Web.Services.Controllers.v4.WellnessController Api(WellnessTestWorld world, string userId)
		{
			var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
			{
				User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
				{
					new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.PrimarySid, userId),
					new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "Test"))
			};
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http };
			return new Resgrid.Web.Services.Controllers.v4.WellnessController(world.Service(), world.WorkforcePro) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[Test, NonParallelizable]
		public async Task The_v4_api_answers_404_with_the_flag_off_and_refuses_writes_with_the_code_while_not_licensed()
		{
			var previous = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor;
			using var activity = new System.Diagnostics.Activity("WorkforceProWellnessGateTests").Start();
			try
			{
				var world = new WellnessTestWorld();
				world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-2));
				world.WorkforcePro.SwitchOff();
				(await Api(world, WellnessTestWorld.Member).GetMyExposures()).Result.Should().BeOfType<NotFoundResult>();
				(await Api(world, WellnessTestWorld.Member).CreateExposure(new Resgrid.Web.Services.Models.v4.Wellness.CreateExposureInput { Category = 1, OccurredOn = Now }, default))
					.Result.Should().BeOfType<NotFoundResult>();

				world.WorkforcePro.Offered = true;
				world.WorkforcePro.Lapse();
				(await Api(world, WellnessTestWorld.Member).GetMyExposures()).Result.Should().BeOfType<OkObjectResult>("the member keeps reading their own records");
				var refused = (await Api(world, WellnessTestWorld.Member).CreateExposure(new Resgrid.Web.Services.Models.v4.Wellness.CreateExposureInput { Category = 1, OccurredOn = Now.AddHours(-1) }, default))
					.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
				((Resgrid.Web.Services.Models.v4.Wellness.ExposureEntryResult)refused.Value).Error.Should().Be(WorkforceProCodes.Required);
				world.Exposures.Should().ContainSingle();
			}
			finally
			{
				Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = previous;
			}
		}

		#endregion v4 API
	}
}
