using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	//namespace ResourceOrdersServiceTests
	//{
	//	public class with_the_resource_orders_service : TestBase
	//	{
	//		protected ResourceOrdersService _resourceOrdersService;
	//		private Mock<IResourceOrdersRepository> _resourceOrdersRepositoryMock;
	//		private Mock<IDepartmentsService> _departmentsServiceMock;
	//		private Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;

	//		protected with_the_resource_orders_service()
	//		{
	//			_resourceOrdersRepositoryMock = new Mock<IResourceOrdersRepository>();
	//			_departmentsServiceMock = new Mock<IDepartmentsService>();
	//			_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();

	//			_resourceOrdersRepositoryMock.Setup(x => x.GetOrderSettingByDepartmentId(100)).Returns(
	//				new ResourceOrderSetting() { ResourceOrderSettingId = 100, DepartmentId = 100, DoNotReceiveOrders = true });

	//			_resourceOrdersService = new ResourceOrdersService(_resourceOrdersRepositoryMock.Object, _departmentsServiceMock.Object, _departmentSettingsServiceMock.Object);
	//		}
	//	}

	//	[TestFixture]
	//	public class when_getting_available_orders : with_the_resource_orders_service
	//	{
	//		[Test]
	//		public void should_not_get_orders_for_department_with_orders_off()
	//		{
	//			var orders = _resourceOrdersService.GetOpenAvailableOrders(100);

	//			orders.Should().NotBeNull();
	//			orders.Should().BeEmpty();
	//		}
	//	}
	//}
}