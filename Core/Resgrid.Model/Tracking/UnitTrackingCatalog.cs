using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Resgrid.Model.Tracking
{
	public static class UnitTrackingCatalog
	{
		private const string CatalogResourceSuffix =
			".Tracking.Catalog.unit-tracking-catalog.json";

		private static readonly Lazy<UnitTrackingCatalogDocument> Catalog =
			new(LoadAndValidate);

		public static IReadOnlyCollection<UnitTrackingCatalogProfile> Profiles =>
			Catalog.Value.Profiles;

		public static IReadOnlyCollection<UnitTrackingIoMap> IoMaps =>
			Catalog.Value.IoMaps;

		public static UnitTrackingCatalogProfile GetProfile(string profileKey)
		{
			if (string.IsNullOrWhiteSpace(profileKey))
				return null;

			return Profiles.FirstOrDefault(profile =>
				string.Equals(
					profile.Key,
					profileKey.Trim(),
					StringComparison.OrdinalIgnoreCase));
		}

		public static UnitTrackingIoMap GetIoMap(string ioMapKey)
		{
			if (string.IsNullOrWhiteSpace(ioMapKey))
				return null;

			return IoMaps.FirstOrDefault(ioMap =>
				string.Equals(
					ioMap.Key,
					ioMapKey.Trim(),
					StringComparison.OrdinalIgnoreCase));
		}

		private static UnitTrackingCatalogDocument LoadAndValidate()
		{
			var assembly = typeof(UnitTrackingCatalog).Assembly;
			var resourceName = assembly
				.GetManifestResourceNames()
				.SingleOrDefault(name =>
					name.EndsWith(
						CatalogResourceSuffix,
						StringComparison.Ordinal));
			if (resourceName == null)
			{
				throw new InvalidOperationException(
					"The embedded unit tracking catalog was not found.");
			}

			using var stream = assembly.GetManifestResourceStream(
				resourceName);
			if (stream == null)
			{
				throw new InvalidOperationException(
					"The embedded unit tracking catalog could not be opened.");
			}

			using var reader = new StreamReader(stream);
			UnitTrackingCatalogDocument document;
			try
			{
				document = JsonConvert.DeserializeObject<
					UnitTrackingCatalogDocument>(
					reader.ReadToEnd(),
					new JsonSerializerSettings
					{
						MissingMemberHandling =
							MissingMemberHandling.Error,
						Converters =
						{
							new StringEnumConverter()
						}
					});
			}
			catch (JsonException ex)
			{
				throw new InvalidOperationException(
					"The embedded unit tracking catalog is invalid.",
					ex);
			}

			Validate(document);
			document.Profiles =
				document.Profiles.ToList().AsReadOnly();
			document.IoMaps =
				document.IoMaps.ToList().AsReadOnly();
			foreach (var profile in document.Profiles)
			{
				profile.SupportedTransports =
					profile.SupportedTransports.ToList().AsReadOnly();
				profile.CertifiedTransports =
					profile.CertifiedTransports.ToList().AsReadOnly();
				profile.SupportedAuthModes =
					profile.SupportedAuthModes.ToList().AsReadOnly();
			}
			foreach (var ioMap in document.IoMaps)
			{
				ioMap.Mappings =
					ioMap.Mappings.ToList().AsReadOnly();
			}

			return document;
		}

		private static void Validate(
			UnitTrackingCatalogDocument document)
		{
			if (document?.Profiles == null ||
			    document.Profiles.Count == 0)
			{
				throw new InvalidOperationException(
					"The unit tracking catalog must contain profiles.");
			}
			if (document.IoMaps == null)
			{
				throw new InvalidOperationException(
					"The unit tracking catalog must contain an ioMaps collection.");
			}

			ValidateUniqueKeys(
				document.Profiles.Select(profile => profile?.Key),
				"profile");
			ValidateUniqueKeys(
				document.IoMaps.Select(ioMap => ioMap?.Key),
				"I/O map");

			foreach (var ioMap in document.IoMaps)
				ValidateIoMap(ioMap);
			foreach (var profile in document.Profiles)
				ValidateProfile(profile, document.IoMaps);
		}

		private static void ValidateProfile(
			UnitTrackingCatalogProfile profile,
			IReadOnlyCollection<UnitTrackingIoMap> ioMaps)
		{
			if (profile == null)
				throw new InvalidOperationException(
					"The unit tracking catalog contains a null profile.");

			RequireText(profile.Key, "profile key");
			RequireCanonicalKey(profile.Key, "profile key");
			RequireText(
				profile.ManufacturerKey,
				$"manufacturer key for '{profile.Key}'");
			RequireCanonicalKey(
				profile.ManufacturerKey,
				$"manufacturer key for '{profile.Key}'");
			RequireText(
				profile.ManufacturerName,
				$"manufacturer name for '{profile.Key}'");
			RequireText(
				profile.Model,
				$"model for '{profile.Key}'");
			RequireText(
				profile.ProtocolKey,
				$"protocol key for '{profile.Key}'");
			RequireCanonicalKey(
				profile.ProtocolKey,
				$"protocol key for '{profile.Key}'");
			RequireText(
				profile.DecoderVariant,
				$"decoder variant for '{profile.Key}'");
			RequireCanonicalKey(
				profile.DecoderVariant,
				$"decoder variant for '{profile.Key}'");
			RequireText(
				profile.ProtocolDocumentVersion,
				$"protocol document version for '{profile.Key}'");
			RequireText(
				profile.SetupSummary,
				$"setup summary for '{profile.Key}'");
			RequireText(
				profile.RetryExpectation,
				$"retry expectation for '{profile.Key}'");

			if (!Enum.IsDefined(profile.TransportType) ||
			    profile.TransportType ==
			    UnitTrackingTransportType.Unknown)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' has an invalid transport type.");
			}
			if (!Enum.IsDefined(profile.CertificationStatus) ||
			    profile.CertificationStatus ==
			    UnitTrackingCertificationStatus.Unknown)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' has an invalid certification status.");
			}
			if (profile.SupportedTransports == null ||
			    profile.SupportedTransports.Count == 0)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' must declare supported transports.");
			}
			if (profile.CertifiedTransports == null ||
			    profile.SupportedAuthModes == null)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' contains a null collection.");
			}
			if (profile.SupportedAuthModes.Any(mode =>
				    !Enum.IsDefined(mode) ||
				    mode == UnitTrackingAuthMode.Unknown) ||
			    profile.SupportedAuthModes.Distinct().Count() !=
			    profile.SupportedAuthModes.Count)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' declares an invalid authentication mode.");
			}
			if (!string.IsNullOrWhiteSpace(
				    profile.PayloadAdapterKey))
			{
				RequireCanonicalKey(
					profile.PayloadAdapterKey,
					$"payload adapter key for '{profile.Key}'");
			}

			ValidateTransports(
				profile.Key,
				profile.SupportedTransports);
			ValidateTransports(
				profile.Key,
				profile.CertifiedTransports);
			if (profile.CertifiedTransports.Any(transport =>
				    !profile.SupportedTransports.Contains(
					    transport,
					    StringComparer.OrdinalIgnoreCase)))
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' certifies an unsupported transport.");
			}
			if (profile.CertificationStatus ==
				    UnitTrackingCertificationStatus.Certified &&
			    profile.CertifiedTransports.Count == 0)
			{
				throw new InvalidOperationException(
					$"Certified profile '{profile.Key}' must declare a certified transport.");
			}
			if (profile.CertificationStatus !=
				    UnitTrackingCertificationStatus.Certified &&
			    profile.CertifiedTransports.Count != 0)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' cannot certify transports before profile certification.");
			}
			if (profile.IsSelectable &&
			    profile.CertificationStatus !=
			    UnitTrackingCertificationStatus.Certified)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' cannot be selectable before certification.");
			}

			var nativeProfile =
				profile.TransportType ==
				UnitTrackingTransportType.NativeTcpUdp;
			if (nativeProfile &&
			    profile.SupportedTransports.Any(transport =>
				    !string.Equals(
					    transport,
					    "Tcp",
					    StringComparison.OrdinalIgnoreCase) &&
				    !string.Equals(
					    transport,
					    "Udp",
					    StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidOperationException(
					$"Native profile '{profile.Key}' declares a non-socket transport.");
			}
			if (!nativeProfile &&
			    string.IsNullOrWhiteSpace(
				    profile.PayloadAdapterKey))
			{
				throw new InvalidOperationException(
					$"Non-native profile '{profile.Key}' must declare a payload adapter.");
			}

			if (string.IsNullOrWhiteSpace(profile.IoMapKey))
				return;

			RequireCanonicalKey(
				profile.IoMapKey,
				$"I/O map key for '{profile.Key}'");
			var ioMap = ioMaps.SingleOrDefault(candidate =>
				string.Equals(
					candidate.Key,
					profile.IoMapKey,
					StringComparison.OrdinalIgnoreCase));
			if (ioMap == null)
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' references an unknown I/O map.");
			}
			if (!string.Equals(
				    ioMap.ProtocolKey,
				    profile.ProtocolKey,
				    StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"Profile '{profile.Key}' references an I/O map for another protocol.");
			}
		}

		private static void ValidateIoMap(
			UnitTrackingIoMap ioMap)
		{
			if (ioMap == null)
				throw new InvalidOperationException(
					"The unit tracking catalog contains a null I/O map.");

			RequireText(ioMap.Key, "I/O map key");
			RequireCanonicalKey(ioMap.Key, "I/O map key");
			RequireText(
				ioMap.ProtocolKey,
				$"protocol key for I/O map '{ioMap.Key}'");
			RequireCanonicalKey(
				ioMap.ProtocolKey,
				$"protocol key for I/O map '{ioMap.Key}'");
			RequireText(
				ioMap.ProtocolDocumentVersion,
				$"protocol document version for I/O map '{ioMap.Key}'");
			if (ioMap.Mappings == null ||
			    ioMap.Mappings.Count == 0)
			{
				throw new InvalidOperationException(
					$"I/O map '{ioMap.Key}' must contain mappings.");
			}
			if (ioMap.Mappings.GroupBy(mapping => mapping.AvlId)
			    .Any(group => group.Count() > 1))
			{
				throw new InvalidOperationException(
					$"I/O map '{ioMap.Key}' contains a duplicate AVL ID.");
			}
			if (ioMap.Mappings.GroupBy(mapping => mapping.Target)
			    .Any(group => group.Count() > 1))
			{
				throw new InvalidOperationException(
					$"I/O map '{ioMap.Key}' contains a duplicate canonical target.");
			}

			foreach (var mapping in ioMap.Mappings)
			{
				if (mapping.AvlId < 0 ||
				    mapping.AvlId > ushort.MaxValue)
				{
					throw new InvalidOperationException(
						$"I/O map '{ioMap.Key}' contains an invalid AVL ID.");
				}
				if (mapping.ValueBytes != 1 &&
				    mapping.ValueBytes != 2 &&
				    mapping.ValueBytes != 4 &&
				    mapping.ValueBytes != 8)
				{
					throw new InvalidOperationException(
						$"I/O map '{ioMap.Key}' contains an invalid value width.");
				}
				if (!Enum.IsDefined(mapping.Target) ||
				    mapping.Target ==
				    UnitTrackingIoTarget.Unknown)
				{
					throw new InvalidOperationException(
						$"I/O map '{ioMap.Key}' contains an invalid target.");
				}
				if (mapping.Multiplier <= 0)
				{
					throw new InvalidOperationException(
						$"I/O map '{ioMap.Key}' contains an invalid multiplier.");
				}
				var maximumValue =
					mapping.ValueBytes == 8
						? ulong.MaxValue
						: (1UL << (mapping.ValueBytes * 8)) - 1;
				if (!mapping.MinimumRawValue.HasValue ||
				    !mapping.MaximumRawValue.HasValue ||
				    mapping.MinimumRawValue.Value >
				    mapping.MaximumRawValue.Value ||
				    mapping.MaximumRawValue.Value >
				    maximumValue)
				{
					throw new InvalidOperationException(
						$"I/O map '{ioMap.Key}' contains an invalid raw value range.");
				}
			}
		}

		private static void ValidateUniqueKeys(
			IEnumerable<string> keys,
			string itemType)
		{
			if (keys.Any(string.IsNullOrWhiteSpace))
			{
				throw new InvalidOperationException(
					$"The unit tracking catalog contains a {itemType} without a key.");
			}
			if (keys.GroupBy(
				    key => key,
				    StringComparer.OrdinalIgnoreCase)
			    .Any(group => group.Count() > 1))
			{
				throw new InvalidOperationException(
					$"The unit tracking catalog contains a duplicate {itemType} key.");
			}
		}

		private static void ValidateTransports(
			string profileKey,
			IReadOnlyCollection<string> transports)
		{
			if (transports.Any(transport =>
				    !string.Equals(
					    transport,
					    "Https",
					    StringComparison.Ordinal) &&
				    !string.Equals(
					    transport,
					    "Tcp",
					    StringComparison.Ordinal) &&
				    !string.Equals(
					    transport,
					    "Udp",
					    StringComparison.Ordinal)))
			{
				throw new InvalidOperationException(
					$"Profile '{profileKey}' declares an invalid transport.");
			}
			if (transports.Distinct(
				    StringComparer.OrdinalIgnoreCase)
			    .Count() != transports.Count)
			{
				throw new InvalidOperationException(
					$"Profile '{profileKey}' declares a duplicate transport.");
			}
		}

		private static void RequireText(
			string value,
			string fieldName)
		{
			if (string.IsNullOrWhiteSpace(value) ||
			    !string.Equals(
				    value,
				    value.Trim(),
				    StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					$"The unit tracking catalog has an invalid {fieldName}.");
			}
		}

		private static void RequireCanonicalKey(
			string value,
			string fieldName)
		{
			if (value.StartsWith(
				    "-",
				    StringComparison.Ordinal) ||
			    value.EndsWith(
				    "-",
				    StringComparison.Ordinal) ||
			    value.Contains(
				    "--",
				    StringComparison.Ordinal) ||
			    value.Any(character =>
				    !(character >= 'a' && character <= 'z') &&
				    !(character >= '0' && character <= '9') &&
				    character != '-'))
			{
				throw new InvalidOperationException(
					$"The unit tracking catalog has a non-canonical {fieldName}.");
			}
		}

		private sealed class UnitTrackingCatalogDocument
		{
			public IReadOnlyCollection<UnitTrackingCatalogProfile> Profiles { get; set; }
			public IReadOnlyCollection<UnitTrackingIoMap> IoMaps { get; set; }
		}
	}
}
