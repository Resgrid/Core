using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Invoicing;
using Resgrid.Services.Workforce;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 items 2.18 and 2.15 / 5.6: nobody approves their own compensation profile or a change they made to
	/// one (the TimeReportApproval rule for pay); and deployment crew access comes from the roster a manager recorded, never
	/// from a unit seat a member took in the unit app.
	/// </summary>
	[TestFixture]
	public class BusinessOpsSeparationOfDutiesTests
	{
		private const int Dept = 81;

		#region Compensation approval

		private EmployeeCompensationProfile _profile;
		private Mock<IEmployeeCompensationProfileRepository> _profiles;
		private CompensationCostService _compensation;

		private void BuildCompensation(EmployeeCompensationProfile profile)
		{
			_profile = profile;
			_profiles = new Mock<IEmployeeCompensationProfileRepository>();
			_profiles.Setup(r => r.GetByIdForDepartmentAsync("cp-1", Dept)).ReturnsAsync(() => _profile);
			var employments = new Mock<IWorkforceEmploymentRepository>();
			employments.Setup(r => r.GetByIdForDepartmentAsync("emp-1", Dept)).ReturnsAsync(new WorkforceEmployment { WorkforceEmploymentId = "emp-1", DepartmentId = Dept, WorkforceWorkerId = "w-1" });
			var workers = new Mock<IWorkforceWorkerRepository>();
			workers.Setup(r => r.GetByIdForDepartmentAsync("w-1", Dept)).ReturnsAsync(new WorkforceWorker { WorkforceWorkerId = "w-1", DepartmentId = Dept, UserId = "employee-1" });
			_compensation = new CompensationCostService(_profiles.Object, Mock.Of<IEmployeePayComponentRepository>(), Mock.Of<IEmployeeCostComponentRepository>(), employments.Object,
				Mock.Of<IWorkforceJobAssignmentRepository>(), Mock.Of<IEventAggregator>(), Mock.Of<IUnitOfWork>(), workers: workers.Object);
		}

		private static EmployeeCompensationProfile Profile(string addedBy, string editedBy = null, int scope = (int)CompensationScopes.Employee) => new EmployeeCompensationProfile
		{
			EmployeeCompensationProfileId = "cp-1", DepartmentId = Dept, Scope = scope, WorkforceEmploymentId = scope == (int)CompensationScopes.Employee ? "emp-1" : null,
			AddedByUserId = addedBy, EditedByUserId = editedBy, EffectiveOn = new DateTime(2026, 1, 1)
		};

		[Test]
		public async Task The_maker_of_a_compensation_change_cannot_approve_it()
		{
			BuildCompensation(Profile("payroll-1", "payroll-2"));

			(await FluentActions.Awaiting(() => _compensation.ApproveProfileAsync("cp-1", Dept, "payroll-2", null, null)).Should().ThrowAsync<InvalidOperationException>())
				.Which.Message.Should().Be(CompensationCostService.SelfApprovalRefused);
			_profile.IsApproved.Should().BeFalse();

			await _compensation.ApproveProfileAsync("cp-1", Dept, "payroll-1", null, null);
			_profile.IsApproved.Should().BeTrue("the creator did not make the latest change; a second person approves it");
			_profile.ApprovedByUserId.Should().Be("payroll-1");
		}

		[Test]
		public async Task Nobody_approves_their_own_pay()
		{
			BuildCompensation(Profile("payroll-1"));

			(await FluentActions.Awaiting(() => _compensation.ApproveProfileAsync("cp-1", Dept, "employee-1", null, null)).Should().ThrowAsync<InvalidOperationException>())
				.Which.Message.Should().Be(CompensationCostService.SelfApprovalRefused);
			_profiles.Verify(r => r.SaveOrUpdateAsync(It.IsAny<EmployeeCompensationProfile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_department_default_is_approvable_by_anyone_but_its_maker()
		{
			BuildCompensation(Profile("payroll-1", scope: (int)CompensationScopes.DepartmentDefault));

			await _compensation.ApproveProfileAsync("cp-1", Dept, "employee-1", null, null);
			_profile.IsApproved.Should().BeTrue();
		}

		#endregion

		#region Deployment roster

		private Mock<IDeploymentPersonnelRepository> _personnel;
		private Mock<IUnitsService> _units;
		private DeploymentService _deployments;

		private void BuildDeployments(List<DeploymentPersonnel> roster)
		{
			_personnel = new Mock<IDeploymentPersonnelRepository>();
			_personnel.Setup(r => r.GetByDeploymentAsync("dep-1")).ReturnsAsync(roster);
			_personnel.Setup(r => r.GetForUserAsync(Dept, It.IsAny<string>())).ReturnsAsync((int _, string user) => roster.Where(p => p.UserId == user).ToList());
			_units = new Mock<IUnitsService>();
			// "seated" put themselves on Engine 2 in the unit app; the deployment carries Engine 2.
			_units.Setup(u => u.GetAllActiveRolesForUnitsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = 2, UserId = "seated", DepartmentId = Dept } });
			var deploymentRows = new Mock<IDeploymentRepository>();
			deploymentRows.Setup(r => r.GetByIdForDepartmentAsync("dep-1", Dept)).ReturnsAsync(new Deployment { DeploymentId = "dep-1", DepartmentId = Dept, Status = (int)DeploymentStatuses.Active });
			deploymentRows.Setup(r => r.GetByIdsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int _, IEnumerable<string> ids) => ids.Select(id => new Deployment { DeploymentId = id, DepartmentId = Dept, Status = (int)DeploymentStatuses.Active }).ToList());
			var unitRows = new Mock<IDeploymentUnitRepository>();
			unitRows.Setup(r => r.GetByDeploymentAsync("dep-1")).ReturnsAsync(new List<DeploymentUnit> { new DeploymentUnit { DeploymentUnitId = "du-2", DeploymentId = "dep-1", DepartmentId = Dept, UnitId = 2 } });
			unitRows.Setup(r => r.GetForUnitsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<DeploymentUnit> { new DeploymentUnit { DeploymentUnitId = "du-2", DeploymentId = "dep-1", DepartmentId = Dept, UnitId = 2 } });

			var constructor = typeof(DeploymentService).GetConstructors().Single();
			_deployments = (DeploymentService)constructor.Invoke(constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(IDeploymentPersonnelRepository) ? _personnel.Object :
				p.ParameterType == typeof(IUnitsService) ? _units.Object :
				p.ParameterType == typeof(IDeploymentRepository) ? deploymentRows.Object :
				p.ParameterType == typeof(IDeploymentUnitRepository) ? unitRows.Object :
				p.ParameterType.IsInterface ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object : (object)null).ToArray());
		}

		private static Deployment OnEngineTwo(params DeploymentPersonnel[] roster)
		{
			var deployment = new Deployment { DeploymentId = "dep-1", DepartmentId = Dept, Status = (int)DeploymentStatuses.Active };
			deployment.Units.Add(new DeploymentUnit { DeploymentUnitId = "du-2", DeploymentId = "dep-1", DepartmentId = Dept, UnitId = 2 });
			deployment.Personnel.AddRange(roster);
			return deployment;
		}

		[Test]
		public async Task Seating_oneself_on_a_deployed_unit_opens_nothing()
		{
			BuildDeployments(new List<DeploymentPersonnel>());

			var access = await _deployments.GetTimeAccessAsync(OnEngineTwo(), "seated", false);
			access.CanRead.Should().BeFalse();
			access.CanWrite.Should().BeFalse();
			(await _deployments.CanFieldMemberSeeAsync("dep-1", Dept, "seated")).Should().BeFalse();
			(await _deployments.GetDeploymentsForUserAsync(Dept, "seated", false)).Should().BeEmpty();
			_units.Verify(u => u.GetAllActiveRolesForUnitsByDepartmentIdAsync(It.IsAny<int>()), Times.Never, "live unit seats are no longer consulted");
		}

		[Test]
		public async Task A_member_the_roster_seats_on_the_unit_keeps_crew_access()
		{
			var row = new DeploymentPersonnel { DeploymentPersonnelId = "dp-1", DeploymentId = "dep-1", DepartmentId = Dept, UserId = "crew", DeploymentUnitId = "du-2" };
			BuildDeployments(new List<DeploymentPersonnel> { row });

			var access = await _deployments.GetTimeAccessAsync(OnEngineTwo(row), "crew", false);
			access.IsRostered.Should().BeTrue();
			access.CrewUnitIds.Should().Equal("du-2");
			access.WritableSubjectIds.Should().BeEquivalentTo(new[] { "dp-1", "du-2" });
			(await _deployments.CanFieldMemberSeeAsync("dep-1", Dept, "crew")).Should().BeTrue();
			(await _deployments.GetDeploymentsForUserAsync(Dept, "crew", false)).Select(d => d.DeploymentId).Should().Equal("dep-1");
		}

		#endregion
	}
}
