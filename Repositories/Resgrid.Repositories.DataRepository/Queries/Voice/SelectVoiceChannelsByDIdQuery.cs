﻿using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Voice
{
	public class SelectVoiceChannelsByDIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectVoiceChannelsByDIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectVoiceChannelsByDIdQuery
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
																				"%DEPARTMENTVOICECHANNELSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.DepartmentVoiceChannelsTableName
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
