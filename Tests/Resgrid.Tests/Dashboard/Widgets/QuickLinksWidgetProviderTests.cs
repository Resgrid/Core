using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class QuickLinksWidgetProviderTests
	{
		[Test]
		public void Only_absolute_http_and_https_links_survive()
		{
			var (links, invalid) = QuickLinksWidgetProvider.ParseUserLinks(string.Join("\n",
				"County CAD | https://cad.example.gov/login",
				"http://weather.example.com",
				"Bad | javascript:alert(1)",
				"Data | data:text/html,<script>alert(1)</script>",
				"Relative | /User/Calls",
				"Creds | https://user:pass@example.com",
				"Pipe | in label | https://example.org/a",
				"   ",
				"FTP | ftp://files.example.com"));

			links.Select(l => (l.Label, l.Url)).Should().Equal(
				("County CAD", "https://cad.example.gov/login"),
				("weather.example.com", "http://weather.example.com/"),
				("Pipe | in label", "https://example.org/a"));
			invalid.Should().Be(5);
		}

		[Test]
		public void The_number_of_links_is_capped()
		{
			var (links, invalid) = QuickLinksWidgetProvider.ParseUserLinks(string.Join("\n", Enumerable.Range(1, 25).Select(i => $"L{i} | https://example.com/{i}")));

			links.Should().HaveCount(QuickLinksWidgetProvider.MaxUserLinks);
			invalid.Should().Be(5);
		}

		[Test]
		public async Task Department_links_are_the_departments_sharing_with_this_one()
		{
			var limits = new Mock<ILimitsService>(MockBehavior.Strict);
			limits.Setup(x => x.CanDepartmentUseLinksAsync(DepartmentId)).ReturnsAsync(true);
			var links = new Mock<IDepartmentLinksService>(MockBehavior.Strict);
			links.Setup(x => x.GetAllLinksForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentLink>
			{
				new DepartmentLink { DepartmentLinkId = 1, DepartmentId = OtherDepartmentId, LinkedDepartmentId = DepartmentId, LinkEnabled = true, DepartmentShareCalls = true,
					DepartmentColor = "#ff0000", Department = new Department { Name = "Neighbor FD" } },
				new DepartmentLink { DepartmentLinkId = 2, DepartmentId = OtherDepartmentId, LinkedDepartmentId = DepartmentId, LinkEnabled = false, Department = new Department { Name = "Pending" } },
				new DepartmentLink { DepartmentLinkId = 3, DepartmentId = DepartmentId, LinkedDepartmentId = OtherDepartmentId, LinkEnabled = true, Department = new Department { Name = "Us" } }
			});
			var provider = new QuickLinksWidgetProvider(links.Object, limits.Object);

			var data = (QuickLinksWidgetData)await provider.GetDataAsync(
				For("links.quick", CommsPlanningWidgetContexts.Settings(("links", "Wiki | https://wiki.example.com"))), CancellationToken.None);

			data.DepartmentLinksAvailable.Should().BeTrue();
			data.DepartmentLinks.Should().ContainSingle().Which.Should().BeEquivalentTo(new
			{
				DepartmentLinkId = 1, DepartmentName = "Neighbor FD", Color = "#ff0000", SharesCalls = true, Url = "/User/Links/View?linkId=1"
			});
			data.UserLinks.Should().ContainSingle().Which.Label.Should().Be("Wiki");
		}

		[Test]
		public async Task A_plan_without_links_reads_no_links_and_keeps_the_members_own()
		{
			var limits = new Mock<ILimitsService>(MockBehavior.Strict);
			limits.Setup(x => x.CanDepartmentUseLinksAsync(DepartmentId)).ReturnsAsync(false);
			var provider = new QuickLinksWidgetProvider(new Mock<IDepartmentLinksService>(MockBehavior.Strict).Object, limits.Object);

			var data = (QuickLinksWidgetData)await provider.GetDataAsync(
				For("links.quick", CommsPlanningWidgetContexts.Settings(("links", "https://a.example.com"))), CancellationToken.None);

			data.DepartmentLinksAvailable.Should().BeFalse();
			data.DepartmentLinks.Should().BeEmpty();
			data.UserLinks.Should().ContainSingle();
		}
	}
}
