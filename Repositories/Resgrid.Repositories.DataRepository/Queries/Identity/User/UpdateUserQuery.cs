using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
	public class UpdateUserQuery : IUpdateQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public UpdateUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery<TEntity>(TEntity entity)
		{
			var roleProperties = entity.GetColumns(_sqlConfiguration, ignoreIdProperty: true, ignoreProperties: new string[] { "ConcurrencyStamp", "SecurityQuestion", "SecurityAnswer", "SecurityAnswerSalt", "CreateDate", "UserId" });

			var setFragment = roleProperties.UpdateQuerySetFragment(_sqlConfiguration.ParameterNotation);

			var query = _sqlConfiguration.UpdateUserQuery
										 .ReplaceUpdateQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																	   _sqlConfiguration.UserTable,
																	   setFragment,
																	   ((IEntity)entity).IdName,
																	   $"{_sqlConfiguration.ParameterNotation}Id");

			return query;
		}
	}
}
