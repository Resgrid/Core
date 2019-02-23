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
										Type t = Nullable.GetUnderlyingType(prop.FieldType) ?? prop.FieldType;
										object safeValue = Convert.ChangeType(configValue.Value, t);
										prop.SetValue(configObj, safeValue);
									}
								}
							}
						}
					}
				}
			}
			catch
			{
				return false;
			}

			return true;
		}
	}
}
