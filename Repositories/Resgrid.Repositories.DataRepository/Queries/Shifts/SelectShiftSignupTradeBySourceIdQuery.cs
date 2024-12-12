using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Shifts
{
	public class SelectShiftSignupTradeBySourceIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectShiftSignupTradeBySourceIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectShiftSignupTradeBySourceIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%SHIFTSIGNUPID%"
																},
																 new string[] {
																				"ShiftSignupId"
																 },
																 new string[] {
																				"%SHIFTSIGNUPTRADESTABLE%",
																				"%SHIFTSIGNUPTRADEUSERSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.ShiftSignupTradesTable,
																				_sqlConfiguration.ShiftSignupTradeUsersTable
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
