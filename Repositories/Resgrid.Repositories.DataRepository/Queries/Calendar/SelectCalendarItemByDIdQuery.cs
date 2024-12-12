using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calendar
{
	public class SelectCalendarItemByDIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectCalendarItemByDIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCalendarItemByDIdQuery
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
																				"%CALENDARITEMSTABLE%",
																				"%CALITEMATTENDEESTABLE%"
																			},
																			new string[] {
																				_sqlConfiguration.CalendarItemsTable,
																				_sqlConfiguration.CalendarItemAttendeesTable
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
