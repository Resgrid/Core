using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.DataRepository
{
	public class ResourceOrdersRepository: IResourceOrdersRepository
	{
		public string connectionString = ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>().FirstOrDefault(x => x.Name == "ResgridContext").ConnectionString;

		public async Task<List<ResourceOrder>> GetAll()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders");
				return result.ToList();
			}
		}

		public async Task<List<ResourceOrder>> GetAllOpen()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders WHERE CloseDate IS NULL");
				return result.ToList();
			}
		}

		public async Task<ResourceOrder> GetOrderById(int id)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				//var orderResult = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders WHERE ResourceOrderId = @id", new { id = id });

				//return await db.GetAsync<ResourceOrder>(id);

				//return orderResult.FirstOrDefault();

				//var query = @"SELECT ro.*,
				//				   d.*,
				//				   roi.*,
				//				   rof.*,
				//				   rou.*
				//			FROM ResourceOrders ro
				//			JOIN Departments d ON ro.DepartmentId = d.DepartmentId
				//			LEFT OUTER JOIN ResourceOrderItems roi ON ro.ResourceOrderId = roi.ResourceOrderId
				//			LEFT OUTER JOIN ResourceOrderFills rof ON roi.ResourceOrderItemId = rof.ResourceOrderItemId
				//			LEFT OUTER JOIN ResourceOrderFillUnits rou ON rou.ResourceOrderFillId = rof.ResourceOrderFillId
				//			WHERE ro.ResourceOrderId = @id";




				//var orderResult = await db.QueryAsync<ResourceOrder, Department, ResourceOrderItem, ResourceOrderFill, ResourceOrderFillUnit, ResourceOrder>(query, (order, d, item, fill, unit) =>
				//{
				//	order.Department = d;
				//	order.Items = item.T
				//	return order;
				//}, new { id = id });



				var query = @"SELECT * FROM ResourceOrders ro 
											INNER JOIN Departments d ON d.DepartmentId = ro.DepartmentId
										WHERE ResourceOrderId = @id

										SELECT * FROM ResourceOrderItems roi
											LEFT OUTER JOIN ResourceOrderFills rof ON rof.ResourceOrderItemId = roi.ResourceOrderItemId
											INNER JOIN Departments d ON d.DepartmentId = rof.DepartmentId
											LEFT OUTER JOIN ResourceOrderFillUnits rou ON rou.ResourceOrderFillId = rof.ResourceOrderFillId
										WHERE ResourceOrderId = @id";

				var multi = await db.QueryMultipleAsync(query, new { id = id });

				var order = multi.Read<ResourceOrder, Department, ResourceOrder>((o, d) => { o.Department = d; return o; }, splitOn: "DepartmentId").FirstOrDefault();

				if (order != null)
				{
					var lookupFills = new Dictionary<int, ResourceOrderFill>();
					var lookupUnits = new Dictionary<int, ResourceOrderFillUnit>();

					var items = multi.Read<ResourceOrderItem, ResourceOrderFill, Department, ResourceOrderFillUnit, ResourceOrderItem>(
						(item, fill, d, unit) =>
						{
							Func<ResourceOrderFill, Department, ResourceOrderFillUnit, ResourceOrderFill> processFill =
								(childOrderFill, childFillDepartment, childOrderUnit) =>
								{
									ResourceOrderFillUnit orderFillUnit;
									if (!lookupUnits.ContainsKey(childOrderUnit.ResourceOrderFillUnitId))
									{
										lookupUnits.Add(childOrderUnit.ResourceOrderFillUnitId, childOrderUnit);
										orderFillUnit = childOrderUnit;
										childOrderFill.Units = new List<ResourceOrderFillUnit>();
									}
									else
									{
										orderFillUnit = lookupUnits[childOrderUnit.ResourceOrderFillUnitId];
									}
									childOrderFill.Units.Add(orderFillUnit);

									childOrderFill.Department = childFillDepartment;

									return fill;
								};

							item.Fills = new List<ResourceOrderFill>();

							if (fill != null)
							{
								ResourceOrderFill orderFill;
								if (!lookupFills.ContainsKey(fill.ResourceOrderFillId))
								{
									lookupFills.Add(fill.ResourceOrderFillId, fill);
									orderFill = processFill(fill, d, unit);
								}
								else
								{
									orderFill = processFill(lookupFills[fill.ResourceOrderFillId], d, unit);
								}

								item.Fills.Add(orderFill);
							}

							return item;

						}, splitOn: "ResourceOrderItemId, DepartmentId, ResourceOrderFillId").Distinct();

					//order.Department = department;

					if (items != null)
						order.Items = items.ToList();

					return order;
				}

				return null;
			}
		}

		public async Task<List<ResourceOrder>> GetOrdersByDepartmentId(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders WHERE DepartmentId = @departmentId", new { departmentId = departmentId });
				return result.ToList();
			}
		}

		public async Task<List<ResourceOrder>> GetOpenOrdersByDepartmentId(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders WHERE DepartmentId = @departmentId AND CloseDate IS NULL", new { departmentId = departmentId });
				return result.ToList();
			}
		}

		public async Task<ResourceOrderSetting> GetOrderSettingById(int id)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrderSetting>($"SELECT * FROM ResourceOrderSettings WHERE ResourceOrderSettingId = @id", new { id = id });
				return result.FirstOrDefault();
			}
		}

		public async Task<ResourceOrderSetting> GetOrderSettingByDepartmentId(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrderSetting>($"SELECT * FROM ResourceOrderSettings WHERE DepartmentId = @departmentId", new { departmentId = departmentId });
				return result.FirstOrDefault();
			}
		}

		public async Task<List<ResourceOrder>> GetAllOpenOrdersByRange(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders ro WHERE ro.CloseDate IS NULL AND ro.Visibility = 0 AND ro.DepartmentId != @departmentId", new { departmentId = departmentId });
				return result.ToList();
			}
		}

		public async Task<List<ResourceOrder>> GetAllOpenOrdersUnrestricted(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders ro WHERE ro.CloseDate IS NULL AND ro.Visibility = 3 AND ro.DepartmentId != @departmentId", new { departmentId = departmentId });
				return result.ToList();
			}
		}

		public async Task<List<ResourceOrder>> GetAllOpenOrdersLinked(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders ro WHERE ro.CloseDate IS NULL AND ro.DepartmentId != @departmentId AND ro.Visibility = 2 AND ro.DepartmentId IN (SELECT dl.DepartmentId FROM DepartmentLinks dl WHERE dl.LinkedDepartmentId = @departmentId AND dl.LinkEnabled = 1 AND dl.DepartmentShareOrders = 1)", new { departmentId = departmentId });
				return result.ToList();
			}
		}
		public async Task UpdateFillStatus(int fillId, string userId, bool accepted)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				await db.ExecuteAsync($"UPDATE [ResourceOrderFills] SET [Accepted] = @accepted, [AcceptedOn] = @acceptedOn, [AcceptedUserId] = @userId WHERE ResourceOrderFillId = @fillId", 
					new { fillId = fillId, userId = userId, accepted = accepted, acceptedOn = DateTime.UtcNow });
			}
		}

		public async Task<ResourceOrderSetting> SaveSettings(ResourceOrderSetting settings)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				if (settings.ResourceOrderSettingId == 0)
					await db.InsertAsync<ResourceOrderSetting>(settings);
				else
					await db.UpdateAsync(settings);
			}

			return settings;
		}

		public async Task<ResourceOrder> SaveOrder(ResourceOrder order)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				if (order.ResourceOrderId == 0)
				{
					var id = await db.InsertAsync<ResourceOrder>(order);

					if (id != null)
					{
						order.ResourceOrderId = id.Value;
						foreach (var item in order.Items)
						{
							item.ResourceOrderId = id.Value;
							var itemId = await db.InsertAsync<ResourceOrderItem>(item);

							if (itemId != null)
								item.ResourceOrderItemId = itemId.Value;
						}
					}
				}
				else
				{
					await db.UpdateAsync(order);

					foreach (var item in order.Items)
					{
						await db.UpdateAsync(item);
					}
				}
			}

			return order;
		}

		public async Task<ResourceOrderFill> SaveFill(ResourceOrderFill fill)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				if (fill.ResourceOrderFillId == 0)
				{
					var id = await db.InsertAsync<ResourceOrderFill>(fill);

					if (id != null)
					{
						fill.ResourceOrderFillId = id.Value;
						foreach (var unit in fill.Units)
						{
							unit.ResourceOrderFillId = id.Value;
							var itemId = await db.InsertAsync<ResourceOrderFillUnit>(unit);

							if (itemId != null)
								unit.ResourceOrderFillUnitId = itemId.Value;
						}
					}
				}
				else
				{
					await db.UpdateAsync(fill);

					foreach (var unit in fill.Units)
					{
						await db.UpdateAsync(unit);
					}
				}
			}

			return fill;
		}
	}
}