using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Dedicated department numbers (DepartmentPhoneNumbers, M0323). PhoneNumber is citext on PostgreSQL.</summary>
	public class DepartmentPhoneNumbersRepository : RmsRepositoryBase<DepartmentPhoneNumber>, IDepartmentPhoneNumbersRepository
	{
		public DepartmentPhoneNumbersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<DepartmentPhoneNumber>> GetByNumberAsync(string phoneNumber, IReadOnlyCollection<int> statuses, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(phoneNumber) || statuses == null || statuses.Count == 0)
				return new List<DepartmentPhoneNumber>();

			var sql = $"SELECT * FROM {Tbl("DepartmentPhoneNumbers")} WHERE {Col("PhoneNumber")} = {CaseInsensitive("PhoneNumber")} " +
				$"AND {InList("Status", "Statuses")} ORDER BY {Col("CreatedOn")} DESC";

			return (await QueryAsync<DepartmentPhoneNumber>(sql, new { PhoneNumber = phoneNumber, Statuses = InListValue(statuses) }, cancellationToken)).ToList();
		}

		public async Task<List<DepartmentPhoneNumber>> GetByStatusesAsync(IReadOnlyCollection<int> statuses, CancellationToken cancellationToken = default)
		{
			if (statuses == null || statuses.Count == 0)
				return new List<DepartmentPhoneNumber>();

			var sql = $"SELECT * FROM {Tbl("DepartmentPhoneNumbers")} WHERE {InList("Status", "Statuses")} " +
				$"ORDER BY {Col("DepartmentId")}, {Col("CreatedOn")}";

			return (await QueryAsync<DepartmentPhoneNumber>(sql, new { Statuses = InListValue(statuses) }, cancellationToken)).ToList();
		}

		public async Task<bool> TryBeginReleaseAsync(string departmentPhoneNumberId, int expectedStatus, DateTime startedOn, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(departmentPhoneNumberId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("DepartmentPhoneNumbers")} SET {Col("ReleasedOn")} = {P}StartedOn, {Col("UpdatedOn")} = {P}StartedOn " +
				$"WHERE {Col("DepartmentPhoneNumberId")} = {P}Id AND {Col("Status")} = {P}Status AND {Col("ReleasedOn")} IS NULL",
				new { Id = departmentPhoneNumberId, Status = expectedStatus, StartedOn = DatabaseTimestamp(startedOn) }, cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryReactivateAsync(string departmentPhoneNumberId, DateTime updatedOn, string lastError, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(departmentPhoneNumberId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("DepartmentPhoneNumbers")} SET {Col("Status")} = {P}Active, {Col("AddonInactiveSince")} = NULL, {Col("ReleaseAfter")} = NULL, " +
				$"{Col("ReleaseWarningsSent")} = 0, {Col("LastReleaseWarningOn")} = NULL, {Col("LastError")} = {P}LastError, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {Col("DepartmentPhoneNumberId")} = {P}Id AND {Col("Status")} = {P}Lapsed AND {Col("ReleasedOn")} IS NULL",
				new
				{
					Id = departmentPhoneNumberId,
					Active = (int)DepartmentPhoneNumberStatuses.Active,
					Lapsed = (int)DepartmentPhoneNumberStatuses.Lapsed,
					LastError = lastError,
					UpdatedOn = DatabaseTimestamp(updatedOn)
				}, cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TrySetRoutingStateAsync(string departmentPhoneNumberId, string lastError, DateTime updatedOn, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(departmentPhoneNumberId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("DepartmentPhoneNumbers")} SET {Col("LastError")} = {P}LastError, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {Col("DepartmentPhoneNumberId")} = {P}Id AND {Col("Status")} = {P}Active AND {Col("ReleasedOn")} IS NULL",
				new
				{
					Id = departmentPhoneNumberId,
					Active = (int)DepartmentPhoneNumberStatuses.Active,
					LastError = lastError,
					UpdatedOn = DatabaseTimestamp(updatedOn)
				}, cancellationToken);

			return updated == 1;
		}
	}
}
