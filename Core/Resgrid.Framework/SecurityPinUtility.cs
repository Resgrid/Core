using System.Security.Cryptography;

namespace Resgrid.Framework
{
	/// <summary>
	/// Generation and validation rules for the 4-digit user security PIN used as a step-up
	/// (2FA-style) check for dangerous/department-wide chatbot and SMS actions.
	/// </summary>
	public static class SecurityPinUtility
	{
		public const int PinLength = 4;

		/// <summary>True when the value is exactly <see cref="PinLength"/> ASCII digits.</summary>
		public static bool IsValidFormat(string pin)
		{
			if (string.IsNullOrWhiteSpace(pin) || pin.Length != PinLength)
				return false;

			foreach (var c in pin)
			{
				if (c < '0' || c > '9')
					return false;
			}

			return true;
		}

		/// <summary>
		/// True for trivially guessable PINs: all-same-digit (0000, 1111, ...) and consecutive
		/// ascending/descending runs (1234, 0123, 4321, 9876, ...).
		/// </summary>
		public static bool IsWeak(string pin)
		{
			if (!IsValidFormat(pin))
				return true;

			bool allSame = true, ascending = true, descending = true;
			for (int i = 1; i < pin.Length; i++)
			{
				if (pin[i] != pin[0])
					allSame = false;
				if (pin[i] != pin[i - 1] + 1)
					ascending = false;
				if (pin[i] != pin[i - 1] - 1)
					descending = false;
			}

			return allSame || ascending || descending;
		}

		/// <summary>Valid format and not weak.</summary>
		public static bool IsAcceptable(string pin) => IsValidFormat(pin) && !IsWeak(pin);

		/// <summary>Generates a random PIN that passes <see cref="IsAcceptable"/> using a crypto RNG.</summary>
		public static string Generate()
		{
			using var rng = RandomNumberGenerator.Create();
			var bytes = new byte[4];

			while (true)
			{
				rng.GetBytes(bytes);
				uint value = (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
				string pin = (value % 10000).ToString().PadLeft(PinLength, '0');

				if (IsAcceptable(pin))
					return pin;
			}
		}
	}
}
