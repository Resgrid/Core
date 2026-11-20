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
	}
}
