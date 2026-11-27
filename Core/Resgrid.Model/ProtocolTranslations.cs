using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>A translation's place in its own approval (ProtocolVersionTranslations.Status). Persisted: append only.</summary>
	public enum ProtocolTranslationStatuses
	{
		/// <summary>Being written; only authors and approvers see it.</summary>
		Draft = 0,

		/// <summary>Approved at L2 (bound to the translation's own hash); members reading in this language see it.</summary>
		Approved = 1,

		/// <summary>Taken out of use; readers fall back to the source language.</summary>
		Withdrawn = 2
	}

	/// <summary>
	/// A locale variant of one protocol version (P3): the version's blocks with their text translated. The source version's hash and
	/// signatures belong to the source language and never change; a translation carries its own hash (of its own rendered HTML) and its
	/// own L2 approval. It is shown only while it was made from the version's current content (<see cref="SourceContentHash"/>).
	/// </summary>
	[Table("ProtocolVersionTranslations")]
	public class ProtocolVersionTranslation : IEntity
	{
		public const int LocaleMaxLength = 16;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolVersionTranslationId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		public int ProtocolVersionId { get; set; }

		/// <summary>BCP 47 language (e.g. "de", "fr-CA"); unique per version.</summary>
		[MaxLength(LocaleMaxLength)]
		public string Locale { get; set; }

		/// <summary><see cref="ProtocolTranslationStatuses"/>.</summary>
		public int Status { get; set; }

		public string ContentJson { get; set; }

		public string ContentHtml { get; set; }

		/// <summary>SHA-256 of <see cref="ContentHtml"/> (<see cref="ProtocolContentHash"/>): what the translation's approval is bound to.</summary>
		[MaxLength(64)]
		public string ContentHash { get; set; }

		/// <summary>The source version's content hash when this translation was made; a mismatch means it no longer matches its source.</summary>
		[MaxLength(64)]
		public string SourceContentHash { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }
		public string UpdatedByUserId { get; set; }

		public DateTime? ApprovedOn { get; set; }
		public string ApprovedByUserId { get; set; }

		/// <summary>The L2 signature of the approval (SignatureRecords, subject ProtocolTranslation).</summary>
		public int? SignatureRecordId { get; set; }

		public DateTime? WithdrawnOn { get; set; }
		public string WithdrawnByUserId { get; set; }

		/// <summary>Bumped on every write; a save against an older value is refused.</summary>
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsApproved => Status == (int)ProtocolTranslationStatuses.Approved;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolVersionTranslationId; }
			set { ProtocolVersionTranslationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolVersionTranslations";

		[NotMapped]
		public string IdName => "ProtocolVersionTranslationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsApproved" };
	}

	/// <summary>Why a translation step was refused. Each maps to a localized message.</summary>
	public enum ProtocolTranslationErrors
	{
		None = 0,
		NotFound,
		NotAuthorized,
		/// <summary>Only a version out of draft (its content locked) can be translated.</summary>
		SourceNotLocked,
		/// <summary>The language is the source's own, or not a language code.</summary>
		InvalidLocale,
		AlreadyExists,
		StaleCopy,
		/// <summary>An approved or withdrawn translation is not edited; withdraw and start again.</summary>
		NotDraft,
		EmptyContent,
		SignatureRequired,
		PasskeyRequired,
		/// <summary>The source version's content changed since the translation was made.</summary>
		SourceChanged
	}

	public sealed class ProtocolTranslationResult
	{
		public ProtocolTranslationErrors Error { get; set; }
		public ProtocolVersionTranslation Translation { get; set; }
		public SignatureRecord Signature { get; set; }
		public bool Succeeded => Error == ProtocolTranslationErrors.None;

		public static ProtocolTranslationResult Fail(ProtocolTranslationErrors error) => new ProtocolTranslationResult { Error = error };
	}

	/// <summary>What a reader is shown for a version in a language: the approved translation, or the source (the fallback).</summary>
	public sealed class ProtocolLocalizedContent
	{
		public ProtocolVersion Version { get; set; }

		/// <summary>The translation shown; null when the source is shown.</summary>
		public ProtocolVersionTranslation Translation { get; set; }

		/// <summary>The language of what is shown.</summary>
		public string Locale { get; set; }

		public string ContentJson => Translation?.ContentJson ?? Version?.ContentJson;

		public string ContentHtml => Translation?.ContentHtml ?? Version?.ContentHtml;

		/// <summary>True when the reader asked for a language this version has no approved translation in.</summary>
		public bool IsFallback { get; set; }

		/// <summary>The approved languages of the version, for the language picker.</summary>
		public List<string> AvailableLocales { get; set; } = new List<string>();
	}

	/// <summary>
	/// The translation rules (P3): a translation keeps the source's structure. Its blocks are the source's blocks, in the same order, with
	/// the same keys (so anchors and step ids line up) and the same types; only wording is taken from the translator. Medication doses,
	/// concentrations, routes and intervals, table shape, links, attachments, images and scope tags always come from the source, so a
	/// translation can never change a dose or a link.
	/// </summary>
	public static class ProtocolTranslationRules
	{
		public static string NormalizeLocale(string locale)
		{
			if (string.IsNullOrWhiteSpace(locale))
				return null;
			var clean = new string(locale.Trim().Where(c => char.IsLetterOrDigit(c) || c == '-').Take(ProtocolVersionTranslation.LocaleMaxLength).ToArray());
			if (clean.Length < 2 || !char.IsLetter(clean[0]) || !char.IsLetter(clean[1]))
				return null;
			return clean;
		}

		/// <summary>True when two language tags name the same language (their primary subtag).</summary>
		public static bool SameLanguage(string a, string b)
		{
			if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
				return false;
			string Primary(string tag) => tag.Trim().Split('-')[0].ToLowerInvariant();
			return Primary(a) == Primary(b);
		}

		/// <summary>The translation document aligned to its source (see the class summary).</summary>
		public static ProtocolContentDocument Align(ProtocolContentDocument source, ProtocolContentDocument translated)
		{
			source ??= new ProtocolContentDocument();
			if (!source.IsBlockModel)
			{
				// A P1 html version is translated as one html block.
				var html = translated?.IsBlockModel == true ? translated.Render() : translated?.Render();
				return ProtocolContentDocument.FromHtml(html ?? string.Empty);
			}

			var byKey = new Dictionary<string, ProtocolContentBlock>(StringComparer.Ordinal);
			var itemByKey = new Dictionary<string, ProtocolStepItem>(StringComparer.Ordinal);
			var variantByKey = new Dictionary<string, ProtocolVariant>(StringComparer.Ordinal);
			foreach (var (block, _) in ProtocolBlocks.Flatten(translated ?? new ProtocolContentDocument()))
			{
				if (block?.Key != null)
					byKey[block.Key] = block;
				foreach (var item in block?.Items ?? new List<ProtocolStepItem>())
					if (item?.Key != null)
						itemByKey[item.Key] = item;
				foreach (var variant in block?.Variants ?? new List<ProtocolVariant>())
					if (variant?.Key != null)
						variantByKey[variant.Key] = variant;
			}

			var aligned = new ProtocolContentDocument { Schema = ProtocolContentDocument.BlockSchema };
			aligned.Blocks = (source.Blocks ?? new List<ProtocolContentBlock>()).Select(b => AlignBlock(b, byKey, itemByKey, variantByKey)).Where(b => b != null).ToList();
			return ProtocolBlocks.Normalize(aligned);
		}

		private static string Pick(string translated, string source) => string.IsNullOrWhiteSpace(translated) ? source : translated;

		private static ProtocolContentBlock AlignBlock(ProtocolContentBlock source, Dictionary<string, ProtocolContentBlock> byKey,
			Dictionary<string, ProtocolStepItem> items, Dictionary<string, ProtocolVariant> variants)
		{
			if (source == null)
				return null;
			byKey.TryGetValue(source.Key ?? string.Empty, out var t);
			if (t != null && !string.Equals(t.Type, source.Type, StringComparison.Ordinal))
				t = null;

			var block = new ProtocolContentBlock
			{
				Type = source.Type,
				Key = source.Key,
				Level = source.Level,
				HeaderRow = source.HeaderRow,
				Url = source.Url,
				AttachmentId = source.AttachmentId,
				LinkKind = source.LinkKind,
				RefId = source.RefId,
				Scopes = source.Scopes,
				Text = Pick(t?.Text, source.Text),
				Title = Pick(t?.Title, source.Title),
				Html = Pick(t?.Html, source.Html),
				Alt = Pick(t?.Alt, source.Alt),
				Label = Pick(t?.Label, source.Label),
				Contact = Pick(t?.Contact, source.Contact)
			};

			if (source.Items != null)
				block.Items = source.Items.Where(i => i != null).Select(i => new ProtocolStepItem
				{
					Key = i.Key,
					Html = Pick(items.TryGetValue(i.Key ?? string.Empty, out var ti) ? ti.Html : null, i.Html)
				}).ToList();

			if (source.Rows != null)
			{
				// The table's shape is the source's; a translated cell is taken where the translator gave one.
				block.Rows = source.Rows.Select((row, r) => (row ?? new List<string>()).Select((cell, c) =>
				{
					var translatedRows = t?.Rows;
					var translatedCell = translatedRows != null && r < translatedRows.Count && translatedRows[r] != null && c < translatedRows[r].Count ? translatedRows[r][c] : null;
					return Pick(translatedCell, cell);
				}).ToList()).ToList();
			}

			if (source.Medication != null)
			{
				var m = source.Medication;
				block.Medication = new ProtocolMedication
				{
					// Doses, concentration, route and interval are never translated: they are the medical director's numbers and words.
					Drug = m.Drug,
					Concentration = m.Concentration,
					DosePerKg = m.DosePerKg,
					MaxSingleDose = m.MaxSingleDose,
					MaxTotalDose = m.MaxTotalDose,
					Route = m.Route,
					RepeatInterval = m.RepeatInterval,
					InventoryItemId = m.InventoryItemId,
					InventoryItemName = m.InventoryItemName,
					Indication = Pick(t?.Medication?.Indication, m.Indication),
					Notes = Pick(t?.Medication?.Notes, m.Notes)
				};
			}

			if (source.Variants != null)
			{
				block.Variants = source.Variants.Where(v => v != null).Select(v => new ProtocolVariant
				{
					Key = v.Key,
					Label = Pick(variants.TryGetValue(v.Key ?? string.Empty, out var tv) ? tv.Label : null, v.Label),
					Blocks = (v.Blocks ?? new List<ProtocolContentBlock>()).Select(c => AlignBlock(c, byKey, items, variants)).Where(c => c != null).ToList()
				}).ToList();
			}

			return block;
		}
	}
}
