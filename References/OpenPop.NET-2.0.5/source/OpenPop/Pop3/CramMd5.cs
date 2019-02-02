using System;
using System.Security.Cryptography;
using System.Text;

namespace OpenPop.Pop3
{
	/// <summary>
	/// Implements the CRAM-MD5 algorithm as specified in <a href="http://tools.ietf.org/html/rfc2195">RFC 2195</a>.
	/// </summary>
	internal static class CramMd5
	{
		/// <summary>
		/// Defined by <a href="http://tools.ietf.org/html/rfc2104#section-2">RFC 2104</a>
		/// Is a 64 byte array with all entries set to 0x36.
		/// </summary>
		private static readonly byte[] ipad;

		/// <summary>
		/// Defined by <a href="http://tools.ietf.org/html/rfc2104#section-2">RFC 2104</a>
		/// Is a 64 byte array with all entries set to 0x5C.
		/// </summary>
		private static readonly byte[] opad;

		/// <summary>
		/// Initializes the static fields
		/// </summary>
		static CramMd5()
		{
			ipad = new byte[64];
			opad = new byte[64];
			for (int i = 0; i < ipad.Length; i++)
			{
				ipad[i] = 0x36;
				opad[i] = 0x5C;
			}
		}

		/// <summary>
		/// Computes the digest needed to login to a server using CRAM-MD5.<br/>
		/// <br/>
		/// This computes:<br/>
		/// MD5((password XOR opad), MD5((password XOR ipad), challenge))
		/// </summary>
		/// <param name="username">The username of the user who wants to log in</param>
		/// <param name="password">The password for the <paramref name="username"/></param>
		/// <param name="challenge">
		/// The challenge received from the server when telling it CRAM-MD5 authenticated is wanted.
		/// Is a base64 encoded string.
		/// </param>
		/// <returns>The response to the challenge, which the server can validate and log in the user if correct</returns>
		/// <exception cref="ArgumentNullException">
		/// If <paramref name="username"/>, 
		/// <paramref name="password"/> or 
		/// <paramref name="challenge"/> is <see langword="null"/>
		/// </exception>
		internal static string ComputeDigest(string username, string password, string challenge)
		{
			if(username == null)
				throw new ArgumentNullException("username");
			
			if(password == null)
				throw new ArgumentNullException("password");

			if(challenge == null)
				throw new ArgumentNullException("challenge");

			// Get the password bytes
			byte[] passwordBytes = GetSharedSecretInBytes(password);

			// The challenge is encoded in base64
			byte[] challengeBytes = Convert.FromBase64String(challenge);

			// Now XOR the password with the opad and ipad magic bytes
			byte[] passwordOpad = Xor(passwordBytes, opad);
			byte[] passwordIpad = Xor(passwordBytes, ipad);

			// Now do the computation: MD5((password XOR opad), MD5((password XOR ipad), challenge))
			byte[] digestValue = Hash(Concatenate(passwordOpad, Hash(Concatenate(passwordIpad, challengeBytes))));

			// Convert the bytes to a hex string
			// BitConverter writes the output as AF-B3-...
			// We need lower-case output without "-"
			string hex = BitConverter.ToString(digestValue).Replace("-", "").ToLowerInvariant();

			// Include the username in the resulting base64 encoded response
			return Convert.ToBase64String(Encoding.ASCII.GetBytes(username + " " + hex));
		}

		/// <summary>
		/// Hashes a byte array using the MD5 algorithm.
		/// </summary>
		/// <param name="toHash">The byte array to hash</param>
		/// <returns>The result of hashing the <paramref name="toHash"/> bytes with the MD5 algorithm</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="toHash"/> is <see langword="null"/></exception>
		private static byte[] Hash(byte[] toHash)
		{
			if(toHash == null)
				throw new ArgumentNullException("toHash");

			using (MD5 md5 = new MD5CryptoServiceProvider())
			{
				return md5.ComputeHash(toHash);
			}
		}

		/// <summary>
		/// Concatenates two byte arrays into one
		/// </summary>
		/// <param name="one">The first byte array</param>
		/// <param name="two">The second byte array</param>
		/// <returns>A concatenated byte array</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="one"/> or <paramref name="two"/> is <see langword="null"/></exception>
		private static byte[] Concatenate(byte[] one, byte[] two)
		{
			if(one == null)
				throw new ArgumentNullException("one");

			if(two == null)
				throw new ArgumentNullException("two");

			// Create space for both byte arrays in one
			byte[] concatenated = new byte[one.Length + two.Length];

			// Copy the first one over
			Buffer.BlockCopy(one, 0, concatenated, 0, one.Length);

			// Copy the second one over
			Buffer.BlockCopy(two, 0, concatenated, one.Length, two.Length);

			// Return result
			return concatenated;
		}

		/// <summary>
		/// XORs a byte array with another.<br/>
		/// Each byte in <paramref name="toXor"/> is XORed with the corresponding byte
		/// in <paramref name="toXorWith"/> until the end of <paramref name="toXor"/> is encountered.
		/// </summary>
		/// <param name="toXor">The byte array to XOR</param>
		/// <param name="toXorWith">The byte array to XOR with</param>
		/// <returns>A new byte array with the XORed results</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="toXor"/> or <paramref name="toXorWith"/> is <see langword="null"/></exception>
		/// <exception cref="ArgumentException">If the lengths of the arrays are not equal</exception>
		private static byte[] Xor(byte[] toXor, byte[] toXorWith)
		{
			if(toXor == null)
				throw new ArgumentNullException("toXor");

			if(toXorWith == null)
				throw new ArgumentNullException("toXorWith");

			if(toXor.Length != toXorWith.Length)
				throw new ArgumentException("The lengths of the arrays must be equal");

			// Create a new array to store results in
			byte[] xored = new byte[toXor.Length];

			// XOR each individual byte.
			for(int i = 0; i<toXor.Length; i++)
			{
				xored[i] = toXor[i];
				xored[i] ^= toXorWith[i];
			}

			// Return result
			return xored;
		}
		/// <summary>
		/// This method is responsible to generate the byte array needed
		/// from the shared secret - the password.<br/>
		/// 
		/// RFC 2195 says:<br/>
		/// The shared secret is null-padded to a length of 64 bytes. If the
		/// shared secret is longer than 64 bytes, the MD5 digest of the
		/// shared secret is used as a 16 byte input to the keyed MD5
		/// calculation.
		/// </summary>
		/// <param name="password">This is the shared secret</param>
		/// <returns>The 64 bytes that is to be used from the shared secret</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="password"/> is <see langword="null"/></exception>
		private static byte[] GetSharedSecretInBytes(string password)
		{
			if(password == null)
				throw new ArgumentNullException("password");

			// Get the password in bytes
			byte[] passwordBytes = Encoding.ASCII.GetBytes(password);

			// If the length is larger than 64, we need to
			if (passwordBytes.Length > 64)
			{
				passwordBytes = new MD5CryptoServiceProvider().ComputeHash(passwordBytes);
			}
			
			if(passwordBytes.Length != 64)
			{
				byte[] returner = new byte[64];
				for(int i = 0; i<passwordBytes.Length; i++)
				{
					returner[i] = passwordBytes[i];
				}
				return returner;
			}

			return passwordBytes;
		}
	}
}