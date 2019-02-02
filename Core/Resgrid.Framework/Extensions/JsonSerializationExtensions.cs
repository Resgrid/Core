using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;

namespace Resgrid
{
	public static class JsonSerializationExtensions
	{
		static IList<JsonConverter> _converters;
		static JsonSerializer _serializer;
		static JsonSerializerSettings _settings;

		static JsonSerializationExtensions()
		{
			_converters = new List<JsonConverter>
			{
				new IsoDateTimeConverter(),
				new StringEnumConverter()
			};

			_settings = new JsonSerializerSettings()
			{
				NullValueHandling = NullValueHandling.Include,
				Converters = _converters
			};

			_serializer = new JsonSerializer();
			foreach (var converter in _converters)
			{
				_serializer.Converters.Add(converter);
			}
		}

		public static JsonSerializerSettings JsonSerializerSettings
		{
			get { return _settings; }
		}

		public static object DeserializeStream(this Stream readStream, Type type)
		{
			var sr = new StreamReader(readStream);
			var jreader = new JsonTextReader(sr);

			object val = _serializer.Deserialize(jreader, type);
			return val;
		}

		public static string SerializeJson(this object value, JsonSerializerSettings settingsOverride = null)
		{
			return JsonConvert.SerializeObject(value, settingsOverride ?? _settings);
		}
	}
}
