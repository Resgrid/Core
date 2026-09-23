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
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// What a member removal ends (docs/architecture/inactive-member-automation.md): admin standing, seats on open
	/// deployments (billable time-report prefill) and open workforce employment (current MARS and pay-data counts).
	/// </summary>
	[TestFixture]
	public class MemberRemovalLifecycleTests
	{
		private const int Dept = 41;

		#region Admin standing

		private static DepartmentsService Departments(Mock<IDepartmentMembersRepository> members, List<AuditEvent> audits, Mock<ILimitsService> limits = null)
		{
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage<AuditEvent>(It.IsAny<AuditEvent>())).Callback<AuditEvent>(audits.Add);
			return new DepartmentsService(Mock.Of<IDepartmentsRepository>(), members.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentCallEmailsRepository>(),
				Mock.Of<IDepartmentCallPruningRepository>(), Mock.Of<ICacheProvider>(), Mock.Of<IUsersService>(), Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IUserProfileService>(), (limits ?? new Mock<ILimitsService>()).Object, events.Object, Mock.Of<IIdentityRepository>(), Mock.Of<IDepartmentCallPruningRepository>());
		}

		private static Mock<IDepartmentMembersRepository> Members(DepartmentMember row)
		{
			var members = new Mock<IDepartmentMembersRepository>();
			members.Setup(m => m.GetDepartmentMemberByDepartmentIdAndUserIdAsync(Dept, row.UserId)).ReturnsAsync(() => row);
			members.Setup(m => m.SaveOrUpdateAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentMember m, CancellationToken _, bool __) => m);
			return members;
		}

		[Test]
		public async Task Removal_clears_admin_standing_and_the_removal_audit_records_it()
		{
			var row = new DepartmentMember { DepartmentMemberId = 7, DepartmentId = Dept, UserId = "former-admin", IsAdmin = true };
			var audits = new List<AuditEvent>();
			var removed = await Departments(Members(row), audits).DeleteUserAsync(Dept, "former-admin", "chief");

			removed.IsDeleted.Should().BeTrue();
			removed.IsAdmin.Should().BeFalse("a removed member is not an admin of anything");
			var audit = audits.Should().ContainSingle(a => a.Type == AuditLogTypes.UserRemoved).Subject;
			JObject.Parse(audit.Before).Value<bool?>("IsAdmin").Should().BeTrue();
			JObject.Parse(audit.After).Value<bool?>("IsAdmin").Should().BeFalse();
		}

		[Test]
		public async Task Reactivation_returns_a_regular_member_even_from_a_row_removed_before_admin_was_cleared_and_is_audited()
		{
			// A row removed before removal cleared IsAdmin, then hidden and disabled on the way out.
			var row = new DepartmentMember { DepartmentMemberId = 8, DepartmentId = Dept, UserId = "returning", IsAdmin = true, IsDeleted = true, IsDisabled = true, IsHidden = true };
			var audits = new List<AuditEvent>();
			var service = Departments(Members(row), audits);

			var member = await service.ReactivateUserAsync(Dept, "returning", "chief");

			member.IsDeleted.Should().BeFalse(); member.IsDisabled.Should().BeFalse(); member.IsHidden.Should().BeFalse();
			member.IsAdmin.Should().BeFalse("admin standing is granted again on purpose, never restored from the removed row");
			var audit = audits.Should().ContainSingle(a => a.Type == AuditLogTypes.UserReactivated).Subject;
			audit.UserId.Should().Be("chief"); audit.DepartmentId.Should().Be(Dept);
			JObject.Parse(audit.Before).Value<bool?>("IsAdmin").Should().BeTrue();
			JObject.Parse(audit.After).Value<bool?>("IsAdmin").Should().BeFalse();

			(await service.ReactivateUserAsync(Dept, "nobody", "chief")).Should().BeNull("there is no membership row to bring back");
		}

		[Test]
		public async Task Removal_and_reactivation_refresh_the_cached_plan_counts()
		{
			// Both change how many personnel seats are used; the 14-day cached counts drive the Personnel index's Add button.
			// (Removal clears them through InvalidateAllDepartmentsCache.)
			var row = new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "seat-holder" };
			var limits = new Mock<ILimitsService>();
			var service = Departments(Members(row), new List<AuditEvent>(), limits);

			await service.DeleteUserAsync(Dept, "seat-holder", "chief");
			limits.Verify(l => l.InvalidateDepartmentsEntityLimitsCache(Dept), Times.Once);

			await service.ReactivateUserAsync(Dept, "seat-holder", "chief");
			limits.Verify(l => l.InvalidateDepartmentsEntityLimitsCache(Dept), Times.Exactly(2));
		}

		#endregion

		#region Deployment seats and employment

		private sealed class Removal
		{
			public readonly List<string> Calls = new List<string>();
			public readonly List<DeploymentPersonnel> Seats = new List<DeploymentPersonnel>();
			public readonly Mock<IDeploymentService> Deployments = new Mock<IDeploymentService>();
			public readonly Mock<IWorkforceService> Workforce = new Mock<IWorkforceService>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public DeleteService Service;
		}

		private static Removal Build()
		{
			var r = new Removal();
			var seats = new Mock<IDeploymentPersonnelRepository>();
			seats.Setup(s => s.GetForUserAsync(Dept, "leaver")).ReturnsAsync(() => r.Seats.ToList());
			r.Deployments.Setup(d => d.RemovePersonnelAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, int _, string actor, string __, string ___, CancellationToken ____) =>
				{
					if (id == "seat-closed") throw new InvalidOperationException("deployments_closed");
					r.Calls.Add("seat:" + id + ":" + actor); return true;
				});
			r.Workforce.Setup(w => w.EndEmploymentsForMemberAsync(Dept, "leaver", It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, string __, DateTime day, string actor, CancellationToken ___) => { r.Calls.Add("employment:" + day.ToString("yyyy-MM-dd") + ":" + actor); return 1; });
			r.Departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, TimeZone = "UTC" });
			r.Departments.Setup(d => d.DeleteUserAsync(Dept, "leaver", "chief", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => { r.Calls.Add("membership"); return null; });
			r.Departments.Setup(d => d.GetAllDepartmentsForUserAsync("leaver")).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = Dept, UserId = "leaver" }, new DepartmentMember { DepartmentId = Dept + 1, UserId = "leaver" }
			});
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserDeleteUserAsync(Dept, "chief", "leaver")).ReturnsAsync(true);
			r.Service = new DeleteService(authorization.Object, r.Departments.Object, Mock.Of<ICallsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IMessageService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IWorkLogsService>(), Mock.Of<IUserStateService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IDistributionListsService>(),
				Mock.Of<IShiftsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICertificationService>(), Mock.Of<ILogService>(), Mock.Of<IInventoryService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<IAddressService>(), Mock.Of<IQueueService>(), Mock.Of<IEmailService>(), Mock.Of<IDeleteRepository>(), Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(),
				Mock.Of<IUserSessionService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>(),
				deploymentService: r.Deployments.Object, deploymentPersonnel: seats.Object, workforceService: r.Workforce.Object);
			return r;
		}

		[Test]
		public async Task Removing_a_member_releases_their_open_deployment_seats_and_ends_their_employment_before_the_membership_goes()
		{
			var r = Build();
			r.Seats.Add(new DeploymentPersonnel { DeploymentPersonnelId = "seat-open", DeploymentId = "open", DepartmentId = Dept, UserId = "leaver" });
			r.Seats.Add(new DeploymentPersonnel { DeploymentPersonnelId = "seat-closed", DeploymentId = "closed", DepartmentId = Dept, UserId = "leaver" });
			r.Seats.Add(new DeploymentPersonnel { DeploymentPersonnelId = "seat-already-off", DeploymentId = "open", DepartmentId = Dept, UserId = "leaver", RemovedOn = DateTime.UtcNow.AddDays(-3) });

			// The member has another department, so only this one is revoked.
			(await r.Service.DeleteUserAsync(Dept, "chief", "leaver")).Should().Be(DeleteUserResults.NoFailure);

			r.Calls.Should().Equal("seat:seat-open:chief", "employment:" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ":chief", "membership");
			r.Deployments.Verify(d => d.RemovePersonnelAsync("seat-already-off", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			r.Deployments.Verify(d => d.RemovePersonnelAsync("seat-closed", Dept, "chief", null, null, It.IsAny<CancellationToken>()), Times.Once, "a closed deployment's roster is history and is skipped, not a failure");
		}

		[Test]
		public async Task A_failure_releasing_a_seat_stops_the_removal_before_the_membership_is_deleted()
		{
			var r = Build();
			r.Seats.Add(new DeploymentPersonnel { DeploymentPersonnelId = "seat-open", DeploymentId = "open", DepartmentId = Dept, UserId = "leaver" });
			r.Deployments.Setup(d => d.RemovePersonnelAsync("seat-open", Dept, It.IsAny<string>(), null, null, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("database"));

			Func<Task> revoke = () => r.Service.RevokeDepartmentAccessAsync("leaver", Dept, "chief");
			await revoke.Should().ThrowAsync<TimeoutException>();
			r.Calls.Should().NotContain("membership", "the removal stays retryable");
		}

		#endregion
	}
}
