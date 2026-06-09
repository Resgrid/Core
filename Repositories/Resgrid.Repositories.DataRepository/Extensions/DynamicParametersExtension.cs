using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Repositories.DataRepository.Extensions;
using System;
using System.Data;
using System.Text;

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
			// Guard the write path: scrub any string value so it is safe for a PostgreSQL UTF-8
			// column (strips NUL, repairs Windows-1252 mojibake, replaces unpaired surrogates).
			// This is the central manual-parameter chokepoint used across all repositories.
			if (value is string stringValue && SystemBehaviorConfig.SanitizeTextForUtf8)
				value = Utf8Sanitizer.Clean(stringValue,
					SystemBehaviorConfig.Utf8RepairDoubleEncoding,
					SystemBehaviorConfig.Utf8NormalizeToNfc ? NormalizationForm.FormC : (NormalizationForm?)null);

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
