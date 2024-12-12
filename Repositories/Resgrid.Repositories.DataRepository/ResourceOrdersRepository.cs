using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.ResourceOrders;

namespace Resgrid.Repositories.DataRepository
{
	public class ResourceOrdersRepository : RepositoryBase<ResourceOrder>, IResourceOrdersRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IResourceOrderItemRepository _resourceOrderItemRepository;
		private readonly IResourceOrderFillRepository _resourceOrderFillRepository;
		private readonly IResourceOrderFillUnitRepository _resourceOrderFillUnitRepository;

		public ResourceOrdersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork,
			IQueryFactory queryFactory, IResourceOrderItemRepository resourceOrderItemRepository, IResourceOrderFillRepository resourceOrderFillRepository, IResourceOrderFillUnitRepository resourceOrderFillUnitRepository)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
			_queryFactory = queryFactory;
			_resourceOrderItemRepository = resourceOrderItemRepository;
			_resourceOrderFillRepository = resourceOrderFillRepository;
			_resourceOrderFillUnitRepository = resourceOrderFillUnitRepository;
		}

		public async Task<IEnumerable<ResourceOrder>> GetAllOpenOrdersAsync()
		{
			try
			{

				var selectFunction = new Func<DbConnection, Task<IEnumerable<ResourceOrder>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();


					var query = _queryFactory.GetQuery<SelectAllOpenOrdersQuery>();

					return await x.QueryAsync<ResourceOrder>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}

		}

		public async Task<IEnumerable<ResourceOrder>> GetAllNonDepartmentOpenVisibleOrdersAsync(int departmentId)
		{
			try
			{

				var selectFunction = new Func<DbConnection, Task<IEnumerable<ResourceOrder>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("CurrentDate", DateTime.UtcNow.AddDays(-7));

					var query = _queryFactory.GetQuery<SelectAllOpenNonDVisibleOrdersQuery>();

					return await x.QueryAsync<ResourceOrder>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}

		}

		public async Task<ResourceOrder> GetOrderById(int id)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	var query = @"SELECT * FROM ResourceOrders ro 
			//								INNER JOIN Departments d ON d.DepartmentId = ro.DepartmentId
			//							WHERE ResourceOrderId = @id

			//							SELECT * FROM ResourceOrderItems roi
			//								LEFT OUTER JOIN ResourceOrderFills rof ON rof.ResourceOrderItemId = roi.ResourceOrderItemId
			//								INNER JOIN Departments d ON d.DepartmentId = rof.DepartmentId
			//								LEFT OUTER JOIN ResourceOrderFillUnits rou ON rou.ResourceOrderFillId = rof.ResourceOrderFillId
			//							WHERE ResourceOrderId = @id";

			//	var multi = await db.QueryMultipleAsync(query, new { id = id });

			//	var order = multi.Read<ResourceOrder, Department, ResourceOrder>((o, d) => { o.Department = d; return o; }, splitOn: "DepartmentId").FirstOrDefault();

			//	if (order != null)
			//	{
			//		var lookupFills = new Dictionary<int, ResourceOrderFill>();
			//		var lookupUnits = new Dictionary<int, ResourceOrderFillUnit>();

			//		var items = multi.Read<ResourceOrderItem, ResourceOrderFill, Department, ResourceOrderFillUnit, ResourceOrderItem>(
			//			(item, fill, d, unit) =>
			//			{
			//				Func<ResourceOrderFill, Department, ResourceOrderFillUnit, ResourceOrderFill> processFill =
			//					(childOrderFill, childFillDepartment, childOrderUnit) =>
			//					{
			//						ResourceOrderFillUnit orderFillUnit;
			//						if (!lookupUnits.ContainsKey(childOrderUnit.ResourceOrderFillUnitId))
			//						{
			//							lookupUnits.Add(childOrderUnit.ResourceOrderFillUnitId, childOrderUnit);
			//							orderFillUnit = childOrderUnit;
			//							childOrderFill.Units = new List<ResourceOrderFillUnit>();
			//						}
			//						else
			//						{
			//							orderFillUnit = lookupUnits[childOrderUnit.ResourceOrderFillUnitId];
			//						}
			//						childOrderFill.Units.Add(orderFillUnit);

			//						childOrderFill.Department = childFillDepartment;

			//						return fill;
			//					};

			//				item.Fills = new List<ResourceOrderFill>();

			//				if (fill != null)
			//				{
			//					ResourceOrderFill orderFill;
			//					if (!lookupFills.ContainsKey(fill.ResourceOrderFillId))
			//					{
			//						lookupFills.Add(fill.ResourceOrderFillId, fill);
			//						orderFill = processFill(fill, d, unit);
			//					}
			//					else
			//					{
			//						orderFill = processFill(lookupFills[fill.ResourceOrderFillId], d, unit);
			//					}

			//					item.Fills.Add(orderFill);
			//				}

			//				return item;

			//			}, splitOn: "ResourceOrderItemId, DepartmentId, ResourceOrderFillId").Distinct();


			//		if (items != null)
			//			order.Items = items.ToList();

			//		return order;
			//	}

			//	return null;
			//}

			return null;
		}

		public async Task<IEnumerable<ResourceOrderItem>> GetAllItemsByResourceOrderIdAsync(int resourceOrderId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ResourceOrderItem>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ResourceOrderId", resourceOrderId);

					var query = _queryFactory.GetQuery<SelectItemsByResourceOrderIdQuery>();

					var messageDictionary = new Dictionary<int, ResourceOrderItem>();
					var result = await x.QueryAsync<ResourceOrderItem, ResourceOrderFill, ResourceOrderItem>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ResourceOrderItemMapping(messageDictionary),
						splitOn: "ResourceOrderFillId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value);

					return result;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}


		public async Task<List<ResourceOrder>> GetAllOpenOrdersByRange(int departmentId)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders ro WHERE ro.CloseDate IS NULL AND ro.Visibility = 0 AND ro.DepartmentId != @departmentId", new { departmentId = departmentId });
			//	return result.ToList();
			//}

			return null;
		}

		public async Task<List<ResourceOrder>> GetAllOpenOrdersUnrestricted(int departmentId)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders ro WHERE ro.CloseDate IS NULL AND ro.Visibility = 3 AND ro.DepartmentId != @departmentId", new { departmentId = departmentId });
			//	return result.ToList();
			//}

			return null;
		}

		public async Task<List<ResourceOrder>> GetAllOpenOrdersLinked(int departmentId)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders ro WHERE ro.CloseDate IS NULL AND ro.DepartmentId != @departmentId AND ro.Visibility = 2 AND ro.DepartmentId IN (SELECT dl.DepartmentId FROM DepartmentLinks dl WHERE dl.LinkedDepartmentId = @departmentId AND dl.LinkEnabled = 1 AND dl.DepartmentShareOrders = 1)", new { departmentId = departmentId });
			//	return result.ToList();
			//}

			return null;
		}

		public async Task<ResourceOrder> SaveOrderAsync(ResourceOrder order, CancellationToken cancellationToken = default(CancellationToken))
		{

			if (order.ResourceOrderId == 0)
			{
				var resourceOrder = await InsertAsync(order, cancellationToken);

				if (resourceOrder != null)
				{
					foreach (var item in order.Items)
					{
						item.ResourceOrderId = resourceOrder.ResourceOrderId;
						var itemId = await _resourceOrderItemRepository.InsertAsync(item, cancellationToken);

						if (itemId != null)
							item.ResourceOrderItemId = itemId.ResourceOrderItemId;
					}
				}
			}
			else
			{
				await UpdateAsync(order, cancellationToken);

				foreach (var item in order.Items)
				{
					await _resourceOrderItemRepository.UpdateAsync(item, cancellationToken);
				}
			}

			return order;
		}

		public async Task<ResourceOrderFill> SaveFillAsync(ResourceOrderFill fill, CancellationToken cancellationToken = default(CancellationToken))
		{

			if (fill.ResourceOrderFillId == 0)
			{
				var orderFill = await _resourceOrderFillRepository.InsertAsync(fill, cancellationToken);

				if (orderFill != null)
				{
					fill.ResourceOrderFillId = orderFill.ResourceOrderFillId;
					foreach (var unit in fill.Units)
					{
						unit.ResourceOrderFillId = orderFill.ResourceOrderFillId;
						var fillUnit = await _resourceOrderFillUnitRepository.InsertAsync(unit, cancellationToken);

						if (fillUnit != null)
							unit.ResourceOrderFillUnitId = fillUnit.ResourceOrderFillUnitId;
					}
				}
			}
			else
			{
				await _resourceOrderFillRepository.UpdateAsync(fill, cancellationToken);

				foreach (var unit in fill.Units)
				{
					await _resourceOrderFillUnitRepository.SaveOrUpdateAsync(unit, cancellationToken);
				}
			}


			return fill;
		}

		private static Func<ResourceOrderItem, ResourceOrderFill, ResourceOrderItem> ResourceOrderItemMapping(Dictionary<int, ResourceOrderItem> dictionary)
		{
			return new Func<ResourceOrderItem, ResourceOrderFill, ResourceOrderItem>((resourceOrderItem, resourceOrderFill) =>
			{
				var dictionaryResourceOrderItem = default(ResourceOrderItem);

				if (resourceOrderFill != null)
				{
					if (dictionary.TryGetValue(resourceOrderItem.ResourceOrderItemId, out dictionaryResourceOrderItem))
					{
						if (dictionaryResourceOrderItem.Fills.All(x => x.ResourceOrderFillId != resourceOrderFill.ResourceOrderFillId))
							dictionaryResourceOrderItem.Fills.Add(resourceOrderFill);
					}
					else
					{
						if (resourceOrderItem.Fills == null)
							resourceOrderItem.Fills = new List<ResourceOrderFill>();

						resourceOrderItem.Fills.Add(resourceOrderFill);
						dictionary.Add(resourceOrderItem.ResourceOrderItemId, resourceOrderItem);

						dictionaryResourceOrderItem = resourceOrderItem;
					}
				}
				else
				{
					resourceOrderItem.Fills = new List<ResourceOrderFill>();
					dictionaryResourceOrderItem = resourceOrderItem;
					dictionary.Add(resourceOrderItem.ResourceOrderItemId, resourceOrderItem);
				}

				return dictionaryResourceOrderItem;
			});
		}

		//private static Func<Message, MessageRecipient, Message> ResourceOrderMapping(Dictionary<int, Message> dictionary)
		//{
		//	return new Func<Message, MessageRecipient, Message>((message, messageRecipient) =>
		//	{
		//		var dictionaryMessage = default(Message);

		//		if (messageRecipient != null)
		//		{
		//			if (dictionary.TryGetValue(message.MessageId, out dictionaryMessage))
		//			{
		//				if (dictionaryMessage.MessageRecipients.All(x => x.MessageRecipientId != messageRecipient.MessageRecipientId))
		//					dictionaryMessage.MessageRecipients.Add(messageRecipient);
		//			}
		//			else
		//			{
		//				if (message.MessageRecipients == null)
		//					message.MessageRecipients = new List<MessageRecipient>();

		//				message.MessageRecipients.Add(messageRecipient);
		//				dictionary.Add(message.MessageId, message);

		//				dictionaryMessage = message;
		//			}
		//		}
		//		else
		//		{
		//			dictionaryMessage = message;
		//		}

		//		return dictionaryMessage;
		//	});
		//}
	}
}
