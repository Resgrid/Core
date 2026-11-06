using Dapper;
using Npgsql;
using Resgrid.Model.Repositories.Connection;
using System;
using System.Data;
using System.Data.Common;

namespace Resgrid.Repositories.DataRepository.Servers.PostgreSql
{
	public class PostgreSqlConnectionProvider : IConnectionProvider
	{
		static PostgreSqlConnectionProvider()
		{
			SqlMapper.AddTypeHandler(new PostgreSqlGuidTypeHandler());
			SqlMapper.RemoveTypeMap(typeof(Guid));
			SqlMapper.RemoveTypeMap(typeof(Guid?));
		}

		public DbConnection Create()
		{
			return new NpgsqlConnection(Config.DataConfig.CoreConnectionString); //+ ";Client Encoding=windows-1252;Encoding=windows-1252");
		}
	}

	// Legacy PostgreSQL identifiers are citext while newer models expose Guid.
	// Accept either database representation; let PostgreSQL infer UUID or legacy citext parameters.
	internal sealed class PostgreSqlGuidTypeHandler : SqlMapper.TypeHandler<Guid>
	{
		public override Guid Parse(object value)
		{
			if (value is Guid guid)
				return guid;
			if (value is string text && Guid.TryParse(text, out var parsed))
				return parsed;
			throw new DataException("The PostgreSQL identifier is not a valid GUID.");
		}

		public override void SetValue(IDbDataParameter parameter, Guid value)
		{
			if (parameter is NpgsqlParameter postgresParameter)
			{
				// A UUID-typed parameter cannot compare with a legacy citext ID.
				// Unknown sends a bound GUID string so the column supplies its type.
				postgresParameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Unknown;
				postgresParameter.Value = value.ToString("D");
				return;
			}
			parameter.DbType = DbType.Guid;
			parameter.Value = value;
		}
	}
}
