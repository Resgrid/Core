using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using Dapper;

namespace Resgrid.Repositories.DataRepository
{
	public class PlansRepository : RepositoryBase<Plan>, IPlansRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public PlansRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public Plan GetPlanByExternalId(string externalId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<Plan>($"SELECT TOP 1 * FROM Plans WHERE ExternalId = @externalId", new { externalId = externalId }).FirstOrDefault();
			}
		}

		public async Task<Plan> GetPlanByExternalIdAsync(string externalId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var plan = await db.QueryAsync<Plan>($"SELECT TOP 1 * FROM Plans WHERE ExternalId = @externalId", new {externalId = externalId});

				return plan.FirstOrDefault();
			}
		}

		public async Task<Plan> GetPlanByIdAsync(int planId)
		{
			Dictionary<int, Plan> lookup = new Dictionary<int, Plan>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT pl.*, lim.* FROM Plans pl
							  LEFT OUTER JOIN PlanLimits lim ON lim.PlanId = pl.PlanId
							  WHERE pl.PlanId = @planId";

				var plans = await db.QueryAsync<Plan, PlanLimit, Plan>(query, (p, pl) =>
				{
					Plan newPlan;

					if (!lookup.TryGetValue(p.PlanId, out newPlan))
					{
						lookup.Add(p.PlanId, p);
						newPlan = p;
					}

					if (p.PlanLimits == null)
						p.PlanLimits = new List<PlanLimit>();

					if (pl != null && !newPlan.PlanLimits.Contains(pl))
					{
						pl.Plan = newPlan;
						newPlan.PlanLimits.Add(pl);
					}

					return newPlan;

				}, new { planId = planId }, splitOn: "PlanLimitId");

				return plans.FirstOrDefault();
			}
		}
	}
}
