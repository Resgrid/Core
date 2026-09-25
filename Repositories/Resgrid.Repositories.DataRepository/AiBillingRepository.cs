using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Enhanced AI add-on billing accounts (M0238). Same shape and transaction rules as BusinessOperationsBillingRepository.</summary>
	public sealed class AiBillingRepository : RmsRepositoryBase<PaymentAddon>, IAiBillingRepository
	{
		public AiBillingRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }

		public Task LockDepartmentAsync(int departmentId) => LockRecordsDepartmentAsync(departmentId, default);

		public Task<AiBillingAccount> GetAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<AiBillingAccount>($"SELECT * FROM {Tbl("AiBillingAccounts")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, default);

		public async Task<AiBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId)
		{
			customerId = string.IsNullOrWhiteSpace(customerId) ? null : customerId;
			subscriptionId = string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId;
			checkoutId = string.IsNullOrWhiteSpace(checkoutId) ? null : checkoutId;
			if (customerId == null && subscriptionId == null && checkoutId == null) return null;
			var matches = (await QueryAsync<AiBillingAccount>(
				$"SELECT * FROM {Tbl("AiBillingAccounts")} WHERE {Col("Provider")}={P}Provider AND ({Col("CustomerId")}={P}CustomerId OR {Col("SubscriptionId")}={P}SubscriptionId OR {Col("CheckoutId")}={P}CheckoutId)",
				new { Provider = provider, CustomerId = customerId, SubscriptionId = subscriptionId, CheckoutId = checkoutId }, default)).ToList();
			if (matches.Count > 1) throw new InvalidOperationException("Enhanced AI billing ownership is ambiguous.");
			return matches.SingleOrDefault();
		}

		public async Task SaveAsync(AiBillingAccount account)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Enhanced AI billing writes require the department transaction.");
			var columns = typeof(AiBillingAccount).GetProperties().Select(p => p.Name).ToArray();
			var existing = await GetAsync(account.DepartmentId);
			if (await ExecuteAsync(existing == null
					? $"INSERT INTO {Tbl("AiBillingAccounts")} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})"
					: $"UPDATE {Tbl("AiBillingAccounts")} SET {string.Join(",", columns.Where(c => c != "DepartmentId").Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId",
				account, default) != 1)
				throw new InvalidOperationException("Enhanced AI billing account could not be saved.");
		}

		public async Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId) =>
			(await QueryAsync<PaymentAddon>($"SELECT * FROM {Tbl("PaymentAddons")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("PlanAddonId")}={P}PlanAddonId", new { DepartmentId = departmentId, PlanAddonId = planAddonId }, default)).ToList();

		public async Task SavePaymentAsync(PaymentAddon payment, bool insert)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Enhanced AI billing writes require the department transaction.");
			var columns = typeof(PaymentAddon).GetProperties().Where(p => p.CanWrite && !payment.IgnoredProperties.Contains(p.Name)).Select(p => p.Name).ToArray();
			if (await ExecuteAsync(insert
					? $"INSERT INTO {Tbl("PaymentAddons")} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})"
					: $"UPDATE {Tbl("PaymentAddons")} SET {string.Join(",", columns.Where(c => c != "DepartmentId" && c != "PaymentAddonId").Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("PaymentAddonId")}={P}PaymentAddonId AND {Col("PlanAddonId")}={P}PlanAddonId",
				payment, default) != 1)
				throw new InvalidOperationException("Enhanced AI billing payment could not be saved.");
		}
	}
}
