using System;
using System.Text;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitTrackingIdentifierService : IUnitTrackingIdentifierService
	{
		private const int MaximumIdentifierLength = 128;

		public string Normalize(string identifier)
		{
			if (string.IsNullOrWhiteSpace(identifier))
				return null;

			var normalized = identifier
				.Trim()
				.Normalize(NormalizationForm.FormKC)
				.ToUpperInvariant();

			if (normalized.Length > MaximumIdentifierLength)
				throw new ArgumentOutOfRangeException(
					nameof(identifier),
					$"Tracking identifiers cannot exceed {MaximumIdentifierLength} characters.");

			return normalized;
		}

		public string Mask(string identifier)
		{
			var normalized = Normalize(identifier);
			if (normalized == null)
				return null;

			if (normalized.Length <= 4)
				return new string('*', normalized.Length);

			return new string('*', normalized.Length - 4) + normalized.Substring(normalized.Length - 4);
		}
	}
}
