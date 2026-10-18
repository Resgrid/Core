using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resgrid.Model.Onboarding;

namespace Resgrid.Services.Onboarding
{
	/// <summary>What detection decided for one sheet (verification ladder L2).</summary>
	public sealed class ImportDetection
	{
		public string EntityKey { get; set; }
		/// <summary>0–100.</summary>
		public int Confidence { get; set; }
		/// <summary>"resgrid-template", "{pack}.{dataset}" or "generic".</summary>
		public string ProfileKey { get; set; }
		public ImportColumnMapOrigin Origin { get; set; }
		public ImportColumnMapBody Map { get; set; }
		/// <summary>Template and pack detections are confirmed without asking (the mapping is known).</summary>
		public bool AutoConfirm { get; set; }
	}

	/// <summary>
	/// Column matching and destination detection (wizard plan sections 6.3 steps 4–6 and 8.4). Every source becomes an
	/// ImportColumnMap: a Resgrid template (headers carry their field key in brackets), a recognized product pack (by its
	/// documented header signature) or a proposed mapping from header aliases that the administrator confirms.
	/// </summary>
	public static class ImportColumnMatcher
	{
		public const int AutoConfirmThreshold = 85;
		private static readonly Regex TemplateKey = new Regex(@"\[([a-z0-9_]+)\]\s*$", RegexOptions.CultureInvariant);

		/// <summary>Header words that mark data Resgrid never imports (section 8.2); the column is dropped before staging.</summary>
		private static readonly string[] ExcludedHeaderTerms =
		{
			"password", "passwd", "pwd", "pin", "pincode", "ssn", "socialsecurity", "sin", "nationalinsurance", "apikey", "secret", "token",
			"creditcard", "cardnumber", "cvv", "cvc", "iban", "routingnumber", "accountnumber", "dateofbirth", "dob", "birthdate", "birthday",
			"race", "ethnicity", "driverslicense", "driverlicense", "driverslicence", "knox", "gatecode", "alarmcode", "supra",
			"diagnosis", "medicalcondition", "allergies", "medication"
		};

		/// <summary>Multi-word terms also matched inside a run-together header ("DateOfBirth", "user_password"); the rest match whole words only.</summary>
		private static readonly string[] JoinedTerms =
		{
			"password", "passwd", "socialsecurity", "nationalinsurance", "apikey", "creditcard", "cardnumber", "routingnumber", "accountnumber",
			"dateofbirth", "birthdate", "driverslicense", "driverlicense", "driverslicence", "gatecode", "alarmcode", "medicalcondition"
		};

		private static readonly Regex SsnPattern = new Regex(@"^\d{3}-\d{2}-\d{4}$", RegexOptions.CultureInvariant);

		public static string KeyFromTemplateHeader(string header)
		{
			var match = TemplateKey.Match(header ?? string.Empty);
			return match.Success ? match.Groups[1].Value : null;
		}

		public static bool IsExampleMarker(string header) =>
			string.Equals(header?.Trim(), ImportTemplateMarkers.ExampleColumn, StringComparison.OrdinalIgnoreCase);

