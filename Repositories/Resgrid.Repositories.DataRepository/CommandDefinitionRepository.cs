using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using CommandDefinition = Resgrid.Model.CommandDefinition;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Commands;

namespace Resgrid.Repositories.DataRepository
{
	public class CommandDefinitionRepository : RepositoryBase<CommandDefinition>, ICommandDefinitionRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CommandDefinitionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}
	}

	public class CommandDefinitionRoleRepository : RepositoryBase<CommandDefinitionRole>, ICommandDefinitionRoleRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CommandDefinitionRoleRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CommandDefinitionRole>> GetRolesByCommandDefinitionIdAsync(int commandDefinitionId)
		{
			return await SelectAsync<SelectCommandDefinitionRolesByCommandIdQuery, CommandDefinitionRole>(
				_connectionProvider, _unitOfWork, _queryFactory, "CommandDefinitionId", commandDefinitionId);
		}

		internal static async Task<IEnumerable<T>> SelectAsync<TQuery, T>(IConnectionProvider connectionProvider,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory, string parameterName, int parameterValue)
			where TQuery : Resgrid.Model.Repositories.Queries.Contracts.ISelectQuery
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<T>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add(parameterName, parameterValue);

					var query = queryFactory.GetQuery<TQuery>();

					return await x.QueryAsync<T>(sql: query,
						param: dynamicParameters,
						transaction: unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (unitOfWork?.Connection == null)
				{
					using (conn = connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = unitOfWork.CreateOrGetConnection();

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

	public class CommandDefinitionRoleUnitTypeRepository : RepositoryBase<CommandDefinitionRoleUnitType>, ICommandDefinitionRoleUnitTypeRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CommandDefinitionRoleUnitTypeRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CommandDefinitionRoleUnitType>> GetUnitTypesByCommandDefinitionIdAsync(int commandDefinitionId)
		{
			return await CommandDefinitionRoleRepository.SelectAsync<SelectCmdRoleUnitTypesByCommandIdQuery, CommandDefinitionRoleUnitType>(
				_connectionProvider, _unitOfWork, _queryFactory, "CommandDefinitionId", commandDefinitionId);
		}

		public async Task<IEnumerable<CommandDefinitionRoleUnitType>> GetUnitTypesByRoleIdAsync(int commandDefinitionRoleId)
		{
			return await CommandDefinitionRoleRepository.SelectAsync<SelectCmdRoleUnitTypesByRoleIdQuery, CommandDefinitionRoleUnitType>(
				_connectionProvider, _unitOfWork, _queryFactory, "CommandDefinitionRoleId", commandDefinitionRoleId);
		}
	}

	public class CommandDefinitionRolePersonnelRoleRepository : RepositoryBase<CommandDefinitionRolePersonnelRole>, ICommandDefinitionRolePersonnelRoleRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CommandDefinitionRolePersonnelRoleRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CommandDefinitionRolePersonnelRole>> GetPersonnelRolesByCommandDefinitionIdAsync(int commandDefinitionId)
		{
			return await CommandDefinitionRoleRepository.SelectAsync<SelectCmdRolePersonnelRolesByCommandIdQuery, CommandDefinitionRolePersonnelRole>(
				_connectionProvider, _unitOfWork, _queryFactory, "CommandDefinitionId", commandDefinitionId);
		}

		public async Task<IEnumerable<CommandDefinitionRolePersonnelRole>> GetPersonnelRolesByRoleIdAsync(int commandDefinitionRoleId)
		{
			return await CommandDefinitionRoleRepository.SelectAsync<SelectCmdRolePersonnelRolesByRoleIdQuery, CommandDefinitionRolePersonnelRole>(
				_connectionProvider, _unitOfWork, _queryFactory, "CommandDefinitionRoleId", commandDefinitionRoleId);
		}
	}
}
