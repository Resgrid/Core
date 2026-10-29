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
	/// <summary>Advanced Data Protection yearly checkout billing accounts (M0298). Same shape and transaction rules as the Enhanced AI repository.</summary>
	public sealed class AdpBillingRepository : RmsRepositoryBase<PaymentAddon>, IAdpBillingRepository
	{
		public AdpBillingRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }

		public Task LockDepartmentAsync(int departmentId) => LockRecordsDepartmentAsync(departmentId, default);

		public Task<AdpBillingAccount> GetAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<AdpBillingAccount>($"SELECT * FROM {Tbl("AdpBillingAccounts")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, default);

		public async Task<AdpBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId)
		{
			customerId = string.IsNullOrWhiteSpace(customerId) ? null : customerId;
			subscriptionId = string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId;
			checkoutId = string.IsNullOrWhiteSpace(checkoutId) ? null : checkoutId;
			if (customerId == null && subscriptionId == null && checkoutId == null) return null;
			var matches = (await QueryAsync<AdpBillingAccount>(
				$"SELECT * FROM {Tbl("AdpBillingAccounts")} WHERE {Col("Provider")}={P}Provider AND ({Col("CustomerId")}={P}CustomerId OR {Col("SubscriptionId")}={P}SubscriptionId OR {Col("CheckoutId")}={P}CheckoutId)",
				new { Provider = provider, CustomerId = customerId, SubscriptionId = subscriptionId, CheckoutId = checkoutId }, default)).ToList();
			if (matches.Count > 1) throw new InvalidOperationException("ADP billing ownership is ambiguous.");
			return matches.SingleOrDefault();
		}

		public async Task SaveAsync(AdpBillingAccount account)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("ADP billing writes require the department transaction.");
			var columns = typeof(AdpBillingAccount).GetProperties().Select(p => p.Name).ToArray();
			var existing = await GetAsync(account.DepartmentId);
			if (await ExecuteAsync(existing == null
					? $"INSERT INTO {Tbl("AdpBillingAccounts")} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})"
					: $"UPDATE {Tbl("AdpBillingAccounts")} SET {string.Join(",", columns.Where(c => c != "DepartmentId").Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId",
				Stamped(account), default) != 1)
				throw new InvalidOperationException("ADP billing account could not be saved.");
		}

		public async Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId) =>
			(await QueryAsync<PaymentAddon>($"SELECT * FROM {Tbl("PaymentAddons")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("PlanAddonId")}={P}PlanAddonId", new { DepartmentId = departmentId, PlanAddonId = planAddonId }, default)).ToList();

		public async Task SavePaymentAsync(PaymentAddon payment, bool insert)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("ADP billing writes require the department transaction.");
			var columns = typeof(PaymentAddon).GetProperties().Where(p => p.CanWrite && !payment.IgnoredProperties.Contains(p.Name)).Select(p => p.Name).ToArray();
			if (await ExecuteAsync(insert
					? $"INSERT INTO {Tbl("PaymentAddons")} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})"
					: $"UPDATE {Tbl("PaymentAddons")} SET {string.Join(",", columns.Where(c => c != "DepartmentId" && c != "PaymentAddonId").Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("PaymentAddonId")}={P}PaymentAddonId AND {Col("PlanAddonId")}={P}PlanAddonId",
				Stamped(payment), default) != 1)
				throw new InvalidOperationException("ADP billing payment could not be saved.");
		}

		// Billing rows are written by hosts that do not all set Npgsql's legacy timestamp switch, so the row is bound as a
		// copy whose DateTime values went through DatabaseTimestamp (PostgreSQL columns are timestamp without time zone).
		private static T Stamped<T>(T row) where T : new()
		{
			var copy = new T();
			foreach (var property in typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
			{
				var value = property.GetValue(row);
				property.SetValue(copy, value is DateTime timestamp ? DatabaseTimestamp(timestamp) : value);
			}
			return copy;
		}
	}
}