		/// <summary>True when the header names data excluded by decision (passwords, national identifiers, card numbers…).</summary>
		public static bool IsExcludedHeader(string header)
		{
			if (string.IsNullOrWhiteSpace(header))
				return false;

			// Whole words ("Knox Box Code", but not "Trace" for "race"), or a long term inside the run-together header ("dateofbirth").
			var tokens = Regex.Split(header.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+").Where(t => t.Length > 0).ToList();
			var joined = string.Concat(tokens);
			return ExcludedHeaderTerms.Any(tokens.Contains) || JoinedTerms.Any(joined.Contains);
		}

		/// <summary>True when most values look like card numbers (Luhn) or US social security numbers.</summary>
		public static bool LooksLikeExcludedValues(IEnumerable<string> samples)
		{
			var values = samples.Where(v => !string.IsNullOrWhiteSpace(v)).Take(50).ToList();
			if (values.Count < 3)
				return false;

			var hits = values.Count(v => SsnPattern.IsMatch(v.Trim()) || LooksLikeCardNumber(v));
			return hits * 2 > values.Count;
		}

		internal static bool LooksLikeCardNumber(string value)
		{
			var digits = new string(value.Where(char.IsDigit).ToArray());
			if (digits.Length < 13 || digits.Length > 19 || value.Any(c => !char.IsDigit(c) && c != ' ' && c != '-'))
				return false;

			var sum = 0;
			var alternate = false;
			for (var i = digits.Length - 1; i >= 0; i--)
			{
				var n = digits[i] - '0';
				if (alternate)
				{
					n *= 2;
					if (n > 9) n -= 9;
				}
				sum += n;
				alternate = !alternate;
			}
			return sum % 10 == 0;
		}

		public static ImportDetection Detect(IReadOnlyList<string> headers, string sheetName, IImportCatalog catalog)
		{
			var template = DetectTemplate(headers, catalog);
			if (template != null)
				return template;

			var pack = DetectPack(headers, sheetName, catalog);
			if (pack != null)
				return pack;

			ImportDetection best = null;
			foreach (var contract in catalog.Contracts)
			{
				var map = ProposeMap(headers, contract);
				var matched = map.Columns.Where(c => c.Kind != (int)ImportColumnBindingKind.Ignore).Select(c => c.FieldKey).Where(k => k != null).Distinct().ToList();
				if (matched.Count == 0)
					continue;

				var required = contract.Fields.Where(f => f.Required).Select(f => f.Key).ToList();
				var requiredShare = required.Count == 0 ? 1d : required.Count(r => matched.Contains(r)) / (double)required.Count;
				var columnShare = matched.Count / (double)Math.Max(1, headers.Count(h => !string.IsNullOrWhiteSpace(h)));
				var nameHint = SheetMentions(sheetName, contract) ? 10 : 0;
				var confidence = (int)Math.Round(Math.Min(100, requiredShare * 60 + columnShare * 40 + nameHint));

				if (best == null || confidence > best.Confidence)
					best = new ImportDetection
					{
						EntityKey = contract.Key,
						Confidence = confidence,
						ProfileKey = "generic",
						Origin = ImportColumnMapOrigin.Administrator,
						Map = map,
						AutoConfirm = false
					};
			}

			return best ?? new ImportDetection { Confidence = 0, ProfileKey = "generic", Origin = ImportColumnMapOrigin.Administrator, Map = new ImportColumnMapBody() };
		}

		private static ImportDetection DetectTemplate(IReadOnlyList<string> headers, IImportCatalog catalog)
		{
			if (!headers.Any(IsExampleMarker))
				return null;

			var keys = headers.Select(KeyFromTemplateHeader).Where(k => k != null).ToList();
			if (keys.Count == 0)
				return null;

			var contract = catalog.Contracts
				.Select(c => new { Contract = c, Hits = keys.Count(k => c.Field(k) != null) })
				.Where(x => x.Hits == keys.Count)
				.OrderByDescending(x => x.Contract.Fields.Count(f => keys.Contains(f.Key)))
				.Select(x => x.Contract)
				.FirstOrDefault();
			if (contract == null)
				return null;

			var map = new ImportColumnMapBody();
			foreach (var header in headers)
			{
				var key = KeyFromTemplateHeader(header);
				var field = key == null ? null : contract.Field(key);
				map.Columns.Add(field == null ? Ignore(header) : Bind(header, field, 100));
				if (field?.Key == "source_id")
					map.SourceIdentityColumn = header;
			}

			return new ImportDetection
			{
				EntityKey = contract.Key,
				Confidence = 100,
				ProfileKey = ImportTemplateMarkers.TemplateProfileKey,
				Origin = ImportColumnMapOrigin.ResgridTemplate,
				Map = map,
				AutoConfirm = true
			};
		}

		private static ImportDetection DetectPack(IReadOnlyList<string> headers, string sheetName, IImportCatalog catalog)
		{
			var normalized = headers.Select(ImportFieldConverter.Normalize).ToList();
			var disabled = new HashSet<string>((Config.ImportConfig.DisabledPacks ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
				StringComparer.OrdinalIgnoreCase);
			ImportDetection best = null;

			foreach (var pack in catalog.Packs.Where(p => p.Published && !disabled.Contains(p.Key)))
			{
				foreach (var dataset in pack.Datasets)
				{
					if (!dataset.RequiredHeaders.All(h => normalized.Contains(ImportFieldConverter.Normalize(h))))
						continue;

					var contract = catalog.GetContract(dataset.EntityKey);
					if (contract == null)
						continue;

					var optional = dataset.OptionalHeaders.Count(h => normalized.Contains(ImportFieldConverter.Normalize(h)));
					var sheetHint = dataset.SheetNames.Any(s => (sheetName ?? string.Empty).ToLowerInvariant().Contains(s)) ? 2 : 0;
					var confidence = Math.Min(100, 90 + optional + sheetHint);

					var map = new ImportColumnMapBody { SourceIdentityColumn = headers.FirstOrDefault(h => Same(h, dataset.SourceIdentityColumn)) };
					foreach (var header in headers)
					{
						var column = dataset.Columns.FirstOrDefault(c => Same(c.Key, header));
						var field = column.Value == null ? null : contract.Field(column.Value);
						map.Columns.Add(field == null || pack.DroppedColumns.Any(d => Same(d, header)) ? Ignore(header) : Bind(header, field, 95));
					}

					if (best == null || confidence > best.Confidence)
						best = new ImportDetection
						{
							EntityKey = contract.Key,
							Confidence = confidence,
							ProfileKey = $"{pack.Key}.{dataset.Key}",
							Origin = ImportColumnMapOrigin.ProductPack,
							Map = map,
							AutoConfirm = true
						};
				}
			}

			return best;
		}

		/// <summary>Proposes a mapping for a chosen destination from header aliases (the generic mapped import, section 8.4).</summary>
		public static ImportColumnMapBody ProposeMap(IReadOnlyList<string> headers, ImportEntityContract contract)
		{
			var map = new ImportColumnMapBody();
			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var header in headers)
			{
				if (string.IsNullOrWhiteSpace(header) || IsExampleMarker(header) || IsExcludedHeader(header))
				{
					map.Columns.Add(Ignore(header));
					continue;
				}

				var templateKey = KeyFromTemplateHeader(header);
				var normalized = ImportFieldConverter.Normalize(templateKey != null ? templateKey.Replace('_', ' ') : header);
				ImportFieldContract best = null;
				var bestScore = 0;

				foreach (var field in contract.Fields)
				{
					if (used.Contains(field.Key))
						continue;

					var score = Score(normalized, field);
					if (score > bestScore)
					{
						bestScore = score;
						best = field;
					}
				}

				if (best != null && bestScore >= 60)
				{
					used.Add(best.Key);
					map.Columns.Add(Bind(header, best, bestScore));
					if (best.Key == "source_id")
						map.SourceIdentityColumn = header;
				}
				else
				{
					map.Columns.Add(Ignore(header));
				}
			}

			return map;
		}

		private static int Score(string header, ImportFieldContract field)
		{
			if (header.Length == 0)
				return 0;

			var candidates = field.Aliases.Select(ImportFieldConverter.Normalize).Concat(new[] { ImportFieldConverter.Normalize(field.Key.Replace('_', ' ')) })
				.Where(a => a.Length > 0).Distinct().ToList();

			if (candidates.Contains(header))
				return 100;

			// "Member Email Address" contains "email"; a short alias only counts when it is a whole token run of the header.
			var contains = candidates.Where(a => a.Length >= 4 && header.Contains(a)).Select(a => a.Length).DefaultIfEmpty(0).Max();
			if (contains > 0)
				return Math.Min(90, 60 + contains * 2);

			return 0;
		}

		private static bool SheetMentions(string sheetName, ImportEntityContract contract)
		{
			var sheet = ImportFieldConverter.Normalize(sheetName);
			if (sheet.Length == 0)
				return false;
			var key = ImportFieldConverter.Normalize(contract.Key);
			return sheet.Contains(key) || key.Contains(sheet);
		}

		private static bool Same(string a, string b) =>
			!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && ImportFieldConverter.Normalize(a) == ImportFieldConverter.Normalize(b);

		private static ImportColumnBinding Ignore(string header) =>
			new ImportColumnBinding { SourceColumn = header, Kind = (int)ImportColumnBindingKind.Ignore };

		public static ImportColumnBinding Bind(string header, ImportFieldContract field, int confidence) => new ImportColumnBinding
		{
			SourceColumn = header,
			Kind = field.Key == "source_id" ? (int)ImportColumnBindingKind.SourceIdentity
				: field.Type == ImportFieldTypes.Reference || (field.Type == ImportFieldTypes.Multi && field.ItemType == ImportFieldTypes.Reference)
					? (int)ImportColumnBindingKind.Reference : (int)ImportColumnBindingKind.Field,
			FieldKey = field.Key,
			ReferencesEntity = field.References,
			ReferenceMatch = field.ReferenceMatch,
			Confidence = confidence
		};
	}
}
