using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Readiness;
using static Resgrid.Tests.Dashboard.Widgets.ReadinessRecordsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>certifications.expiry and certifications.mine (plan §7.7).</summary>
	[TestFixture]
	public class CertificationsWidgetProviderTests
	{
		[Test]
		public async Task Expiry_carries_the_dashboard_totals_and_the_most_urgent_cells()
		{
			var certifications = new Mock<ICertificationService>(MockBehavior.Strict);
			certifications.Setup(c => c.GetExpiryDashboardAsync(DepartmentId, null)).ReturnsAsync(new CertificationExpiryDashboard
			{
				Horizon = 60, ExpiredCount = 1, ExpiringCount = 2, SuspendedCount = 1, PendingVerificationCount = 3,
				PersonCells = new List<CertificationDashboardCell>
				{
					new CertificationDashboardCell { SubjectId = "u1", SubjectName = "Ann", TypeName = "EMT", Status = (int)PersonnelCertificationStatuses.Active, DaysUntilExpiry = 40 },
					new CertificationDashboardCell { SubjectId = "u2", SubjectName = "Bob", TypeName = "EMT", Status = (int)PersonnelCertificationStatuses.Expired, DaysUntilExpiry = -3 },
					new CertificationDashboardCell { SubjectId = "u3", SubjectName = "Cy", TypeName = "CPR", Status = (int)PersonnelCertificationStatuses.Active, DaysUntilExpiry = 400 }
				},
				UnitCells = new List<CertificationDashboardCell>
				{
					new CertificationDashboardCell { SubjectId = "9", SubjectName = "Engine 9", TypeName = "Pump test", Status = (int)UnitCertificationStatuses.Active, DaysUntilExpiry = 5 }
				}
			});

			var data = (CertificationsExpiryWidgetData)await new CertificationsExpiryWidgetProvider(certifications.Object).GetDataAsync(Context("certifications.expiry"), CancellationToken.None);

			data.HorizonDays.Should().Be(60);
			data.Expired.Should().Be(1);
			data.Expiring.Should().Be(2);
			data.PendingVerification.Should().Be(3);
			data.Items.Select(i => (i.Scope, i.SubjectName)).Should().Equal(("person", "Bob"), ("unit", "Engine 9"), ("person", "Ann"));
			data.Items[0].Tone.Should().Be("danger");
		}

		[Test]
		public async Task Mine_lists_only_the_viewers_records_in_this_department_within_the_horizon()
		{
			var certifications = new Mock<ICertificationService>(MockBehavior.Strict);
			certifications.Setup(c => c.GetCertificationsForDepartmentAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { UserId }))))
				.ReturnsAsync(new List<PersonnelCertification>
				{
					new PersonnelCertification { PersonnelCertificationId = 1, DepartmentId = DepartmentId, UserId = UserId, Name = "EMT", ExpiresOn = Now.AddDays(20), Number = "LIC-123" },
					new PersonnelCertification { PersonnelCertificationId = 2, DepartmentId = DepartmentId, UserId = UserId, Name = "CPR", ExpiresOn = Now.AddDays(-1) },
					new PersonnelCertification { PersonnelCertificationId = 3, DepartmentId = DepartmentId, UserId = UserId, Name = "Hazmat", ExpiresOn = Now.AddDays(200) },
					new PersonnelCertification { PersonnelCertificationId = 4, DepartmentId = OtherDepartmentId, UserId = UserId, Name = "Elsewhere", ExpiresOn = Now.AddDays(1) },
					new PersonnelCertification { PersonnelCertificationId = 5, DepartmentId = DepartmentId, UserId = OtherUserId, Name = "Not mine", ExpiresOn = Now.AddDays(1) },
					new PersonnelCertification { PersonnelCertificationId = 6, DepartmentId = DepartmentId, UserId = UserId, Name = "Deleted", ExpiresOn = Now.AddDays(1), IsDeleted = true }
				});
			certifications.Setup(c => c.GetCertificationSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCertificationSettings { NotifyLeadDaysCsv = "30,14" });

			var data = (CertificationsMineWidgetData)await new CertificationsMineWidgetProvider(certifications.Object).GetDataAsync(Context("certifications.mine"), CancellationToken.None);

			data.HorizonDays.Should().Be(30);
			data.Total.Should().Be(3);
			data.Expired.Should().Be(1);
			data.Expiring.Should().Be(1);
			data.Items.Select(i => i.Name).Should().Equal("CPR", "EMT");
			typeof(CertificationsMineItem).GetProperty("Number").Should().BeNull("certificate numbers are protected");
		}
	}
}
