using System;
using System.Collections.Generic;
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
	/// <summary>Department payment-provider connections (plan B2.2, registry M0212).</summary>
	public class DepartmentPaymentConnectionRepository : RmsRepositoryBase<DepartmentPaymentConnection>, IDepartmentPaymentConnectionRepository
	{
		public DepartmentPaymentConnectionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DepartmentPaymentConnection> GetByIdForDepartmentAsync(string departmentPaymentConnectionId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<DepartmentPaymentConnection>(
				$"SELECT * FROM {Tbl("DepartmentPaymentConnections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DepartmentPaymentConnectionId")} = {P}Id AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Id = departmentPaymentConnectionId });
		}

		public Task<IEnumerable<DepartmentPaymentConnection>> GetForDepartmentAsync(int departmentId)
		{
			return QueryAsync<DepartmentPaymentConnection>(
				$"SELECT * FROM {Tbl("DepartmentPaymentConnections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("IsDefault")} DESC, {Col("ConnectedOn")} DESC",
				new { DepartmentId = departmentId });
		}

		public Task<DepartmentPaymentConnection> GetDefaultForDepartmentAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<DepartmentPaymentConnection>(
				$"SELECT * FROM {Tbl("DepartmentPaymentConnections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("Status")} = {P}Connected ORDER BY {Col("IsDefault")} DESC, {Col("ConnectedOn")} DESC",
				new { DepartmentId = departmentId, Connected = (int)PaymentConnectionStatuses.Connected });
		}

		public Task<DepartmentPaymentConnection> GetByExternalAccountIdAsync(int provider, string externalAccountId)
		{
			return QueryFirstOrDefaultAsync<DepartmentPaymentConnection>(
				$"SELECT * FROM {Tbl("DepartmentPaymentConnections")} WHERE {Col("Provider")} = {P}Provider AND {Col("ExternalAccountId")} = {P}Account AND {Col("IsDeleted")} = {False} ORDER BY {Col("ConnectedOn")} DESC",
				new { Provider = provider, Account = externalAccountId });
		}

		public Task<int> ClearDefaultAsync(int departmentId, string exceptConnectionId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("DepartmentPaymentConnections")} SET {Col("IsDefault")} = {False} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DepartmentPaymentConnectionId")} <> {P}Except AND {Col("IsDefault")} = {True}",
				new { DepartmentId = departmentId, Except = exceptConnectionId ?? string.Empty }, cancellationToken);
		}

		public Task<IEnumerable<DepartmentPaymentConnection>> GetStaleVerifiedAsync(DateTime notVerifiedSinceUtc, int take)
		{
			return QueryAsync<DepartmentPaymentConnection>(
				$"SELECT * FROM {Tbl("DepartmentPaymentConnections")} WHERE {Col("IsDeleted")} = {False} AND {Col("Status")} = {P}Connected AND ({Col("LastVerifiedOn")} IS NULL OR {Col("LastVerifiedOn")} < {P}Since) ORDER BY {Col("LastVerifiedOn")} {Paging()}",
				new { Connected = (int)PaymentConnectionStatuses.Connected, Since = DatabaseTimestamp(notVerifiedSinceUtc), Skip = 0, Take = take });
		}

		private static string False => IsPostgres ? "FALSE" : "0";
		private static string True => IsPostgres ? "TRUE" : "1";
	}

	/// <summary>Hosted payment requests (plan B2.2).</summary>
	public class InvoicePaymentRequestRepository : RmsRepositoryBase<InvoicePaymentRequest>, IInvoicePaymentRequestRepository
	{
		private const string OpenStatuses = "(0, 1, 2)";

		public InvoicePaymentRequestRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<InvoicePaymentRequest> GetByIdForDepartmentAsync(string invoicePaymentRequestId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoicePaymentRequestId")} = {P}Id",
				new { DepartmentId = departmentId, Id = invoicePaymentRequestId });
		}

		public Task<IEnumerable<InvoicePaymentRequest>> GetByInvoiceIdAsync(string invoiceId, int departmentId)
		{
			return QueryAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}InvoiceId ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, InvoiceId = invoiceId });
		}

		public Task<InvoicePaymentRequest> GetOpenByInvoiceIdAsync(string invoiceId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}InvoiceId AND {Col("Status")} IN {OpenStatuses} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, InvoiceId = invoiceId });
		}

		public Task<InvoicePaymentRequest> GetByExternalReferenceAsync(int provider, string externalReference)
		{
			return QueryFirstOrDefaultAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("Provider")} = {P}Provider AND {Col("ExternalReference")} = {P}Reference",
				new { Provider = provider, Reference = externalReference });
		}

		public Task<InvoicePaymentRequest> GetByPaymentIntentIdAsync(int provider, string paymentIntentId)
		{
			return QueryFirstOrDefaultAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("Provider")} = {P}Provider AND {Col("PaymentIntentId")} = {P}Intent",
				new { Provider = provider, Intent = paymentIntentId });
		}

		public Task<IEnumerable<InvoicePaymentRequest>> GetOpenOlderThanAsync(DateTime createdBeforeUtc, int take)
		{
			return QueryAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("Status")} IN {OpenStatuses} AND {Col("AddedOn")} < {P}Before ORDER BY {Col("AddedOn")} {Paging()}",
				new { Before = DatabaseTimestamp(createdBeforeUtc), Skip = 0, Take = take });
		}

		public Task<IEnumerable<InvoicePaymentRequest>> GetOpenExpiredAsync(DateTime asOfUtc, int take)
		{
			return QueryAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("Status")} IN {OpenStatuses} AND {Col("ExpiresOn")} < {P}AsOf ORDER BY {Col("ExpiresOn")} {Paging()}",
				new { AsOf = DatabaseTimestamp(asOfUtc), Skip = 0, Take = take });
		}

		public Task<IEnumerable<InvoicePaymentRequest>> GetOpenByConnectionAsync(string departmentPaymentConnectionId)
		{
			return QueryAsync<InvoicePaymentRequest>(
				$"SELECT * FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("DepartmentPaymentConnectionId")} = {P}Connection AND {Col("Status")} IN {OpenStatuses}",
				new { Connection = departmentPaymentConnectionId });
		}

		public async Task<int> CountOpenOlderThanAsync(DateTime createdBeforeUtc)
		{
			return await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("Status")} IN {OpenStatuses} AND {Col("AddedOn")} < {P}Before",
				new { Before = DatabaseTimestamp(createdBeforeUtc) });
		}

		public async Task<bool> HasActivitySinceAsync(DateTime sinceUtc)
		{
			var count = await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("InvoicePaymentRequests")} WHERE {Col("Status")} IN ({(int)PaymentRequestStatuses.Completed}, {(int)PaymentRequestStatuses.Processing}) AND {Col("UpdatedOn")} >= {P}Since",
				new { Since = DatabaseTimestamp(sinceUtc) });
			return count > 0;
		}
	}

	/// <summary>Received provider events (plan B2.2).</summary>
	public class PaymentProviderEventRepository : RmsRepositoryBase<PaymentConnectEvent>, IPaymentProviderEventRepository
	{
		public PaymentProviderEventRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<PaymentConnectEvent> GetByExternalEventIdAsync(int provider, string externalEventId)
		{
			return QueryFirstOrDefaultAsync<PaymentConnectEvent>(
				$"SELECT * FROM {Tbl("PaymentProviderEvents")} WHERE {Col("Provider")} = {P}Provider AND {Col("ExternalEventId")} = {P}EventId",
				new { Provider = provider, EventId = externalEventId });
		}

		public Task<DateTime?> GetNewestReceivedOnAsync()
		{
			return ScalarAsync<DateTime?>($"SELECT MAX({Col("ReceivedOn")}) FROM {Tbl("PaymentProviderEvents")}", new { });
		}

		public Task<DateTime?> GetNewestAppliedOnAsync()
		{
			return ScalarAsync<DateTime?>(
				$"SELECT MAX({Col("ProcessedOn")}) FROM {Tbl("PaymentProviderEvents")} WHERE {Col("Outcome")} = {P}Applied",
				new { Applied = (int)PaymentEventOutcomes.Applied });
		}

		public Task<int> CountByOutcomeSinceAsync(int outcome, DateTime sinceUtc)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("PaymentProviderEvents")} WHERE {Col("Outcome")} = {P}Outcome AND {Col("ReceivedOn")} >= {P}Since",
				new { Outcome = outcome, Since = DatabaseTimestamp(sinceUtc) });
		}

		public Task<int> PurgeReceivedBeforeAsync(DateTime receivedBeforeUtc, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("PaymentProviderEvents")} WHERE {Col("ReceivedOn")} < {P}Before",
				new { Before = DatabaseTimestamp(receivedBeforeUtc) }, cancellationToken);
		}
	}
}
