using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Resgrid.Web.Attributes
{
	/// <summary>
	/// Validates that a string's UTF-8 encoded byte count does not exceed a maximum. Use when the
	/// stored representation depends on byte length (e.g. values encrypted before persisting), where
	/// a character-count check (StringLength) would pass multi-byte Unicode input that overflows a
	/// fixed-size column.
	/// </summary>
	public sealed class MaxUtf8BytesAttribute : ValidationAttribute
	{
		private readonly int _maxBytes;

		public MaxUtf8BytesAttribute(int maxBytes)
			: base($"Cannot exceed {maxBytes} bytes when UTF-8 encoded.")
		{
			_maxBytes = maxBytes;
		}

		public override bool IsValid(object value)
		{
			if (value is null)
				return true;

			return value is string s && Encoding.UTF8.GetByteCount(s) <= _maxBytes;
		}
	}
}
