using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Shifts;

namespace Resgrid.Repositories.DataRepository
{
	public class ShiftsRepository : RepositoryBase<Shift>, IShiftsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ShiftsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<Shift> GetShiftAndDaysByShiftIdAsync(int shiftId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Shift>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftId", shiftId);

					if (DataConfig.DatabaseType == DatabaseTypes.SqlServer)
						dynamicParameters.Add("JsonResult", null, DbType.String, ParameterDirection.Output, int.MaxValue);

					var query = _queryFactory.GetQuery<SelectShiftAndDaysByShiftIdQuery>();

					var result = await x.QueryAsync<string>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					if (result != null)
					{
						var singleResult = result.FirstOrDefault();

						if (singleResult != null)
						{
							var shifts = JsonConvert.DeserializeObject<IEnumerable<Shift>>(singleResult);

							if (shifts != null)
								return shifts.FirstOrDefault();
						}
					}

					return null;
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

		public async Task<IEnumerable<Shift>> GetShiftAndDaysByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Shift>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					if (DataConfig.DatabaseType == DatabaseTypes.SqlServer)
						dynamicParameters.Add("JsonResult", null, DbType.String, ParameterDirection.Output, int.MaxValue);

					var query = _queryFactory.GetQuery<SelectShiftAndDaysByDIdQuery>();

					var result = await x.QueryAsync<string>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					if (result != null)
					{
						var singleResult = result.FirstOrDefault();
						if (singleResult != null)
						{
							return JsonConvert.DeserializeObject<IEnumerable<Shift>>(singleResult);
						}
					}

					return null;
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

		public async Task<IEnumerable<Shift>> GetAllShiftAndDaysAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Shift>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					if (DataConfig.DatabaseType == DatabaseTypes.SqlServer)
						dynamicParameters.Add("JsonResult", null, DbType.String, ParameterDirection.Output, int.MaxValue);

					var query = _queryFactory.GetQuery<SelectShiftAndDaysQuery>();

					var result = await x.QueryAsync<string>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					if (result != null)
					{
						var singleResult = result.FirstOrDefault();
						if (singleResult != null)
						{
							return JsonConvert.DeserializeObject<IEnumerable<Shift>>(singleResult);
						}
					}

					return null;
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

		private static Func<Shift, ShiftDay, Shift> ShiftDayMapping(Dictionary<int, Shift> dictionary)
		{
			return new Func<Shift, ShiftDay, Shift>((shift, shiftDay) =>
			{
				var dictionaryShift = default(Shift);

				if (shiftDay != null)
				{
					if (dictionary.TryGetValue(shift.ShiftId, out dictionaryShift))
					{
						if (dictionaryShift.Days.All(x => x.ShiftDayId != shiftDay.ShiftDayId))
							dictionaryShift.Days.Add(shiftDay);
					}
					else
					{
						if (shift.Days == null)
							shift.Days = new List<ShiftDay>();

						shift.Days.Add(shiftDay);
						dictionary.Add(shift.ShiftId, shift);

						dictionaryShift = shift;
					}
				}
				else
				{
					shift.Days = new List<ShiftDay>();
					dictionaryShift = shift;
					dictionary.Add(shift.ShiftId, shift);
				}

				return dictionaryShift;
			});
		}
	}
}
