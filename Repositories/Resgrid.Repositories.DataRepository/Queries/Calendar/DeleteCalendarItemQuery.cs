using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calendar
{
	public class DeleteCalendarItemQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteCalendarItemQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteCalendarItemQuery
				.ReplaceDeleteQueryParameters(_sqlConfiguration.SchemaName,
					_sqlConfiguration.CalendarItemsTable,
					"CalendarItemId", // TODO: Fix MEEEE!
					$"{_sqlConfiguration.ParameterNotation}Id");

			return query;
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
		{
			throw new NotImplementedException();
		}
	}
}
