using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Shifts
{
	public class SelectShiftSignupTradeUsersByTradeIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectShiftSignupTradeUsersByTradeIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectShiftSignupTradeUsersByTradeIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%SHIFTSIGNUPTRADEID%"
																},
																 new string[] {
																				"ShiftTradeId"
																 },
																 new string[] {
																				"%SHIFTSIGNUPTRADEUSERSTABLE%",
																				"%SHIFTSIGNUPTRADEUSERSHIFTSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.ShiftSignupTradeUsersTable,
																				_sqlConfiguration.ShiftSignupTradeUserShiftsTable
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
