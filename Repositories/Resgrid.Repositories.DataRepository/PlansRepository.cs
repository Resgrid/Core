using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Plans;

namespace Resgrid.Repositories.DataRepository
{
	public class PlansRepository : RepositoryBase<Plan>, IPlansRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public PlansRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<Plan> GetPlanByPlanIdAsync(int planId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Plan>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("PlanId", planId);

					var query = _queryFactory.GetQuery<SelectPlanByPlanIdQuery>();

					var dictionary = new Dictionary<int, Plan>();
					var result = await x.QueryAsync<Plan, PlanLimit, Plan>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: PlanLimitMapping(dictionary),
						splitOn: "PlanLimitId");

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

		private static Func<Plan, PlanLimit, Plan> PlanLimitMapping(Dictionary<int, Plan> dictionary)
		{
			return new Func<Plan, PlanLimit, Plan>((plan, planLimit) =>
			{
				var dictionaryPlan = default(Plan);

				if (planLimit != null)
				{
					if (dictionary.TryGetValue(plan.PlanId, out dictionaryPlan))
					{
						if (dictionaryPlan.PlanLimits.All(x => x.PlanLimitId != planLimit.PlanLimitId))
							dictionaryPlan.PlanLimits.Add(planLimit);
					}
					else
					{
						if (plan.PlanLimits == null)
							plan.PlanLimits = new List<PlanLimit>();

						plan.PlanLimits.Add(planLimit);
						dictionary.Add(plan.PlanId, plan);

						dictionaryPlan = plan;
					}
				}
				else
				{
					plan.PlanLimits = new List<PlanLimit>();
					dictionaryPlan = plan;
					dictionary.Add(plan.PlanId, plan);
				}

				return dictionaryPlan;
			});
		}
	}
}
