using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Resgrid.Config
{
	public static class ConfigProcessor
	{
		public static bool LoadAndProcessConfig(string path = null)
		{
			try
			{
				if (String.IsNullOrWhiteSpace(path))
				{
					path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase).Replace("file:\\", "");
					path = $"{path}\\ResgridConfig.json";
				}

				if (File.Exists(path))
				{
					var configFile = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));

					foreach (var configValue in configFile)
					{
						if (!String.IsNullOrWhiteSpace(configValue.Value))
						{
							var parts = configValue.Key.Split(char.Parse("."));

							if (parts.Length == 2)
							{
								Type configObj = Type.GetType($"Resgrid.Config.{parts[0]}");

								if (configObj != null)
								{
									FieldInfo prop = configObj.GetField(parts[1]);
									if (null != prop)
									{
										Console.WriteLine($"Resgrid.Config: Setting Value for {prop.ToString()}");

										if (prop.FieldType.BaseType == typeof(Enum))
										{
											Type t = Nullable.GetUnderlyingType(prop.FieldType) ?? prop.FieldType;

											String name = Enum.GetName(t, int.Parse(configValue.Value));
											Object enumValue = Enum.Parse(t, name, false);

											object safeValue = Convert.ChangeType(enumValue, t);
											prop.SetValue(configObj, safeValue);
										}
										else
										{
											Type t = Nullable.GetUnderlyingType(prop.FieldType) ?? prop.FieldType;
											object safeValue = Convert.ChangeType(configValue.Value, t);
											prop.SetValue(configObj, safeValue);
										}
									}
								}
							}
						}
					}

					ApplyConnectionStringFallback();
					return true;
				}
			}
			catch (Exception ex)
			{
				return false;
			}

			return false;
		}

		public static bool LoadAndProcessEnvVariables(IEnumerable<KeyValuePair<string, string>> values)
		{
			Console.WriteLine($"Resgrid.Config: Processing Environment Variables");

			bool hasSetAtLeastOneVariable = false;

			foreach (var configValue in values)
			{
				try
				{
					if (!String.IsNullOrWhiteSpace(configValue.Value) && configValue.Key.StartsWith("RESGRID"))
					{
						var parts = configValue.Key.Split(char.Parse(":"));

						if (parts.Length == 3)
						{
							Type configObj = Type.GetType($"Resgrid.Config.{parts[1]}");

							if (configObj != null)
							{
								hasSetAtLeastOneVariable = true;

								FieldInfo prop = configObj.GetField(parts[2]);
								if (null != prop)
								{
									Console.WriteLine($"Resgrid.Config: Setting Value for {prop.ToString()}");

									if (prop.FieldType.BaseType == typeof(Enum))
									{
										Type t = Nullable.GetUnderlyingType(prop.FieldType) ?? prop.FieldType;

										String name = Enum.GetName(t, int.Parse(configValue.Value));
										Object enumValue = Enum.Parse(t, name, false);

										object safeValue = Convert.ChangeType(enumValue, t);
										prop.SetValue(configObj, safeValue);
									}
									else
									{
										Type t = Nullable.GetUnderlyingType(prop.FieldType) ?? prop.FieldType;
										object safeValue = Convert.ChangeType(configValue.Value, t);
										prop.SetValue(configObj, safeValue);
									}
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Resgrid.Config: Error processing Environment Variable: {configValue.Key}");
					Console.WriteLine(ex.ToString());
				}
			}

			ApplyConnectionStringFallback();
			return hasSetAtLeastOneVariable;
		}

		// The CoreConnectionString value this processor filled in itself (null when it was configured explicitly).
		private static string _derivedCoreConnectionString;

		/// <summary>
		/// SQL Server installs have long configured only DataConfig.ConnectionString, but the database upgrade
		/// (FluentMigrator), identity and claims read DataConfig.CoreConnectionString. Left empty, FluentMigrator
		/// silently falls back to its connectionless preview processor: no DDL is applied and the first
		/// Exists() check throws NotImplementedException (GitHub #536). On SQL Server, an unset core connection
		/// string therefore follows ConnectionString. An explicitly configured value always wins, and because the
		/// config file and the environment variables are applied in turn, this re-runs after each and only ever
		/// replaces a value it derived itself.
		/// </summary>
		private static void ApplyConnectionStringFallback()
		{
			var coreIsUnset = String.IsNullOrWhiteSpace(DataConfig.CoreConnectionString) ||
				(_derivedCoreConnectionString != null && DataConfig.CoreConnectionString == _derivedCoreConnectionString);

			if (!coreIsUnset)
			{
				_derivedCoreConnectionString = null;
				return;
			}

			if (DataConfig.DatabaseType == DatabaseTypes.SqlServer && !String.IsNullOrWhiteSpace(DataConfig.ConnectionString))
			{
				DataConfig.CoreConnectionString = DataConfig.ConnectionString;
				_derivedCoreConnectionString = DataConfig.ConnectionString;
			}
			else if (_derivedCoreConnectionString != null)
			{
				// No longer SQL Server (e.g. DatabaseType switched to Postgres later in loading): PostgreSQL needs an
				// explicit core connection string, so don't leave the SQL Server one behind.
				DataConfig.CoreConnectionString = "";
				_derivedCoreConnectionString = null;
			}
		}
	}
}
