using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Customer billing profiles (plan B3, registry M0209). Dapper over the shared dialect helpers, Phase A style.</summary>
	public class CustomerBillingProfileRepository : RmsRepositoryBase<CustomerBillingProfile>, ICustomerBillingProfileRepository
	{
		public CustomerBillingProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CustomerBillingProfile> GetByIdForDepartmentAsync(string customerBillingProfileId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<CustomerBillingProfile>(
				$"SELECT * FROM {Tbl("CustomerBillingProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CustomerBillingProfileId")} = {P}Id AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Id = customerBillingProfileId });
		}

		public Task<CustomerBillingProfile> GetByContactIdAsync(string contactId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<CustomerBillingProfile>(
				$"SELECT * FROM {Tbl("CustomerBillingProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, ContactId = contactId });
		}

		public Task<IEnumerable<CustomerBillingProfile>> GetByContactIdsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var ids = InListValue(contactIds);
			if (ids.Length == 0)
				return Task.FromResult<IEnumerable<CustomerBillingProfile>>(new List<CustomerBillingProfile>());

			return QueryAsync<CustomerBillingProfile>(
				$"SELECT * FROM {Tbl("CustomerBillingProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "ContactIds")} AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, ContactIds = ids });
		}

		public Task<IEnumerable<CustomerBillingProfile>> GetAllForDepartmentAsync(int departmentId)
		{
			return QueryAsync<CustomerBillingProfile>(
				$"SELECT * FROM {Tbl("CustomerBillingProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId });
		}

		private static string False => IsPostgres ? "FALSE" : "0";
	}

	/// <summary>Rate cards (plan B3, registry M0209).</summary>
	public class RateCardRepository : RmsRepositoryBase<RateCard>, IRateCardRepository
	{
		public RateCardRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RateCard> GetByIdForDepartmentAsync(string rateCardId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<RateCard>(
				$"SELECT * FROM {Tbl("RateCards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RateCardId")} = {P}Id AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Id = rateCardId });
		}

		public Task<IEnumerable<RateCard>> GetAllForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RateCard>(
				$"SELECT * FROM {Tbl("RateCards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("IsDefault")} DESC, {Col("Name")}",
				new { DepartmentId = departmentId });
		}

		public Task<RateCard> GetDefaultForDepartmentAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<RateCard>(
				$"SELECT * FROM {Tbl("RateCards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDefault")} = {True} AND {Col("Active")} = {True} AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId });
		}

		public Task<int> ClearDefaultAsync(int departmentId, string exceptRateCardId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("RateCards")} SET {Col("IsDefault")} = {False} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RateCardId")} <> {P}Except AND {Col("IsDefault")} = {True}",
				new { DepartmentId = departmentId, Except = exceptRateCardId ?? string.Empty }, cancellationToken);
		}

		private static string False => IsPostgres ? "FALSE" : "0";
		private static string True => IsPostgres ? "TRUE" : "1";
	}

	/// <summary>Rate card items (plan B3, registry M0209).</summary>
	public class RateCardItemRepository : RmsRepositoryBase<RateCardItem>, IRateCardItemRepository
	{
		public RateCardItemRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RateCardItem> GetByIdForDepartmentAsync(string rateCardItemId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<RateCardItem>(
				$"SELECT * FROM {Tbl("RateCardItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RateCardItemId")} = {P}Id AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Id = rateCardItemId });
		}

		public Task<IEnumerable<RateCardItem>> GetByRateCardIdAsync(string rateCardId, int departmentId, bool includeInactive = false)
		{
			var active = includeInactive ? string.Empty : $" AND {Col("Active")} = {True}";
			return QueryAsync<RateCardItem>(
				$"SELECT * FROM {Tbl("RateCardItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RateCardId")} = {P}RateCardId AND {Col("IsDeleted")} = {False}{active} ORDER BY {Col("SortOrder")}, {Col("Name")}",
				new { DepartmentId = departmentId, RateCardId = rateCardId });
		}

		private static string False => IsPostgres ? "FALSE" : "0";
		private static string True => IsPostgres ? "TRUE" : "1";
	}

	/// <summary>Invoices (plan B3, registry M0210).</summary>
	public class InvoiceRepository : RmsRepositoryBase<Invoice>, IInvoiceRepository
	{
		private static readonly int[] OpenStatuses = { (int)InvoiceStatus.Sent, (int)InvoiceStatus.PartiallyPaid, (int)InvoiceStatus.Overdue };

		public InvoiceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<Invoice> GetByIdForDepartmentAsync(string invoiceId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<Invoice>(
				$"SELECT * FROM {Tbl("Invoices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}Id AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Id = invoiceId });
		}

		public Task<Invoice> GetByNumberAsync(int departmentId, int invoiceNumber)
		{
			return QueryFirstOrDefaultAsync<Invoice>(
				$"SELECT * FROM {Tbl("Invoices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceNumber")} = {P}Number AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Number = invoiceNumber });
		}

		private string FilterSql(InvoiceListFilter filter, DynamicParameters parameters)
		{
			var sql = new StringBuilder($"{Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}");
			if (filter == null)
				return sql.ToString();

			var statuses = InListValue(filter.Statuses);
			if (statuses.Length > 0)
			{
				sql.Append($" AND {InList("Status", "Statuses")}");
				parameters.Add("Statuses", statuses);
			}
			if (!string.IsNullOrWhiteSpace(filter.ContactId))
			{
				sql.Append($" AND {Col("ContactId")} = {P}ContactId");
				parameters.Add("ContactId", filter.ContactId);
			}
			if (filter.IssuedFromUtc.HasValue)
			{
				sql.Append($" AND {Col("IssuedOn")} >= {P}IssuedFrom");
				parameters.Add("IssuedFrom", filter.IssuedFromUtc.Value);
			}
			if (filter.IssuedToUtc.HasValue)
			{
				sql.Append($" AND {Col("IssuedOn")} < {P}IssuedTo");
				parameters.Add("IssuedTo", filter.IssuedToUtc.Value);
			}
			return sql.ToString();
		}

		public Task<IEnumerable<Invoice>> GetForDepartmentAsync(int departmentId, InvoiceListFilter filter)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			var where = FilterSql(filter, parameters);
			var skip = Math.Max(0, filter?.Skip ?? 0);
			var take = Math.Clamp(filter?.Take ?? 50, 1, 500);
			parameters.Add("Skip", skip);
			parameters.Add("Take", take);

			return QueryAsync<Invoice>(
				$"SELECT * FROM {Tbl("Invoices")} WHERE {where} ORDER BY {Col("InvoiceNumber")} DESC {Paging()}",
				parameters);
		}

		public Task<int> CountForDepartmentAsync(int departmentId, InvoiceListFilter filter)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			var where = FilterSql(filter, parameters);
			return ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("Invoices")} WHERE {where}", parameters);
		}

		public Task<IEnumerable<Invoice>> GetByContactIdAsync(string contactId, int departmentId)
		{
			return QueryAsync<Invoice>(
				$"SELECT * FROM {Tbl("Invoices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("InvoiceNumber")} DESC",
				new { DepartmentId = departmentId, ContactId = contactId });
		}

		public Task<IEnumerable<Invoice>> GetInvoicesByStatusAsync(int departmentId, int status)
		{
			return QueryAsync<Invoice>(
				$"SELECT * FROM {Tbl("Invoices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}Status AND {Col("IsDeleted")} = {False} ORDER BY {Col("InvoiceNumber")} DESC",
				new { DepartmentId = departmentId, Status = status });
		}

		public Task<IEnumerable<Invoice>> GetOverdueCandidatesAsync(DateTime asOfUtc, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("AsOf", asOfUtc);
			parameters.Add("Statuses", new[] { (int)InvoiceStatus.Sent, (int)InvoiceStatus.PartiallyPaid });
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Clamp(take, 1, 5000));
			return QueryAsync<Invoice>(
				$"SELECT * FROM {Tbl("Invoices")} WHERE {InList("Status", "Statuses")} AND {Col("DueOn")} IS NOT NULL AND {Col("DueOn")} < {P}AsOf AND {Col("IsDeleted")} = {False} ORDER BY {Col("DueOn")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<InvoiceAgingRow>> GetAgingDataAsync(int departmentId)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Statuses", OpenStatuses);
			return QueryAsync<InvoiceAgingRow>(
				$"SELECT {Cols("InvoiceId", "InvoiceNumber", "ContactId", "Status", "DueOn", "Currency", "Total", "AmountPaid")} FROM {Tbl("Invoices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("Status", "Statuses")} AND {Col("IsDeleted")} = {False} ORDER BY {Col("DueOn")}",
				parameters);
		}

		public async Task<bool> HasNonVoidInvoicesForContactAsync(string contactId, int departmentId)
		{
			var count = await ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl("Invoices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("Status")} <> {P}Void AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, ContactId = contactId, Void = (int)InvoiceStatus.Void });
			return count > 0;
		}

		private static string False => IsPostgres ? "FALSE" : "0";
	}

	/// <summary>Invoice line items (plan B3, registry M0210).</summary>
	public class InvoiceLineItemRepository : RmsRepositoryBase<InvoiceLineItem>, IInvoiceLineItemRepository
	{
		public InvoiceLineItemRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<InvoiceLineItem>> GetByInvoiceIdAsync(string invoiceId, int departmentId)
		{
			return QueryAsync<InvoiceLineItem>(
				$"SELECT * FROM {Tbl("InvoiceLineItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}InvoiceId ORDER BY {Col("SortOrder")}",
				new { DepartmentId = departmentId, InvoiceId = invoiceId });
		}

		public Task<IEnumerable<InvoiceLineItem>> GetByCallIdAsync(int callId, int departmentId)
		{
			return QueryAsync<InvoiceLineItem>(
				$"SELECT * FROM {Tbl("InvoiceLineItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId ORDER BY {Col("SortOrder")}",
				new { DepartmentId = departmentId, CallId = callId });
		}

		public Task<int> DeleteByInvoiceIdAsync(string invoiceId, int departmentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("InvoiceLineItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}InvoiceId",
				new { DepartmentId = departmentId, InvoiceId = invoiceId }, cancellationToken);
		}

		public Task<int> DeleteByIdAsync(string invoiceLineItemId, int departmentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("InvoiceLineItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceLineItemId")} = {P}Id",
				new { DepartmentId = departmentId, Id = invoiceLineItemId }, cancellationToken);
		}
	}

	/// <summary>Invoice payments (plan B3, registry M0210).</summary>
	public class InvoicePaymentRepository : RmsRepositoryBase<InvoicePayment>, IInvoicePaymentRepository
	{
		public InvoicePaymentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<InvoicePayment> GetByIdForDepartmentAsync(string invoicePaymentId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<InvoicePayment>(
				$"SELECT * FROM {Tbl("InvoicePayments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoicePaymentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = invoicePaymentId });
		}

		public Task<IEnumerable<InvoicePayment>> GetByInvoiceIdAsync(string invoiceId, int departmentId)
		{
			return QueryAsync<InvoicePayment>(
				$"SELECT * FROM {Tbl("InvoicePayments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}InvoiceId ORDER BY {Col("PaidOn")}, {Col("AddedOn")}",
				new { DepartmentId = departmentId, InvoiceId = invoiceId });
		}

		public Task<InvoicePayment> GetByGatewayTransactionIdAsync(int provider, string gatewayTransactionId)
		{
			return QueryFirstOrDefaultAsync<InvoicePayment>(
				$"SELECT * FROM {Tbl("InvoicePayments")} WHERE {Col("Provider")} = {P}Provider AND {Col("GatewayTransactionId")} = {P}Gateway",
				new { Provider = provider, Gateway = gatewayTransactionId });
		}
	}

	/// <summary>Per-department invoice numbering (plan decision 7): one atomic upsert-and-increment per dialect.</summary>
	public class InvoiceNumberSequenceRepository : RmsRepositoryBase<InvoiceNumberSequence>, IInvoiceNumberSequenceRepository
	{
		public InvoiceNumberSequenceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<int> GetNextNumberAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			// The row stores the NEXT number to hand out. Both statements insert the row on first use (handing out 1)
			// and otherwise advance it, returning the number that was handed out, in a single atomic statement.
			string sql;
			if (IsPostgres)
			{
				sql = $"INSERT INTO {Tbl("InvoiceNumberSequences")} ({Col("DepartmentId")}, {Col("NextInvoiceNumber")}) VALUES ({P}DepartmentId, 2) " +
					  $"ON CONFLICT ({Col("DepartmentId")}) DO UPDATE SET {Col("NextInvoiceNumber")} = {Tbl("InvoiceNumberSequences")}.{Col("NextInvoiceNumber")} + 1 " +
					  $"RETURNING {Col("NextInvoiceNumber")} - 1";
			}
			else
			{
				sql = $"MERGE {Tbl("InvoiceNumberSequences")} WITH (HOLDLOCK) AS t USING (SELECT {P}DepartmentId AS DepartmentId) AS s ON t.[DepartmentId] = s.DepartmentId " +
					  "WHEN MATCHED THEN UPDATE SET [NextInvoiceNumber] = t.[NextInvoiceNumber] + 1 " +
					  "WHEN NOT MATCHED THEN INSERT ([DepartmentId], [NextInvoiceNumber]) VALUES (s.DepartmentId, 2) " +
					  "OUTPUT inserted.[NextInvoiceNumber] - 1;";
			}

			return ScalarAsync<int>(sql, new { DepartmentId = departmentId }, cancellationToken);
		}
	}

	/// <summary>The department's billing identity (plan B1, registry M0209): explicit upsert keyed by DepartmentId.</summary>
	public class DepartmentBillingIdentityRepository : RmsRepositoryBase<DepartmentBillingIdentity>, IDepartmentBillingIdentityRepository
	{
		public DepartmentBillingIdentityRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DepartmentBillingIdentity> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<DepartmentBillingIdentity>(
				$"SELECT * FROM {Tbl("DepartmentBillingIdentities")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });
		}

		public async Task<DepartmentBillingIdentity> UpsertAsync(DepartmentBillingIdentity identity, CancellationToken cancellationToken = default)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));

			var columns = new[]
			{
				"LegalBusinessName", "RemitToAddressId", "TaxRegistrationNumber", "SecondaryTaxRegistrationNumber", "SamUei", "CageCode",
				"WorkersCompAccountNumber", "InvoiceFooterText", "OnlinePaymentsEnabled", "DefaultPaymentConnectionId", "AllowedPaymentMethodsCsv",
				"PayLinkExpiryDays", "ShowPayOnlineOnDocuments", "UpdatedOn", "UpdatedByUserId"
			};
			var setList = string.Join(", ", columns.Select(c => $"{Col(c)} = {P}{c}"));

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("DepartmentBillingIdentities")} SET {setList} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				identity, cancellationToken);

			if (updated == 0)
			{
				var all = new[] { "DepartmentId" }.Concat(columns).ToArray();
				try
				{
					await ExecuteAsync(
						$"INSERT INTO {Tbl("DepartmentBillingIdentities")} ({string.Join(", ", all.Select(Col))}) VALUES ({string.Join(", ", all.Select(c => P + c))})",
						identity, cancellationToken);
				}
				catch (Exception ex) when (IsUniqueViolation(ex))
				{
					// Two first saves for the department raced past the update; the loser applies its values over the winner's row.
					await ExecuteAsync(
						$"UPDATE {Tbl("DepartmentBillingIdentities")} SET {setList} WHERE {Col("DepartmentId")} = {P}DepartmentId",
						identity, cancellationToken);
				}
			}

			return await GetByDepartmentIdAsync(identity.DepartmentId);
		}
	}
}
