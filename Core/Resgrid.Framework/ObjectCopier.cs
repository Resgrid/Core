using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json.Serialization;
using System.Text.Json;
using Newtonsoft.Json;
using System.Collections;

namespace Resgrid.Framework
{
	/// <summary>
	/// Reference Article http://www.codeproject.com/KB/tips/SerializedObjectCloner.aspx
	/// Provides a method for performing a deep copy of an object.
	/// Binary Serialization is used to perform the copy.
	/// </summary>
	public static class ObjectCopier
	{
		/// <summary>
		/// Perform a deep Copy of the object.
		/// </summary>
		/// <typeparam name="T">The type of object being copied.</typeparam>
		/// <param name="source">The object instance to copy.</param>
		/// <returns>The copied object.</returns>
		public static T Clone<T>(T source)
		{
			if (!typeof (T).IsSerializable)
			{
				throw new ArgumentException("The type must be serializable.", "source");
			}

			// Don't serialize a null object, simply return the default for that object
			if (Object.ReferenceEquals(source, null))
			{
				return default(T);
			}

			//IFormatter formatter = new BinaryFormatter();
			Stream stream = new MemoryStream();
			using (stream)
			{
				System.Text.Json.JsonSerializer.Serialize(stream, source, GetJsonSerializerOptions());
				//formatter.Serialize(stream, source);
				stream.Seek(0, SeekOrigin.Begin);
				//return (T) formatter.Deserialize(stream);

				return (T)System.Text.Json.JsonSerializer.Deserialize<T>(stream, GetJsonSerializerOptions());
			}
		}

		/// <summary>
		/// Perform a deep Copy of the object, using Json as a serialisation method.
		/// </summary>
		/// <typeparam name="T">The type of object being copied.</typeparam>
		/// <param name="source">The object instance to copy.</param>
		/// <returns>The copied object.</returns>
		public static T CloneJson<T>(this T source)
		{
			// Don't serialize a null object, simply return the default for that object
			if (Object.ReferenceEquals(source, null))
			{
				return default(T);
			}

			//return ObjectSerialization.Deserialize<T>(ObjectSerialization.Serialize(source));
			return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source));
		}

		/// <summary>
		/// Perform a deep Copy of the object, using Json as a serialisation method returns a string
		/// </summary>
		/// <typeparam name="T">The type of object being copied.</typeparam>
		/// <param name="source">The object instance to copy.</param>
		/// <returns>The copied object as a json string</returns>
		public static string CloneJsonToString<T>(this T source)
		{
			// Don't serialize a null object, simply return the default for that object
			if (Object.ReferenceEquals(source, null))
			{
				return "";
			}

			//return ObjectSerialization.Deserialize<T>(ObjectSerialization.Serialize(source));
			return JsonConvert.SerializeObject(source);
		}

		private static JsonSerializerOptions GetJsonSerializerOptions()
		{
			return new JsonSerializerOptions()
			{
				PropertyNamingPolicy = null,
				WriteIndented = true,
				AllowTrailingCommas = true,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			};
		}
	}
}
