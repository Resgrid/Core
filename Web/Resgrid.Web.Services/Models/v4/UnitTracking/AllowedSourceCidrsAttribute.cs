using System;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Resgrid.Web.Services.Models.v4.UnitTracking
{
	[AttributeUsage(AttributeTargets.Property)]
	public sealed class AllowedSourceCidrsAttribute : ValidationAttribute
	{
		protected override ValidationResult IsValid(object value, ValidationContext validationContext)
		{
			if (value == null)
				return ValidationResult.Success;

			if (value is not string cidrs || string.IsNullOrWhiteSpace(cidrs))
				return ValidationResult.Success;

			var entries = cidrs.Split(
				',',
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (entries.Length == 0)
				return new ValidationResult(
					"Allowed source CIDRs must contain at least one canonical CIDR entry.",
					new[] { validationContext.MemberName });

			foreach (var entry in entries)
			{
				var parts = entry.Split('/', 2, StringSplitOptions.None);
				if (parts.Length != 2 ||
				    !IPAddress.TryParse(parts[0], out var address) ||
				    !int.TryParse(parts[1], out var prefixLength) ||
				    prefixLength < 0 ||
				    prefixLength > address.GetAddressBytes().Length * 8 ||
				    !IPNetwork.TryParse(entry, out var network) ||
				    !string.Equals(entry, network.ToString(), StringComparison.Ordinal))
				{
					return new ValidationResult(
						$"Allowed source CIDR entry '{entry}' is not a canonical IPv4 or IPv6 CIDR (for example 10.0.0.0/8).",
						new[] { validationContext.MemberName });
				}
			}

			return ValidationResult.Success;
		}
	}
}
