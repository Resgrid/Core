using System;
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
	public class MapVisualizationWidgetProviderTests
	{
		[Test]
		public async Task Without_a_chosen_visualization_nothing_is_read()
		{
			var service = new Mock<IMapVisualizationService>(MockBehavior.Strict);
			var provider = new MapVisualizationWidgetProvider(service.Object);

			var data = (MapVisualizationWidgetData)await provider.GetDataAsync(For("map.visualization"), CancellationToken.None);

			data.Configured.Should().BeFalse();
			data.Result.Should().BeNull();
		}

		[Test]
		public async Task A_visualization_from_another_department_or_member_is_not_run()
		{
			var service = new Mock<IMapVisualizationService>(MockBehavior.Strict);
			// A loose implementation handing back someone else's row must still not be run.
			service.Setup(x => x.GetVisibleByIdAsync(DepartmentId, UserId, "viz-1"))
				.ReturnsAsync(new MapVisualization { MapVisualizationId = "viz-1", DepartmentId = OtherDepartmentId, IsShared = true });
			var provider = new MapVisualizationWidgetProvider(service.Object);

			var data = (MapVisualizationWidgetData)await provider.GetDataAsync(
				For("map.visualization", CommsPlanningWidgetContexts.Settings(("visualization", "viz-1"))), CancellationToken.None);

			data.Configured.Should().BeTrue();
			data.Found.Should().BeFalse();
			service.Verify(x => x.RunAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<MapVisualizationDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_visible_visualization_runs_as_the_viewer()
		{
			var service = new Mock<IMapVisualizationService>(MockBehavior.Strict);
			var visualization = new MapVisualization
			{
				MapVisualizationId = "viz-2", DepartmentId = DepartmentId, Name = "Hot zones", CreatedByUserId = UserId, UpdatedOn = Now
			};
			var result = new MapVisualizationResult { CenterLatitude = 1 };
			service.Setup(x => x.GetVisibleByIdAsync(DepartmentId, UserId, "viz-2")).ReturnsAsync(visualization);
			service.Setup(x => x.RunAsync(DepartmentId, UserId, It.IsAny<MapVisualizationDefinition>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
			var provider = new MapVisualizationWidgetProvider(service.Object);

			var data = (MapVisualizationWidgetData)await provider.GetDataAsync(
				For("map.visualization", CommsPlanningWidgetContexts.Settings(("visualization", "viz-2"))), CancellationToken.None);

			data.Found.Should().BeTrue();
			data.Name.Should().Be("Hot zones");
			data.Result.Should().BeSameAs(result);
			data.Url.Should().Be("/User/MapVisualizations?id=viz-2");
		}
	}
}
