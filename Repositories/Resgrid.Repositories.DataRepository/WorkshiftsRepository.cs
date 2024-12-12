using System;
using System.Collections.Generic;
using System.Data;
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
using Resgrid.Repositories.DataRepository.Queries.Workshifts;

namespace Resgrid.Repositories.DataRepository
{
	public class WorkshiftsRepository : RepositoryBase<Workshift>, IWorkshiftsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public WorkshiftsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<Workshift>> GetAllWorkshiftAndDaysByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Workshift>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectAllWorkshiftsAndDaysByDidQuery>();

					var dictionary = new Dictionary<string, Workshift>();
					var result = await x.QueryAsync<Workshift, WorkshiftDay, Workshift>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: WorkshiftDaysMapping(dictionary),
						splitOn: "WorkshiftDayId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value);

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

		public async Task<Workshift> GetWorkshiftByIdAsync(string workshiftId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Workshift>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("WorkshiftId", workshiftId);
					
					var query = _queryFactory.GetQuery<SelectWorkshiftByIdQuery>();

					var dictionary = new Dictionary<string, Workshift>();
					var result = await x.QueryAsync<Workshift, WorkshiftDay, Workshift>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: WorkshiftDaysMapping(dictionary),
						splitOn: "WorkshiftDayId");

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

		private static Func<Workshift, WorkshiftDay, Workshift> WorkshiftDaysMapping(Dictionary<string, Workshift> dictionary)
		{
			return new Func<Workshift, WorkshiftDay, Workshift>((workshift, workshiftDay) =>
			{
				var dictionaryWorkshift = default(Workshift);

				if (workshiftDay != null)
				{
					if (dictionary.TryGetValue(workshift.WorkshiftId, out dictionaryWorkshift))
					{
						if (dictionaryWorkshift.Days.All(x => x.WorkshiftDayId != workshiftDay.WorkshiftDayId))
							dictionaryWorkshift.Days.Add(workshiftDay);
					}
					else
					{
						if (workshift.Days == null)
							workshift.Days = new List<WorkshiftDay>();

						workshift.Days.Add(workshiftDay);
						dictionary.Add(workshift.WorkshiftId, workshift);

						dictionaryWorkshift = workshift;
					}
				}
				else
				{
					workshift.Days = new List<WorkshiftDay>();
					dictionaryWorkshift = workshift;
					dictionary.Add(workshift.WorkshiftId, workshift);
				}

				return dictionaryWorkshift;
			});
		}
	}
}
