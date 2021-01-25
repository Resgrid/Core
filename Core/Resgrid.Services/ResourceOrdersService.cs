using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using GeoCoordinatePortable;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class ResourceOrdersService : IResourceOrdersService
	{
		private readonly IResourceOrdersRepository _resourceOrdersRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IResourceOrderItemRepository _resourceOrderItemRepository;
		private readonly IResourceOrderFillRepository _resourceOrderFillRepository;
		private readonly IResourceOrderSettingsRepository _resourceOrderSettingsRepository;

		public ResourceOrdersService(IResourceOrdersRepository resourceOrdersRepository, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator, IResourceOrderItemRepository resourceOrderItemRepository,
			IResourceOrderFillRepository resourceOrderFillRepository, IResourceOrderSettingsRepository resourceOrderSettingsRepository)
		{
			_resourceOrdersRepository = resourceOrdersRepository;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_resourceOrderItemRepository = resourceOrderItemRepository;
			_resourceOrderFillRepository = resourceOrderFillRepository;
			_resourceOrderSettingsRepository = resourceOrderSettingsRepository;
		}

		public async Task<List<ResourceOrder>> GetAllAsync()
		{
			var items = await _resourceOrdersRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<ResourceOrder>();
		}

		public async Task<List<ResourceOrder>> GetAllOpenAsync()
		{
			var items = await _resourceOrdersRepository.GetAllOpenOrdersAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<ResourceOrder>();
		}

		public async Task<List<ResourceOrder>> GetOpenOrdersByDepartmentIdAsync(int departmentId)
		{
			var items = await GetAllOpenAsync();
			return items.Where(x => x.DepartmentId == departmentId).ToList();
		}

		public async Task<List<ResourceOrder>> GetAllOrdersByDepartmentIdAsync(int departmentId)
		{
			var items = await _resourceOrdersRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<ResourceOrder>();
		}

		public async Task<ResourceOrderSetting> GetSettingsByDepartmentIdAsync(int departmentId)
		{
			var items = await _resourceOrderSettingsRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.FirstOrDefault();

			return null;
		}

		public async Task<ResourceOrderSetting> SaveSettingsAsync(ResourceOrderSetting settings, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _resourceOrderSettingsRepository.SaveOrUpdateAsync(settings, cancellationToken);
		}

		public async Task<ResourceOrder> CreateOrderAsync(ResourceOrder order, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedOrder = await _resourceOrdersRepository.SaveOrderAsync(order, cancellationToken);

			_eventAggregator.SendMessage<ResourceOrderAddedEvent>(new ResourceOrderAddedEvent()
			{
				Order = savedOrder
			});

			return savedOrder;
		}

		public async Task<ResourceOrder> GetOrderByIdAsync(int orderId)
		{
			return await _resourceOrdersRepository.GetByIdAsync(orderId);
		}

		public async Task<ResourceOrderItem> GetOrderItemByIdAsync(int orderItemId)
		{
			return await _resourceOrderItemRepository.GetByIdAsync(orderItemId);
		}

		public async Task<ResourceOrderFill> GetOrderFillByIdAsync(int orderFillId)
		{
			return await _resourceOrderFillRepository.GetByIdAsync(orderFillId);
		}

		public async Task<ResourceOrderFill> CreateFillAsync(ResourceOrderFill fill, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _resourceOrdersRepository.SaveFillAsync(fill, cancellationToken);
		}

		public async Task<bool> SetFillStatusAsync(int fillId, string userId, bool accepted, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _resourceOrderFillRepository.UpdateFillStatusAsync(fillId, userId, accepted, cancellationToken);
		}

		public async Task<List<ResourceOrder>> GetOpenAvailableOrdersAsync(int departmentId)
		{
			var orders = new List<ResourceOrder>();

			var departmentSettings = await GetSettingsByDepartmentIdAsync(departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var mapCenterLocation = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);

			// Target department does not want to recieve orders
			if (departmentSettings != null && departmentSettings.DoNotReceiveOrders)
				return orders;

			var allRangeOrders = await _resourceOrdersRepository.GetAllNonDepartmentOpenVisibleOrdersAsync(departmentId);
			orders.AddRange(allRangeOrders.Where(x => (x.OriginLocation.GetDistanceTo(new GeoCoordinate(mapCenterLocation.Latitude.Value, mapCenterLocation.Longitude.Value)) / 1609.344) <= x.Range));

			// TODO: Yea :-(
			//orders.AddRange(await _genericResourceOrderRepository.GetAll().Where(x => x.DepartmentId != departmentId && x.CloseDate == null && x.Visibility == 3).ToListAsync());
			//orders.AddRange(await _resourceOrdersRepository.GetAllOpenOrdersLinked(departmentId));

			return orders;
		}
	}
}
