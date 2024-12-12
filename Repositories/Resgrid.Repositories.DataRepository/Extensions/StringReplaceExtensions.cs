// From https://github.com/grandchamp/Identity.Dapper

using Resgrid.Config;
using Resgrid.Repositories.DataRepository.Configs;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Repositories.DataRepository.Extensions
{
	public static class StringReplaceExtensions
	{
		public static string ReplaceInsertQueryParameters(this string query, SqlConfiguration sqlConfiguration, string schemaName, string tableName, string returnIdCmd, string columns, string values)
		{
			query = $"{sqlConfiguration.QueryPrefix} {query}";

			return query.Replace("%SCHEMA%", schemaName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%TABLENAME%", tableName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%RETURNID%", returnIdCmd, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%COLUMNS%", $"({columns})", System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%VALUES%", values, System.StringComparison.InvariantCultureIgnoreCase);
		}

		public static string ReplaceDeleteQueryParameters(this string query, SqlConfiguration sqlConfiguration, string schemaName, string tableName, string idColumnName, string idParameter)
		{
			query = $"{sqlConfiguration.QueryPrefix} {query}";

			return query.Replace("%SCHEMA%", schemaName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%TABLENAME%", tableName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%IDCOLUMN%", idColumnName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%ID%", idParameter, System.StringComparison.InvariantCultureIgnoreCase);
		}

		public static string ReplaceUpdateQueryParameters(this string query, SqlConfiguration sqlConfiguration, string schemaName, string tableName, string setValues, string idColumnName, string idParameter)
		{
			query = $"{sqlConfiguration.QueryPrefix} {query}";

			return query.Replace("%SCHEMA%", schemaName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%TABLENAME%", tableName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%IDCOLUMN%", idColumnName, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%SETVALUES%", setValues, System.StringComparison.InvariantCultureIgnoreCase)
						.Replace("%ID%", idParameter, System.StringComparison.InvariantCultureIgnoreCase);
		}

		public static string ReplaceQueryParameters(this string query, SqlConfiguration sqlConfiguration, string schemaName, string tableName, string parameterNotation, string[] parameterPlaceholders, string[] sqlParameterValues)
		{
			query = $"{sqlConfiguration.QueryPrefix} {query}";

			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				query = query.ToLower();

			var queryBuilder = new StringBuilder(query);
			for (int i = 0; i < parameterPlaceholders.Length; i++)
			{
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
					queryBuilder.Replace($"{parameterPlaceholders[i].ToLower()}", $"{parameterNotation}{sqlParameterValues[i].ToLower()}");
				else
					queryBuilder.Replace($"{parameterPlaceholders[i]}", $"{parameterNotation}{sqlParameterValues[i]}");
			}

			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				queryBuilder.Replace("%schema%", schemaName)
				.Replace("%tablename%", tableName);
			else
				queryBuilder.Replace("%SCHEMA%", schemaName)
				.Replace("%TABLENAME%", tableName);
			//.Replace("%TABLENAME%", $"{sqlConfiguration.TableColumnStartNotation}{tableName}{sqlConfiguration.TableColumnEndNotation}");

			return queryBuilder.ToString();
		}

		public static string ReplaceQueryParameters(this string query, SqlConfiguration sqlConfiguration, string schemaName, string tableName, string parameterNotation, string[] parameterPlaceholders, string[] sqlParameterValues, string[] othersPlaceholders, string[] othersPlaceholdersValues)
		{
			query = $"{sqlConfiguration.QueryPrefix} {query}";

			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				query = query.ToLower();

			var queryBuilder = new StringBuilder(query.ReplaceQueryParameters(sqlConfiguration, schemaName, tableName, parameterNotation, parameterPlaceholders, sqlParameterValues));
			for (int i = 0; i < othersPlaceholders.Length; i++)
			{
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
					queryBuilder.Replace(othersPlaceholders[i].ToLower(), othersPlaceholdersValues[i].ToLower());
				else
					queryBuilder.Replace(othersPlaceholders[i], othersPlaceholdersValues[i]);
			}
			//queryBuilder.Replace(othersPlaceholders[i], $"{sqlConfiguration.TableColumnStartNotation}{othersPlaceholdersValues[i]}{sqlConfiguration.TableColumnEndNotation}" );

			return queryBuilder.ToString();
		}

		public static string RemoveSpecialCharacters(this string value) => Regex.Replace(value, @"[^\w\d]", "");
	}
}
