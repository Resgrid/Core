using Dapper;
using Resgrid.Config;
using Resgrid.Repositories.DataRepository.Extensions;
using System;
using System.Data;

namespace Resgrid.Repositories.DataRepository
{
	internal class DynamicParametersExtension: DynamicParameters
	{
		public DynamicParametersExtension()
		{
		}

		public DynamicParametersExtension(object template)
		{
			AddDynamicParams(template);
		}

		public void Add(string name, object? value = null, DbType? dbType = null, ParameterDirection? direction = null, int? size = null, byte? precision = null, byte? scale = null)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				name = name.ToLower();

				if (value is String newValue && dbType == null)
					base.Add(name, new CitextParameter(newValue), dbType, direction, size);
				else
					base.Add(name, value, dbType, direction, size);
			}
			else
			{
				base.Add(name, value, dbType, direction, size);
			}
		}
	}
}
