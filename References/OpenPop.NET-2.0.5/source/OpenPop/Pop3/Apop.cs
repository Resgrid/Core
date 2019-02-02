using System;
using System.Security.Cryptography;
using System.Text;

namespace OpenPop.Pop3
{
	/// <summary>
	/// Class for computing the digest needed when issuing the APOP command to a POP3 server.
	/// </summary>
	internal static class Apop
	{
		/// <summary>
		/// Create the digest for the APOP command so that the server can validate
		/// we know the password for some user.
		/// </summary>
		/// <param name="password">The password for the user</param>
		/// <param name="serverTimestamp">The timestamp advertised in the server greeting to the POP3 client</param>
		/// <returns>The password and timestamp hashed with the MD5 algorithm outputted as a HEX string</returns>
		public static string ComputeDigest(string password, string serverTimestamp)
		{
			if (password == null)
				throw new ArgumentNullException("password");

			if (serverTimestamp == null)
				throw new ArgumentNullException("serverTimestamp");

			// The APOP command authorizes itself by using the password together
			// with the server timestamp. This way the password is not transmitted
			// in clear text, and the server can still verify we have the password.
			byte[] digestToHash = Encoding.ASCII.GetBytes(serverTimestamp + password);

			using (MD5 md5 = new MD5CryptoServiceProvider())
			{
				// MD5 hash the digest
				byte[] result = md5.ComputeHash(digestToHash);

				// Convert the bytes to a hex string
				// BitConverter writes the output as AF-B3-...
				// We need lower-case output without "-"
				return BitConverter.ToString(result).Replace("-", "").ToLowerInvariant();
			}
		}
	}
}