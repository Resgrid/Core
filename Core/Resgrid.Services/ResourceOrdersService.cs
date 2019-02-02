using System.Collections.Generic;
using System.Data.Entity;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System.Linq;
using System.Device.Location;
using System.Threading.Tasks;
using Resgrid.Model.Events;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class ResourceOrdersService : IResourceOrdersService
	{
		private readonly IResourceOrdersRepository _resourceOrdersRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IGenericDataRepository<ResourceOrder> _genericResourceOrderRepository;
		private readonly IGenericDataRepository<ResourceOrderItem> _genericResourceOrderItemRepository;
		private readonly IGenericDataRepository<ResourceOrderFill> _genericResourceOrderFillRepository;

		public ResourceOrdersService(IResourceOrdersRepository resourceOrdersRepository, IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService, 
			IEventAggregator eventAggregator, IGenericDataRepository<ResourceOrder> genericResourceOrderRepository, IGenericDataRepository<ResourceOrderItem> genericResourceOrderItemRepository,
			IGenericDataRepository<ResourceOrderFill> genericResourceOrderFillRepository)
		{
			_resourceOrdersRepository = resourceOrdersRepository;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_genericResourceOrderRepository = genericResourceOrderRepository;
			_genericResourceOrderItemRepository = genericResourceOrderItemRepository;
			_genericResourceOrderFillRepository = genericResourceOrderFillRepository;
		}

		public async Task<List<ResourceOrder>> GetAll()
		{
			return await _resourceOrdersRepository.GetAll();
		}

		public async Task<List<ResourceOrder>> GetAllOpen()
		{
			return await _resourceOrdersRepository.GetAllOpen();
		}

		public async Task<List<ResourceOrder>> GetOpenOrdersByDepartmentId(int departmentId)
		{
			//return await _resourceOrdersRepository.GetOpenOrdersByDepartmentId(departmentId);
			return await _genericResourceOrderRepository.GetAll().Where(x => x.DepartmentId == departmentId && x.CloseDate == null).ToListAsync();
		}

		public async Task<List<ResourceOrder>> GetAllOrdersByDepartmentId(int departmentId)
		{
			//return await _resourceOrdersRepository.GetOpenOrdersByDepartmentId(departmentId);
			return await _genericResourceOrderRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToListAsync();
		}

		public async Task<ResourceOrderSetting> GetSettingsByDepartmentId(int departmentId)
		{
			return await _resourceOrdersRepository.GetOrderSettingByDepartmentId(departmentId);
		}

		public async Task<ResourceOrderSetting> SaveSettings(ResourceOrderSetting settings)
		{
			return await _resourceOrdersRepository.SaveSettings(settings);
		}

		public async Task<ResourceOrder> CreateOrder(ResourceOrder order)
		{
			var savedOrder = await _resourceOrdersRepository.SaveOrder(order);

			_eventAggregator.SendMessage<ResourceOrderAddedEvent>(new ResourceOrderAddedEvent()
			{
				Order = savedOrder
			});

			return savedOrder;
		}

		public async Task<ResourceOrder> GetOrderById(int orderId)
		{
			//return await _resourceOrdersRepository.GetOrderById(orderId);
			return await _genericResourceOrderRepository.GetAll().Where(x => x.ResourceOrderId == orderId).FirstOrDefaultAsync();
		}

		public async Task<ResourceOrderItem> GetOrderItemById(int orderItemId)
		{
			return await _genericResourceOrderItemRepository.GetAll().Where(x => x.ResourceOrderItemId == orderItemId).FirstOrDefaultAsync();
		}

		public async Task<ResourceOrderFill> GetOrderFillById(int orderFillId)
		{
			return await _genericResourceOrderFillRepository.GetAll().Where(x => x.ResourceOrderFillId == orderFillId).FirstOrDefaultAsync();
		}

		public async Task<ResourceOrderFill> CreateFill(ResourceOrderFill fill)
		{
			var savedFill = await _resourceOrdersRepository.SaveFill(fill);

			//_eventAggregator.SendMessage<ResourceOrderFillAddedEvent>(new ResourceOrderFillAddedEvent()
			//{
			//	Order = savedOrder
			//});

			return savedFill;
		}

		public async Task SetFillStatus(int fillId, string userId, bool accepted)
		{
			await _resourceOrdersRepository.UpdateFillStatus(fillId, userId, accepted);
		}

		public async Task<List<ResourceOrder>> GetOpenAvailableOrders(int departmentId)
		{
			var orders = new List<ResourceOrder>();

			var departmentSettings = await _resourceOrdersRepository.GetOrderSettingByDepartmentId(departmentId);
			var department = _departmentsService.GetDepartmentById(departmentId);
			var mapCenterLocation = _departmentSettingsService.GetMapCenterCoordinates(department);

			// Target department does not want to recieve orders
			if (departmentSettings != null && departmentSettings.DoNotReceiveOrders)
				return orders;

			//var allRangeOrders = await _resourceOrdersRepository.GetAllOpenOrdersByRange(departmentId);
			var allRangeOrders =
				await _genericResourceOrderRepository.GetAll()
					.Where(x => x.DepartmentId != departmentId && x.CloseDate == null && x.Visibility == 0)
					.ToListAsync();
			orders.AddRange(allRangeOrders.Where(x => (x.OriginLocation.GetDistanceTo(new GeoCoordinate(mapCenterLocation.Latitude.Value, mapCenterLocation.Longitude.Value)) / 1609.344) <= x.Range));
			//orders.AddRange(await _resourceOrdersRepository.GetAllOpenOrdersUnrestricted(departmentId));
			orders.AddRange(await _genericResourceOrderRepository.GetAll().Where(x => x.DepartmentId != departmentId && x.CloseDate == null && x.Visibility == 3).ToListAsync());
			orders.AddRange(await _resourceOrdersRepository.GetAllOpenOrdersLinked(departmentId));

			return orders;
		}
	}
}