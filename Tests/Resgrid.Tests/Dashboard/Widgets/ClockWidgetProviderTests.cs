using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Awareness;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class ClockWidgetProviderTests
	{
		[TestCase("Pacific Standard Time", "America/Los_Angeles")]
		[TestCase("Europe/Brussels", "Europe/Brussels")]
		[TestCase("Not A Zone", "Etc/UTC")]
		[TestCase(null, "Etc/UTC")]
		public void Converts_the_stored_zone_to_iana(string stored, string expected)
		{
			ClockWidgetProvider.ToIana(stored).Should().Be(expected);
		}

		[Test]
		public async Task Uses_the_viewers_department_zone_without_a_read()
		{
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			var provider = new ClockWidgetProvider(departments.Object);

			var data = (ClockWidgetData)await provider.GetDataAsync(For("general.clock"), CancellationToken.None);

			data.TimeZoneId.Should().Be("America/Los_Angeles");
			data.DepartmentTimeZone.Should().Be("Pacific Standard Time");
		}

		[Test]
		public async Task Reads_the_department_when_the_viewer_has_no_zone()
		{
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			var provider = new ClockWidgetProvider(departments.Object);

			var data = (ClockWidgetData)await provider.GetDataAsync(For("general.clock", timeZone: null), CancellationToken.None);

			data.TimeZoneId.Should().Be("America/New_York");
		}
	}
}
