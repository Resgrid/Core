using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Shifts;

namespace Resgrid.Repositories.DataRepository
{
	public class ShiftStaffingRepository : RepositoryBase<ShiftStaffing>, IShiftStaffingRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ShiftStaffingRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<ShiftStaffing> GetShiftStaffingByShiftDayAsync(int shiftId, DateTime shiftDay)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ShiftStaffing>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftId", shiftId);
					dynamicParameters.Add("ShiftDay", shiftDay);

					var query = _queryFactory.GetQuery<SelectShiftStaffingByDayQuery>();

					var dictionary = new Dictionary<int, ShiftStaffing>();
					var result = await x.QueryAsync<ShiftStaffing, ShiftStaffingPerson, ShiftStaffing>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftStaffingMapping(dictionary),
						splitOn: "ShiftStaffingPersonId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value).FirstOrDefault();

					return result.FirstOrDefault();
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

		private static Func<ShiftStaffing, ShiftStaffingPerson, ShiftStaffing> ShiftStaffingMapping(Dictionary<int, ShiftStaffing> dictionary)
		{
			return new Func<ShiftStaffing, ShiftStaffingPerson, ShiftStaffing>((shiftStaffing, shiftStaffingPerson) =>
			{
				var dictionaryShiftStaffing = default(ShiftStaffing);

				if (shiftStaffingPerson != null)
				{
					if (dictionary.TryGetValue(shiftStaffing.ShiftStaffingId, out dictionaryShiftStaffing))
					{
						if (dictionaryShiftStaffing.Personnel.All(x => x.ShiftStaffingPersonId != shiftStaffingPerson.ShiftStaffingPersonId))
							dictionaryShiftStaffing.Personnel.Add(shiftStaffingPerson);
					}
					else
					{
						if (shiftStaffing.Personnel == null)
							shiftStaffing.Personnel = new List<ShiftStaffingPerson>();

						shiftStaffing.Personnel.Add(shiftStaffingPerson);
						dictionary.Add(shiftStaffing.ShiftStaffingId, shiftStaffing);

						dictionaryShiftStaffing = shiftStaffing;
					}
				}
				else
				{
					shiftStaffing.Personnel = new List<ShiftStaffingPerson>();
					dictionaryShiftStaffing = shiftStaffing;
					dictionary.Add(shiftStaffing.ShiftStaffingId, shiftStaffing);
				}

				return dictionaryShiftStaffing;
			});
		}
	}
}
