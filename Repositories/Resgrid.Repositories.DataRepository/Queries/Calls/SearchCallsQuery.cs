using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public static class SearchCallsQuery
	{
		public static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");

		public static string Build(SqlConfiguration config, CallSearchQuery query)
		{
			var postgres = config is PostgreSqlConfiguration;
			string Col(string name) => postgres ? name.ToLowerInvariant() : "[" + name + "]";
			var table = config.SchemaName + "." + (postgres ? "calls" : "[Calls]");
			var columns = $"{Col("CallId")}, {Col("DepartmentId")}, {Col("Number")}, {Col("LoggedOn")}, {Col("State")}";
			if (query.IncludeText) columns += $", {Col("Name")}, {Col("Address")}";
			var sql = $"SELECT {columns} FROM {table} WHERE {Col("DepartmentId")} = @DepartmentId AND {Col("IsDeleted")} = {(postgres ? "false" : "0")}";
			if (query.Closed.HasValue) sql += $" AND {Col("State")} {(query.Closed.Value ? "> 0" : "= 0")}";
			if (query.FromUtc.HasValue) sql += $" AND {Col("LoggedOn")} >= @FromUtc";
			if (query.UntilUtc.HasValue) sql += $" AND {Col("LoggedOn")} < @UntilUtc";
			if (!string.IsNullOrWhiteSpace(query.Term))
			{
				string Matches(string column) => $"LOWER({Col(column)}) LIKE LOWER(@Term) ESCAPE '!'";
				sql += " AND (" + Matches("Number");
				if (query.IncludeText) sql += " OR " + Matches("Name") + " OR " + Matches("Address");
				sql += ")";
			}
			sql += $" ORDER BY {Col("LoggedOn")} DESC, {Col("CallId")} DESC";
			return sql + (postgres ? " LIMIT @Take OFFSET @Offset" : " OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY");
		}
	}
}
