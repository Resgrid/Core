using System;
using System.IO;
using System.Text;
using ProtoBuf;

namespace Resgrid.Framework
{
	public static class ObjectSerialization
	{
		public static string Serialize(object o)
		{
			String XmlizedString = null;
			var memoryStream = new MemoryStream();

			Serializer.Serialize(memoryStream, o);
			string stringBase64 = Convert.ToBase64String(memoryStream.ToArray());

			return stringBase64;
		}

		public static T Deserialize<T>(string o)
		{
			byte[] byteAfter64 = Convert.FromBase64String(o);
			MemoryStream memoryStream = new MemoryStream(byteAfter64);

			return Serializer.Deserialize<T>(memoryStream);
		}

		/// <summary>
		/// To convert a Byte Array of Unicode values (UTF-8 encoded) to a complete String.
		/// </summary>
		/// <param name="characters">Unicode Byte Array to be converted to String</param>
		/// <returns>String converted from Unicode Byte Array</returns>
		private static String UTF8ByteArrayToString(Byte[] characters)
		{
			UTF8Encoding encoding = new UTF8Encoding();
			String constructedString = encoding.GetString(characters);

			return (constructedString);
		}

		/// <summary>
		/// Converts the String to UTF8 Byte array and is used in De serialization
		/// </summary>
		/// <param name="pXmlString"></param>
		/// <returns></returns>
		private static Byte[] StringToUTF8ByteArray(String pXmlString)
		{
			UTF8Encoding encoding = new UTF8Encoding();
			Byte[] byteArray = encoding.GetBytes(pXmlString);

			return byteArray;
		}
	}
}
