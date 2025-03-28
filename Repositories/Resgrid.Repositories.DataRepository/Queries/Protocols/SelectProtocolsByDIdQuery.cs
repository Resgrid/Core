﻿using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Protocols
{
	public class SelectProtocolsByDIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectProtocolsByDIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectProtocolsByDIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%DID%"
																			  },
																 new string[] {
																				"DepartmentId",
																			  },
																 new string[] {
																				"%PROTOCOLSTABLE%",
																				"%PROTOCOLTRIGGERSSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.DispatchProtocolsTable,
																				_sqlConfiguration.DispatchProtocolTriggersTable
																 }
																 );

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
