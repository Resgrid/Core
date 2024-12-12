using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Workshifts
{
	public class SelectAllWorkshiftsAndDaysByDidQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectAllWorkshiftsAndDaysByDidQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectAllWorkshiftsAndDaysByDidQuery
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
																				"%WORKSHIFTSTABLE%",
																				"%WORKSHIFTDAYSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.WorkshiftsTable,
																				_sqlConfiguration.WorkshiftDaysTable
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
