// Inspired from http://www.codeproject.com/Articles/2393/A-C-Password-Generator

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Framework
{
	public static class RandomGenerator
	{
		private const int UBoundDigit = 61;

		public static int GetRandomNumber(int lBound, int uBound)
		{
			var rng = new RNGCryptoServiceProvider();

			// Assumes lBound >= 0 && lBound < uBound
			// returns an int >= lBound and < uBound
			uint urndnum;
			byte[] rndnum = new Byte[4];
			if (lBound == uBound - 1)
			{
				// test for degenerate case where only lBound can be returned
				return lBound;
			}

			uint xcludeRndBase = (uint.MaxValue -
														(uint.MaxValue % (uint)(uBound - lBound)));

			do
			{
				rng.GetBytes(rndnum);
				urndnum = System.BitConverter.ToUInt32(rndnum, 0);
			} while (urndnum >= xcludeRndBase);

			return (int)(urndnum % (uBound - lBound)) + lBound;
		}

		public static char GetRandomCharacter(bool excludeNumbers, bool excludeLowercaseLetters,
				bool excludeUppercaseLetters, bool excludeSymbols)
		{
			var pwdCharArray = CreateCharacterArray(excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters,
					excludeSymbols);

			if (pwdCharArray.Length <= 0)
				return char.Parse(" ");

			int upperBound = pwdCharArray.GetUpperBound(0);

			int randomCharPosition = GetRandomNumber(
					pwdCharArray.GetLowerBound(0), upperBound);

			char randomChar = pwdCharArray[randomCharPosition];

			return randomChar;
		}

		public static string GenerateRandomString(int minimum, int maximum, bool excludeNumbers, bool excludeLowercaseLetters,
				bool excludeUppercaseLetters, bool excludeSymbols, bool allowConsecutiveCharacters, bool repeatCharacters, string charactersToExclude, HashSet<string> existingValues)
		{
			var generatingString = GenerateRandomString(minimum, maximum, excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters, excludeSymbols, allowConsecutiveCharacters,
					repeatCharacters, charactersToExclude);

			var doesExist = existingValues.Contains(generatingString);

			while (doesExist)
			{
				generatingString = GenerateRandomString(minimum, maximum, excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters, excludeSymbols, allowConsecutiveCharacters,
				 repeatCharacters, charactersToExclude);

				doesExist = existingValues.Contains(generatingString);
			}

			return generatingString;
		}

		public static string GenerateRandomString(int minimum, int maximum, bool excludeNumbers, bool excludeLowercaseLetters,
				bool excludeUppercaseLetters, bool excludeSymbols, bool allowConsecutiveCharacters, bool repeatCharacters, string charactersToExclude)
		{
			int pwdLength;

			// Pick random length between minimum and maximum
			if (minimum == maximum)
				pwdLength = minimum;
			else
				pwdLength = GetRandomNumber(minimum, maximum);

			StringBuilder pwdBuffer = new StringBuilder();
			pwdBuffer.Capacity = maximum;

			// Generate random characters
			char lastCharacter, nextCharacter;

			// Initial dummy character flag
			lastCharacter = nextCharacter = '\n';

			for (int i = 0; i < pwdLength; i++)
			{
				nextCharacter = GetRandomCharacter(excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters, excludeSymbols);

				if (!allowConsecutiveCharacters)
				{
					while (lastCharacter == nextCharacter)
					{
						nextCharacter = GetRandomCharacter(excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters, excludeSymbols);
					}
				}

				if (!repeatCharacters)
				{
					string temp = pwdBuffer.ToString();
					int duplicateIndex = temp.IndexOf(nextCharacter);
					while (-1 != duplicateIndex)
					{
						nextCharacter = GetRandomCharacter(excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters, excludeSymbols);
						duplicateIndex = temp.IndexOf(nextCharacter);
					}
				}

				if (!String.IsNullOrEmpty(charactersToExclude))
				{
					while (-1 != charactersToExclude.IndexOf(nextCharacter))
					{
						nextCharacter = GetRandomCharacter(excludeNumbers, excludeLowercaseLetters, excludeUppercaseLetters, excludeSymbols);
					}
				}

				pwdBuffer.Append(nextCharacter);
				lastCharacter = nextCharacter;
			}

			if (!String.IsNullOrWhiteSpace(pwdBuffer.ToString()))
				return pwdBuffer.ToString();

			return String.Empty;
		}


		public static string CreateCode(int passwordLength)
		{
			string allowedChars = "ABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
			char[] chars = new char[passwordLength];
			Random rd = new Random();

			for (int i = 0; i < passwordLength; i++)
			{
				chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
			}

			return new string(chars);
		}

		private static char[] CreateCharacterArray(bool excludeNumbers, bool excludeLowercaseLetters,
						bool excludeUppercaseLetters, bool excludeSymbols)
		{
			List<char> characters = new List<char>();

			if (!excludeNumbers)
				characters.AddRange("0123456789".ToCharArray());

			if (!excludeLowercaseLetters)
				characters.AddRange("abcdefghijklmnopqrstuvwxyz".ToCharArray());

			if (!excludeUppercaseLetters)
				characters.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());

			if (!excludeSymbols)
				characters.AddRange("`~!@#$%^&*()-_=+[]{}\\|;:'\",<.>/?".ToCharArray());

			return characters.ToArray();
		}
	}
}