using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class QualificationEvidenceTests
	{
		[Test]
		public async Task A_required_role_nobody_holds_yet_is_not_an_uncovered_qualification()
		{
			var actor = new AdminAssistActor(7, "admin");
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "UTC" });
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(c => c.GetExpiryDashboardAsync(7, It.IsAny<DateTime?>())).ReturnsAsync(new CertificationExpiryDashboard());
			certifications.Setup(c => c.GetAllRoleRequirementsAsync(7)).ReturnsAsync(new List<PersonnelRoleCertificationRequirement>
			{
				new() { PersonnelRoleId = 1, DepartmentId = 7, IsMandatory = true },
				new() { PersonnelRoleId = 2, DepartmentId = 7, IsMandatory = true }
			});
			// Role 1 was created during setup before anyone was assigned; role 2 is held only by an unqualified member.
			certifications.Setup(c => c.EvaluateRoleRequirementsAsync(7, 1, It.IsAny<DateTime?>())).ReturnsAsync(new List<RoleCertificationEvaluation>());
			certifications.Setup(c => c.EvaluateRoleRequirementsAsync(7, 2, It.IsAny<DateTime?>())).ReturnsAsync(new List<RoleCertificationEvaluation>
			{
				new() { PersonnelRoleId = 2, UserId = "member", Qualified = false }
			});
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserViewPersonAsync(actor.UserId, "member", 7)).ReturnsAsync(true);

			var evidence = await new QualificationEvidenceSource(certifications.Object, authorization.Object, departments.Object)
				.ReadAsync(actor, DateTime.UtcNow, CancellationToken.None);

			Assert.That(evidence.Single(e => e.Id == "uncoveredQualificationCount").Number, Is.EqualTo(1m));
		}
	}
}
