using Dapper;
using Npgsql;
using NpgsqlTypes;
using System.Data;

namespace Resgrid.Repositories.DataRepository.Extensions
{
	public class CitextParameter : SqlMapper.ICustomQueryParameter
	{
		readonly string _value;

		public CitextParameter(string value)
		{
			_value = value;
		}

		public void AddParameter(IDbCommand command, string name)
		{
			command.Parameters.Add(new NpgsqlParameter
			{
				ParameterName = name,
				NpgsqlDbType = NpgsqlDbType.Citext,
				Value = _value
			});
		}
	}
}
