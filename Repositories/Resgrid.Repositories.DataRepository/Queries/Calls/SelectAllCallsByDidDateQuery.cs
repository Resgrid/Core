﻿using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectAllCallsByDidDateQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectAllCallsByDidDateQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectAllCallsByDidDateQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CallsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%", "%STARTDATE%", "%ENDDATE%" },
					new string[] { "DepartmentId", "StartDate", "EndDate" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
