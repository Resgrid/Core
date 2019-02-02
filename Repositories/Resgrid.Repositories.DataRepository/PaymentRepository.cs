using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class PaymentRepository : RepositoryBase<Payment>, IPaymentRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public PaymentRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public DepartmentPlanCount GetDepartmentPlanCounts(int departmentId)
		{
			var data = db.SqlQuery<DepartmentPlanCount>(@"SELECT 
										(SELECT COUNT(*) FROM DepartmentMembers dm WHERE dm.DepartmentId = @departmentId AND IsDisabled = 1) AS 'UsersCount',
										(SELECT COUNT(*) FROM DepartmentGroups dg WHERE dg.DepartmentId = @departmentId) AS 'GroupsCount',
										(SELECT COUNT(*) FROM Units u WHERE u.DepartmentId = @departmentId) AS 'UnitsCount'",
				new SqlParameter("@departmentId", departmentId));

			return data.FirstOrDefault();
		}

		public Payment GetLatestPaymentForDepartment(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var data = db.Query<Payment>(@"SELECT TOP 1 * FROM Payments
											  WHERE DepartmentId = @departmentId AND EffectiveOn <= @currentUtcDate AND EndingOn >= @currentUtcDate
											  ORDER BY PlanId DESC, PaymentId DESC",
					new
					{
						departmentId = departmentId,
						currentUtcDate = DateTime.UtcNow
					});

				return data.FirstOrDefault();
			}
		}

		public async Task<Payment> GetLatestPaymentForDepartmentAsync(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var data = await db.QueryAsync<Payment>(@"SELECT TOP 1 * FROM Payments
											  WHERE DepartmentId = @departmentId AND EffectiveOn <= @currentUtcDate AND EndingOn >= @currentUtcDate
											  ORDER BY PlanId DESC, PaymentId DESC",
					new
					{
						departmentId = departmentId,
						currentUtcDate = DateTime.UtcNow
					});

				return data.FirstOrDefault();
			}
		}

		public async Task<Plan> GetLatestPlanForDepartmentAsync(int departmentId)
		{
			Dictionary<int, Plan> lookup = new Dictionary<int, Plan>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT pl.*, lim.* FROM Plans pl
								INNER JOIN Payments pay ON pay.PaymentId = 
														(
															 SELECT TOP 1 pay1.PaymentId
															 FROM Payments pay1
															 WHERE pay1.DepartmentId = @departmentId AND pay1.EffectiveOn <= @currentUtcDate AND pay1.EndingOn >= @currentUtcDate
															 ORDER BY pay1.PlanId DESC, pay1.PaymentId DESC
														 )
								LEFT OUTER JOIN PlanLimits lim ON lim.PlanId = pl.PlanId
								WHERE pl.PlanId = pay.PlanId";

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

				}, new { departmentId = departmentId, currentUtcDate = DateTime.UtcNow}, splitOn: "PlanLimitId");

				return plans.FirstOrDefault();
			}
		}

		public Payment GetPaymentByTransactionId(string transactionId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var data = db.Query<Payment>(@"SELECT TOP 1 * FROM Payments
											  WHERE TransactionId = @transactionId",
				new
				{
					transactionId = transactionId
				});

				return data.FirstOrDefault();
			}
		}

		public void InsertFreePayment(Payment payment)
		{
			if (payment == null || payment.PaymentId != 0)
				return;

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				db.Execute(@"INSERT INTO INSERT INTO [dbo].[Payments]
								   ([DepartmentId]
								   ,[PlanId]
								   ,[Method]
								   ,[IsTrial]
								   ,[PurchaseOn]
								   ,[PurchasingUserId]
								   ,[TransactionId]
								   ,[Successful]
								   ,[Data]
								   ,[IsUpgrade]
								   ,[Description]
								   ,[EffectiveOn]
								   ,[Amount]
								   ,[Payment_PaymentId]
								   ,[EndingOn]
								   ,[Cancelled]
								   ,[CancelledOn]
								   ,[CancelledData]
								   ,[UpgradedPaymentId]
								   ,[SubscriptionId]) 
									VALUES (@departmentId
											,@planId
											,@method
											,@isTrial
											,@purchaseOn
											,@purchasingUserId
											,@transactionId
											,1
											,NULL
											,0
											,@description
											,@effectiveOn
											,0
											,NULL
											,@endingOn
											,0
											,NULL
											,NULL
											,NULL
											,NULL)", new
				{
					departmentId = payment.DepartmentId,
					planId = payment.PaymentId,
					method = payment.Method,
					isTrial = payment.IsTrial,
					purchaseOn = payment.PurchaseOn,
					purchasingUserId = payment.PurchasingUserId,
					transactionId = payment.TransactionId,
					description = payment.Description,
					effectiveOn = payment.EffectiveOn,
					endingOn = payment.EndingOn
				});
			}
		}

		public void InsertPayment(Payment payment)
		{
			if (payment == null || payment.PaymentId != 0)
				return;

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				db.Execute(@"INSERT INTO [Payments]
								   ([DepartmentId]
								   ,[PlanId]
								   ,[Method]
								   ,[IsTrial]
								   ,[PurchaseOn]
								   ,[PurchasingUserId]
								   ,[TransactionId]
								   ,[Successful]
								   ,[Data]
								   ,[IsUpgrade]
								   ,[Description]
								   ,[EffectiveOn]
								   ,[Amount]
								   ,[Payment_PaymentId]
								   ,[EndingOn]
								   ,[Cancelled]
								   ,[CancelledOn]
								   ,[CancelledData]
								   ,[UpgradedPaymentId]
								   ,[SubscriptionId]) 
									VALUES (@departmentId
											,@planId
											,@method
											,@isTrial
											,@purchaseOn
											,@purchasingUserId
											,@transactionId
											,@successful
											,@data
											,@isUpgrade
											,@description
											,@effectiveOn
											,@ammount
											,NULL
											,@endingOn
											,@cancelled
											,@cancelledOn
											,@cancelledData
											,@upgradedPaymentId
											,@subscriptionId)", new
				{
					departmentId = payment.DepartmentId,
					planId = payment.PlanId,
					method = payment.Method,
					isTrial = payment.IsTrial,
					purchaseOn = payment.PurchaseOn,
					purchasingUserId = payment.PurchasingUserId,
					transactionId = payment.TransactionId,
					successful = payment.Successful,
					data = payment.Data,
					isUpgrade = payment.IsUpgrade,
					description = payment.Description,
					effectiveOn = payment.EffectiveOn,
					ammount = payment.Amount,
					endingOn = payment.EndingOn,
					cancelled = payment.Cancelled,
					cancelledOn = payment.CancelledOn,
					cancelledData = payment.CancelledData,
					upgradedPaymentId = payment.UpgradedPaymentId,
					subscriptionId = payment.SubscriptionId
				});
			}
		}

		public void UpdatePayment(Payment payment)
		{
			if (payment == null || payment.PaymentId != 0)
				return;

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				db.Execute(@"UPDATE [dbo].[Payments]
						   SET [DepartmentId] = @departmentId
							  ,[PlanId] = @planId
							  ,[Method] = @method
							  ,[IsTrial] = @isTrial
							  ,[PurchaseOn] = @purchaseOn
							  ,[PurchasingUserId] = @purchasingUserId
							  ,[TransactionId] = @transactionId
							  ,[Successful] = @successful
							  ,[Data] = @data
							  ,[IsUpgrade] = @isUpgrade
							  ,[Description] = @description
							  ,[EffectiveOn] = @effectiveOn
							  ,[Amount] = @ammount
							  ,[EndingOn] = @endingOn
							  ,[Cancelled] = @cancelled
							  ,[CancelledOn] = @cancelledOn
							  ,[CancelledData] = @cancelledData
							  ,[UpgradedPaymentId] = @upgradedPaymentId
							  ,[SubscriptionId] = @subscriptionId
						 WHERE PaymentId = @paymentId", new
				{
					departmentId = payment.DepartmentId,
					planId = payment.PlanId,
					method = payment.Method,
					isTrial = payment.IsTrial,
					purchaseOn = payment.PurchaseOn,
					purchasingUserId = payment.PurchasingUserId,
					transactionId = payment.TransactionId,
					successful = payment.Successful,
					data = payment.Data,
					isUpgrade = payment.IsUpgrade,
					description = payment.Description,
					effectiveOn = payment.EffectiveOn,
					ammount = payment.Amount,
					endingOn = payment.EndingOn,
					cancelled = payment.Cancelled,
					cancelledOn = payment.CancelledOn,
					cancelledData = payment.CancelledData,
					upgradedPaymentId = payment.UpgradedPaymentId,
					subscriptionId = payment.SubscriptionId,
					paymentId = payment.PaymentId
				});
			}
		}
	}
}
