using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Common
{
	public class InsertQuery : IInsertQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public InsertQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery<TEntity>(TEntity entity)
		{
			List<string> ignoredProperties = new List<string>(((IEntity)entity).IgnoredProperties);
			string returnId = "";

			if (((IEntity)entity).IdType == 0)
			{
				ignoredProperties.Add(((IEntity)entity).IdName);

				if (Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres)
				{
					returnId = $" RETURNING {((IEntity)entity).IdName.ToLower()}";
				}
				else
					returnId = _sqlConfiguration.InsertGetReturnIdCommand;
			}

			var columns = entity.GetColumns(_sqlConfiguration, ignoreProperties: ignoredProperties);

			var valuesArray = new List<string>(columns.Count());
			valuesArray = valuesArray.InsertQueryValuesFragment(_sqlConfiguration.ParameterNotation, columns);


			var query = _sqlConfiguration.InsertQuery
										 .ReplaceInsertQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
											((IEntity)entity).TableName,
											returnId,
											columns.GetCommaSeparatedColumns(),
											string.Join(", ", valuesArray));

			return query;
		}
	}
}
