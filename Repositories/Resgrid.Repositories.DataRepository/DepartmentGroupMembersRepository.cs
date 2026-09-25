using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.DepartmentGroups;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentGroupMembersRepository : AuditedConfigurationRepository<DepartmentGroupMember>, IDepartmentGroupMembersRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentGroupMembersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<DepartmentGroupMember>> GetAllGroupMembersByGroupIdAsync(int groupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroupMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("GroupId", groupId);

					var query = _queryFactory.GetQuery<SelectGroupMembersByGroupIdQuery>();

					return await x.QueryAsync<DepartmentGroupMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

				throw;
			}
		}

		public async Task<IEnumerable<DepartmentGroupMember>> GetAllGroupMembersByUserAndDepartmentAsync(string userId, int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroupMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectGroupMembersByUserDidQuery>();

					return await x.QueryAsync<DepartmentGroupMember, DepartmentGroup, DepartmentGroupMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (dgm, dg) => { dgm.DepartmentGroup = dg; return dgm; },
						splitOn: "DepartmentGroupId");
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

				throw;
			}
		}

		public async Task<bool> DeleteGroupMembersByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!HasConfigurationJournal) return await DeleteGroupMembersCoreAsync(groupId, departmentId, cancellationToken);
			var owner = await ScalarAsync<int>($"SELECT {Col("DepartmentId")} FROM {Tbl("DepartmentGroups")} WHERE {Col("DepartmentGroupId")}={P}Id", new { Id = groupId }, cancellationToken);
			if (owner == 0) return false;
			if (owner != departmentId) throw new UnauthorizedAccessException();
			async Task<Resgrid.Model.AdminAssist.ConfigurationChangeStamp> Read()
			{
				var count = await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("DepartmentGroupMembers")} WHERE {Col("DepartmentGroupId")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId", new { Id = groupId, DepartmentId = departmentId }, cancellationToken);
				var value = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
				return new(value, "{\"members\":" + value + "}");
			}
			return await ExecuteConfigurationMutationAsync(departmentId, "DepartmentGroupMembers.BulkDelete", Read,
				() => DeleteGroupMembersCoreAsync(groupId, departmentId, cancellationToken), cancellationToken);
		}

		private async Task<bool> DeleteGroupMembersCoreAsync(int groupId, int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var dynamicParameters = new DynamicParametersExtension();
						dynamicParameters.Add("GroupId", groupId);
						dynamicParameters.Add("DepartmentId", departmentId);

						var query = _queryFactory.GetDeleteQuery<DeleteGroupMembersByGroupIdDidQuery>();

						var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await removeFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await removeFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}


		public async Task<IEnumerable<DepartmentGroupMember>> GetAllGroupAdminsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroupMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectGroupAdminsByDidQuery>();

					return await x.QueryAsync<DepartmentGroupMember, DepartmentGroup, DepartmentGroupMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (dgm, dg) => { dgm.DepartmentGroup = dg; return dgm; },
						splitOn: "DepartmentGroupId");
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

				throw;
			}
		}
	}
}
