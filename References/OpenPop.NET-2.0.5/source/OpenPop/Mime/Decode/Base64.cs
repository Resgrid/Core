using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OpenPop.Common.Logging;

namespace OpenPop.Mime.Decode
{
	/// <summary>
	/// Utility class for dealing with Base64 encoded strings
	/// </summary>
	internal static class Base64
	{
		/// <summary>
		/// Decodes a base64 encoded string into the bytes it describes
		/// </summary>
		/// <param name="base64Encoded">The string to decode</param>
		/// <returns>A byte array that the base64 string described</returns>
		public static byte[] Decode(string base64Encoded)
		{
			// According to http://www.tribridge.com/blog/crm/blogs/brandon-kelly/2011-04-29/Solving-OutOfMemoryException-errors-when-attempting-to-attach-large-Base64-encoded-content-into-CRM-annotations.aspx
			// System.Convert.ToBase64String may leak a lot of memory
			// An OpenPop user reported that OutOfMemoryExceptions were thrown, and supplied the following
			// code for the fix. This should not have memory leaks.
			// The code is nearly identical to the example on MSDN:
			// http://msdn.microsoft.com/en-us/library/system.security.cryptography.frombase64transform.aspx#exampleToggle
			try
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					base64Encoded = base64Encoded.Replace("\r\n", "");

					byte[] inputBytes = Encoding.ASCII.GetBytes(base64Encoded);

					using (FromBase64Transform transform = new FromBase64Transform(FromBase64TransformMode.DoNotIgnoreWhiteSpaces))
					{
						byte[] outputBytes = new byte[transform.OutputBlockSize];

						// Transform the data in chunks the size of InputBlockSize.
						const int inputBlockSize = 4;
						int currentOffset = 0;
						while (inputBytes.Length - currentOffset > inputBlockSize)
						{
							transform.TransformBlock(inputBytes, currentOffset, inputBlockSize, outputBytes, 0);
							currentOffset += inputBlockSize;
							memoryStream.Write(outputBytes, 0, transform.OutputBlockSize);
						}

						// Transform the final block of data.
						outputBytes = transform.TransformFinalBlock(inputBytes, currentOffset, inputBytes.Length - currentOffset);
						memoryStream.Write(outputBytes, 0, outputBytes.Length);
					}

					return memoryStream.ToArray();
				}
			} catch (FormatException e)
			{
				DefaultLogger.Log.LogError("Base64: (FormatException) " + e.Message + "\r\nOn string: " + base64Encoded);
				throw;
			}
		}

		/// <summary>
		/// Decodes a Base64 encoded string using a specified <see cref="System.Text.Encoding"/> 
		/// </summary>
		/// <param name="base64Encoded">Source string to decode</param>
		/// <param name="encoding">The encoding to use for the decoded byte array that <paramref name="base64Encoded"/> describes</param>
		/// <returns>A decoded string</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="base64Encoded"/> or <paramref name="encoding"/> is <see langword="null"/></exception>
		/// <exception cref="FormatException">If <paramref name="base64Encoded"/> is not a valid base64 encoded string</exception>
		public static string Decode(string base64Encoded, Encoding encoding)
		{
			if(base64Encoded == null)
				throw new ArgumentNullException("base64Encoded");

			if(encoding == null)
				throw new ArgumentNullException("encoding");

			return encoding.GetString(Decode(base64Encoded));
		}
	}
}