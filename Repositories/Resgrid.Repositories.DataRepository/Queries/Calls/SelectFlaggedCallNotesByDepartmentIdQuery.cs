using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectFlaggedCallNotesByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectFlaggedCallNotesByDepartmentIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectFlaggedCallNotesByDepartmentIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%" },
					new string[] { "DepartmentId" },
					new string[] { "%CALLNOTESTABLE%", "%CALLSTABLE%" },
					new string[]
					{
						_sqlConfiguration.CallNotesTable,
						_sqlConfiguration.CallsTable
					});

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}

