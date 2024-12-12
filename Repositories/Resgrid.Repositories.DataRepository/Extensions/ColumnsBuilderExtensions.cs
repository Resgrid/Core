// From https://github.com/grandchamp/Identity.Dapper

using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Configs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resgrid.Repositories.DataRepository.Extensions
{
	public static class ColumnsBuilderExtensions
	{
		public static string GetCommaSeparatedColumns(this IEnumerable<string> properties)
		{
			var columnsBuilder = new StringBuilder();

			var valuesArray = new List<string>(properties.Count());

			columnsBuilder.Append(string.Join(", ", properties));

			return columnsBuilder.ToString();
		}

		public static IEnumerable<string> GetColumns<TEntity>(this TEntity entity, SqlConfiguration sqlConfiguration, bool ignoreIdProperty = false, IEnumerable<string> ignoreProperties = null, bool forInsert = true)
		{
			ignoreProperties = ignoreProperties ?? Enumerable.Empty<string>();

			var roleProperties = Enumerable.Empty<string>();
			//var idProperty = entity.GetType().GetProperty("Id");
			var idPropertyName = ((IEntity)entity).IdName;
			var idProperty = entity.GetType().GetProperty(idPropertyName);

			ignoreProperties = ignoreProperties.Append("IdValue");
			ignoreProperties = ignoreProperties.Append("TableName");
			ignoreProperties = ignoreProperties.Append("IdName");

			if (idProperty != null && !ignoreIdProperty)
			{
				var defaultIdTypeValue = idProperty.PropertyType == typeof(string) ? string.Empty : Activator.CreateInstance(idProperty.PropertyType);
				var idPropertyValue = idProperty.GetValue(entity, null);

				roleProperties = !idPropertyValue.Equals(defaultIdTypeValue)
									? entity.GetType()
											.GetPublicPropertiesNames(x => !ignoreProperties.Any(y => x.Name == y))
									: forInsert
										? entity.GetType()
												.GetPublicPropertiesNames(y => !y.Name.Equals(idPropertyName)
																			   && !ignoreProperties.Any(x => x == y.Name))
										: entity.GetType()
												.GetPublicPropertiesNames(x => !ignoreProperties.Any(y => x.Name == y));
			}
			else
			{
				roleProperties = entity.GetType()
									   .GetPublicPropertiesNames(y => !y.Name.Equals(idPropertyName)
																	  && !ignoreProperties.Any(x => x == y.Name));
			}

			roleProperties = roleProperties.Select(y => string.Concat(sqlConfiguration.TableColumnStartNotation, y, sqlConfiguration.TableColumnEndNotation));

			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				roleProperties = roleProperties.Select(x => x.ToLower());
			}

			return roleProperties;
		}
	}
}
