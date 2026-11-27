using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Model;
using static Resgrid.Tests.Wellness.MemberHealthTestWorld;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// Workforce Pro (2026-11-26) on WL1: exposure cases, injuries, duty status, medical clearances, the OSHA log and the due notices. Flag off
	/// refuses everything; not licensed is read-only (the member's own records, the officers' lists and the OSHA export stay readable) with no
	/// notices; licensed, changes are made only for members holding a seat, by officers who need no seat themselves.
	/// </summary>
	[TestFixture]
	public class WorkforceProMemberHealthGateTests
	{
		private static readonly DateTime Today = DateTime.UtcNow.Date;
		private bool _doNotBroadcast;

		[SetUp]
		public void Allow_broadcast()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
		}

		[TearDown]
		public void Restore_broadcast() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		private static async Task<string> RefusalAsync(Func<Task> act) =>
			(await FluentActions.Awaiting(act).Should().ThrowAsync<WorkforceProAccessException>()).Which.Code;

		[Test]
		public async Task With_the_flag_off_every_read_and_write_refuses_and_the_retention_still_runs()
		{
			var world = new MemberHealthTestWorld();
			var injury = world.AddInjury(Member, new DateTime(2015, 6, 1));
			world.WorkforcePro.SwitchOff();
			var service = world.Service();

			(await RefusalAsync(() => service.GetInjuriesForMemberAsync(DepartmentId, Member, Member))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetDutyBoardAsync(DepartmentId, Admin))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetDutyStatusAsync(DepartmentId, Member, Member))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.GetCasesForMemberAsync(DepartmentId, Member, Member))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.BuildOshaLogAsync(DepartmentId, WellnessOfficer, Today.Year, id => id))).Should().Be(WorkforceProCodes.Unavailable);
			(await RefusalAsync(() => service.ReportInjuryAsync(DepartmentId, Member, new MemberInjuryInput { OccurredOn = Today }))).Should().Be(WorkforceProCodes.Unavailable);

			(await service.EraseExpiredAsync(DateTime.UtcNow)).Should().Be(1, "retention is a legal duty, not a Workforce Pro feature");
			world.Injuries.Should().NotContain(injury);
		}

		[Test]
		public async Task Not_licensed_keeps_records_readable_and_exportable_and_refuses_every_change()
		{
			var world = new MemberHealthTestWorld();
			var injury = world.AddInjury(Member, Today.AddDays(-5), MemberDutyStatuses.OffDuty);
			var incident = world.AddIncident(Member, ExposureIncidentStages.Reported, Today.AddDays(-4));
			world.WorkforcePro.Lapse();
			var service = world.Service();

			(await service.GetInjuriesForMemberAsync(DepartmentId, Member, Member)).Should().ContainSingle();
			(await service.GetCasesForMemberAsync(DepartmentId, Member, Member)).Should().ContainSingle();
			(await service.GetDutyStatusAsync(DepartmentId, Member, Member)).Should().NotBeNull();
			(await service.GetOpenInjuriesAsync(DepartmentId, WellnessOfficer)).Should().ContainSingle();
			(await service.BuildOshaLogAsync(DepartmentId, WellnessOfficer, Today.AddDays(-5).Year, id => id)).Should().NotBeNull("the OSHA log stays exportable");

			(await RefusalAsync(() => service.ReportInjuryAsync(DepartmentId, Member, new MemberInjuryInput { OccurredOn = Today }))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.ReportExposureAsync(DepartmentId, Member, new ExposureIncidentReport { Route = (int)ExposureIncidentRoutes.Percutaneous, OccurredOn = Today.AddHours(-3) })))
				.Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.SetDutyStatusAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId, (int)MemberDutyStatuses.RestrictedDuty, Today, null, null)))
				.Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.ClearReturnToDutyAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId, Today, null))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.VoidInjuryAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.SourceFollowUpRequested }))).Should().Be(WorkforceProCodes.Required);
			(await RefusalAsync(() => service.RecordClearanceAsync(DepartmentId, WellnessOfficer, new MedicalClearanceInput { UserId = Member, Status = 1, EvaluatedOn = Today })))
				.Should().Be(WorkforceProCodes.Required);

			world.Injuries.Should().ContainSingle().Which.DutyStatus.Should().Be((int)MemberDutyStatuses.OffDuty, "nothing changed");
			world.Exposures.Should().BeEmpty("no entry was logged");
			world.Sent.Should().BeEmpty();
		}

		[Test]
		public async Task Licensed_officers_change_records_only_of_members_holding_a_seat()
		{
			var world = new MemberHealthTestWorld();
			world.WorkforcePro.OnlySeats(Member);
			var service = world.Service();

			var reported = await service.ReportInjuryAsync(DepartmentId, WellnessOfficer, new MemberInjuryInput { UserId = Member, OccurredOn = Today.AddDays(-1) });
			reported.UserId.Should().Be(Member, "the health officer needs the role, not a seat");
			(await RefusalAsync(() => service.ReportInjuryAsync(DepartmentId, WellnessOfficer, new MemberInjuryInput { UserId = Other, OccurredOn = Today.AddDays(-1) })))
				.Should().Be(WorkforceProCodes.SeatRequired);
			(await RefusalAsync(() => service.ReportExposureAsync(DepartmentId, Other, new ExposureIncidentReport { Route = (int)ExposureIncidentRoutes.Percutaneous, OccurredOn = Today.AddHours(-3) })))
				.Should().Be(WorkforceProCodes.SeatRequired);

			var unseated = world.AddInjury(Other, Today.AddDays(-2), MemberDutyStatuses.OffDuty);
			(await RefusalAsync(() => service.SetDutyStatusAsync(DepartmentId, WellnessOfficer, unseated.RmsMemberInjuryId, (int)MemberDutyStatuses.RestrictedDuty, Today, null, null)))
				.Should().Be(WorkforceProCodes.SeatRequired);
			(await service.GetInjuriesForMemberAsync(DepartmentId, Other, Other)).Should().ContainSingle("a member without a seat keeps reading their own records");
		}

		[Test]
		public async Task Not_licensed_or_without_a_seat_no_due_notice_is_sent_and_the_claims_still_move()
		{
			var world = new MemberHealthTestWorld();
			var now = DateTime.UtcNow;
			var overdue = world.AddIncident(Member, ExposureIncidentStages.NotificationRequested, now.AddDays(-4));
			overdue.NotificationRequestedOn = now.AddDays(-3);
			overdue.NotificationDueOn = now.AddDays(-1);
			var review = world.AddInjury(Other, now.AddDays(-10), MemberDutyStatuses.OffDuty);
			review.ExpectedReturnOn = now.Date.AddDays(-1);
			world.WorkforcePro.Lapse();

			(await world.Service().SendDueNoticesAsync(now)).Should().Be(0);
			world.Sent.Should().BeEmpty();
			overdue.NotificationOverdueNoticeOn.Should().NotBeNull("claimed then skipped");

			// Licensed again, but the injured member holds no seat: still no notice about them.
			var world2 = new MemberHealthTestWorld();
			var review2 = world2.AddInjury(Other, now.AddDays(-10), MemberDutyStatuses.OffDuty);
			review2.ExpectedReturnOn = now.Date.AddDays(-1);
			world2.WorkforcePro.OnlySeats(Member);
			(await world2.Service().SendDueNoticesAsync(now)).Should().Be(0);
			world2.Sent.Should().BeEmpty();
		}

		[Test, NonParallelizable]
		public async Task The_v4_api_answers_404_with_the_flag_off_and_400_with_the_code_while_not_licensed()
		{
			var previous = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor;
			using var activity = new System.Diagnostics.Activity("WorkforceProMemberHealthGateTests").Start();
			try
			{
				var world = new MemberHealthTestWorld();
				world.AddInjury(Member, Today.AddDays(-5));
				var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
				{
					User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
					{
						new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.PrimarySid, Member),
						new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
					}, "test"))
				};
				Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http };
				Resgrid.Web.Services.Controllers.v4.MemberHealthController Api() =>
					new Resgrid.Web.Services.Controllers.v4.MemberHealthController(world.Service(), world.WorkforcePro) { ControllerContext = new ControllerContext { HttpContext = http } };

				world.WorkforcePro.SwitchOff();
				(await Api().GetMyHealth()).Result.Should().BeOfType<NotFoundResult>();
				(await Api().GetDutyBoard()).Result.Should().BeOfType<NotFoundResult>();

				world.WorkforcePro.Offered = true;
				world.WorkforcePro.Lapse();
				(await Api().GetMyInjuries()).Result.Should().BeOfType<OkObjectResult>();
				var refused = (await Api().ReportInjury(new Resgrid.Web.Services.Models.v4.Wellness.ReportInjuryInput { OccurredOn = Today.AddHours(-2) }, default))
					.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
				((Resgrid.Web.Services.Models.v4.Wellness.InjuryResult)refused.Value).Error.Should().Be(WorkforceProCodes.Required);
				world.Injuries.Should().ContainSingle();
			}
			finally
			{
				Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = previous;
			}
		}
	}
}
