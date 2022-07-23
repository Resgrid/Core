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
using Resgrid.Framework;

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
		private readonly IResourceOrderFillUnitRepository _resourceOrderFillUnitRepository;

		public ResourceOrdersService(IResourceOrdersRepository resourceOrdersRepository, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator, IResourceOrderItemRepository resourceOrderItemRepository,
			IResourceOrderFillRepository resourceOrderFillRepository, IResourceOrderSettingsRepository resourceOrderSettingsRepository,
			IResourceOrderFillUnitRepository resourceOrderFillUnitRepository)
		{
			_resourceOrdersRepository = resourceOrdersRepository;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_resourceOrderItemRepository = resourceOrderItemRepository;
			_resourceOrderFillRepository = resourceOrderFillRepository;
			_resourceOrderSettingsRepository = resourceOrderSettingsRepository;
			_resourceOrderFillUnitRepository = resourceOrderFillUnitRepository;
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
			{
				var orders = items.ToList();

				for (int i = 0; i < orders.Count; i++)
				{
					orders[i].Department = await _departmentsService.GetDepartmentByIdAsync(orders[i].DepartmentId);

					var resourceItems = await _resourceOrdersRepository.GetAllItemsByResourceOrderIdAsync(orders[i].ResourceOrderId);

					if (resourceItems != null && resourceItems.Any())
					{
						orders[i].Items = resourceItems?.ToList();

						foreach (var item in orders[i].Items)
						{
							if (item.Fills != null && item.Fills.Any())
							{
								foreach (var fill in item.Fills)
								{
									fill.Department = await _departmentsService.GetDepartmentByIdAsync(fill.DepartmentId);
								}
							}

						}
					}
					else
						orders[i].Items = new List<ResourceOrderItem>();
				}

				return orders;
			}

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
			order.Summary = StringHelpers.SanitizeHtmlInString(order.Summary);
			order.SpecialInstructions = StringHelpers.SanitizeHtmlInString(order.SpecialInstructions);

			var savedOrder = await _resourceOrdersRepository.SaveOrderAsync(order, cancellationToken);

			_eventAggregator.SendMessage<ResourceOrderAddedEvent>(new ResourceOrderAddedEvent()
			{
				Order = savedOrder
			});

			return savedOrder;
		}

		public async Task<ResourceOrder> GetOrderByIdAsync(int orderId)
		{
			var order = await _resourceOrdersRepository.GetByIdAsync(orderId);

			if (order != null)
			{
				order.Department = await _departmentsService.GetDepartmentByIdAsync(order.DepartmentId);

				var items = await _resourceOrdersRepository.GetAllItemsByResourceOrderIdAsync(orderId);

				if (items != null && items.Any())
				{
					order.Items = items.ToList();

					foreach (var item in order.Items)
					{
						if (item.Fills != null && item.Fills.Any())
						{
							foreach (var fill in item.Fills)
							{
								fill.Department = await _departmentsService.GetDepartmentByIdAsync(fill.DepartmentId);
								var fillUnits = await _resourceOrderFillUnitRepository.GetAllResourceOrderFillUnitsByFillIdAsync(fill.ResourceOrderFillId);

								if (fillUnits != null && fillUnits.Any())
								{
									fill.Units = fillUnits?.ToList();
								}
								else
								{
									fill.Units = new List<ResourceOrderFillUnit>();
								}
							}
						}

					}
				}
				else
					order.Items = new List<ResourceOrderItem>();
			}

			return order;
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

			if (allRangeOrders != null && allRangeOrders.Any())
			{
				var allOrders = allRangeOrders.ToList();

				for (int i = 0; i < allOrders.Count; i++)
				{
					allOrders[i].Department = await _departmentsService.GetDepartmentByIdAsync(allOrders[i].DepartmentId);
					var resourceItems = await _resourceOrdersRepository.GetAllItemsByResourceOrderIdAsync(allOrders[i].ResourceOrderId);

					if (resourceItems != null && resourceItems.Any())
					{
						allOrders[i].Items = resourceItems?.ToList();

						foreach (var item in allOrders[i].Items)
						{
							if (item.Fills != null && item.Fills.Any())
							{
								foreach (var fill in item.Fills)
								{
									fill.Department = await _departmentsService.GetDepartmentByIdAsync(fill.DepartmentId);
								}
							}
							
						}
					}
					else
						allOrders[i].Items = new List<ResourceOrderItem>();
				}
			}

			orders.AddRange(allRangeOrders.Where(x => (x.OriginLocation.GetDistanceTo(new GeoCoordinate(mapCenterLocation.Latitude.Value, mapCenterLocation.Longitude.Value)) / 1609.344) <= x.Range));

			// TODO: Yea :-(
			//orders.AddRange(await _genericResourceOrderRepository.GetAll().Where(x => x.DepartmentId != departmentId && x.CloseDate == null && x.Visibility == 3).ToListAsync());
			//orders.AddRange(await _resourceOrdersRepository.GetAllOpenOrdersLinked(departmentId));

			return orders;
		}
	}
}
