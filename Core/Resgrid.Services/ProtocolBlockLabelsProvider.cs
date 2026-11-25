using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using Resgrid.Localization;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// The block labels (P3) in a content language, from the Protocols resources: what the renderer writes around the authored text
	/// (Caution, Contact medical control, the medication field names...). The version's HTML carries them in its own language.
	/// </summary>
	public static class ProtocolBlockLabelsProvider
	{
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.Protocols.Protocols", typeof(SupportedLocales).Assembly);
		private static readonly ConcurrentDictionary<string, ProtocolBlockLabels> Cache = new ConcurrentDictionary<string, ProtocolBlockLabels>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The labels for a language tag ("de", "fr-CA"); English for an unknown or unsupported language.</summary>
		public static ProtocolBlockLabels For(string locale)
		{
			var culture = Culture(locale);
			return Cache.GetOrAdd(culture.Name, _ => Build(culture));
		}

		private static readonly ConcurrentDictionary<string, ProtocolGraphLabels> GraphCache = new ConcurrentDictionary<string, ProtocolGraphLabels>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The P4 case flow labels for a language tag; English for an unknown or unsupported language.</summary>
		public static ProtocolGraphLabels GraphFor(string locale)
		{
			var culture = Culture(locale);
			return GraphCache.GetOrAdd(culture.Name, _ =>
			{
				var fallback = ProtocolGraphLabels.Default;
				string L(string key, string english)
				{
					try
					{
						var value = Strings.GetString(key, culture);
						return string.IsNullOrWhiteSpace(value) ? english : value;
					}
					catch (MissingManifestResourceException)
					{
						return english;
					}
				}

				return new ProtocolGraphLabels
				{
					CaseFlow = L("GraphLabelCaseFlow", fallback.CaseFlow),
					EntryQuestion = L("GraphLabelEntryQuestion", fallback.EntryQuestion),
					KeyQuestion = L("GraphLabelKeyQuestion", fallback.KeyQuestion),
					Instruction = L("GraphLabelInstruction", fallback.Instruction),
					Outcome = L("GraphLabelOutcome", fallback.Outcome),
					Determinant = L("GraphLabelDeterminant", fallback.Determinant),
					Priority = L("GraphLabelPriority", fallback.Priority),
					Response = L("GraphLabelResponse", fallback.Response),
					PostDispatch = L("GraphLabelPostDispatch", fallback.PostDispatch),
					GoTo = L("GraphLabelGoTo", fallback.GoTo),
					AnyAnswer = L("GraphLabelAnyAnswer", fallback.AnyAnswer),
					Yes = L("GraphLabelYes", fallback.Yes),
					No = L("GraphLabelNo", fallback.No),
					Start = L("GraphLabelStart", fallback.Start)
				};
			});
		}

		private static CultureInfo Culture(string locale)
		{
			try
			{
				var culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(locale) ? "en" : locale.Trim());
				if (Array.IndexOf(SupportedLocales.GetSupportedCultures(), culture.TwoLetterISOLanguageName) >= 0)
					return CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName);
			}
			catch (CultureNotFoundException)
			{
			}

			return CultureInfo.GetCultureInfo("en");
		}

		private static ProtocolBlockLabels Build(CultureInfo culture)
		{
			var fallback = ProtocolBlockLabels.Default;
			string L(string key, string english)
			{
				try
				{
					var value = Strings.GetString(key, culture);
					return string.IsNullOrWhiteSpace(value) ? english : value;
				}
				catch (MissingManifestResourceException)
				{
					return english;
				}
			}

			return new ProtocolBlockLabels
			{
				Caution = L("BlockLabelCaution", fallback.Caution),
				Warning = L("BlockLabelWarning", fallback.Warning),
				ContactMedicalControl = L("BlockLabelContactMedicalControl", fallback.ContactMedicalControl),
				Contact = L("BlockLabelContact", fallback.Contact),
				ScopeOfPractice = L("BlockLabelScopeOfPractice", fallback.ScopeOfPractice),
				Medication = L("BlockLabelMedication", fallback.Medication),
				Indication = L("BlockLabelIndication", fallback.Indication),
				Concentration = L("BlockLabelConcentration", fallback.Concentration),
				DosePerKg = L("BlockLabelDosePerKg", fallback.DosePerKg),
				MaxSingleDose = L("BlockLabelMaxSingleDose", fallback.MaxSingleDose),
				MaxTotalDose = L("BlockLabelMaxTotalDose", fallback.MaxTotalDose),
				Route = L("BlockLabelRoute", fallback.Route),
				RepeatInterval = L("BlockLabelRepeatInterval", fallback.RepeatInterval),
				InventoryItem = L("BlockLabelInventoryItem", fallback.InventoryItem),
				DoseAsAuthored = L("BlockLabelDoseAsAuthored", fallback.DoseAsAuthored),
				Video = L("BlockLabelVideo", fallback.Video),
				Attachment = L("BlockLabelAttachment", fallback.Attachment),
				LinkProtocol = L("BlockLabelLinkProtocol", fallback.LinkProtocol),
				LinkPoi = L("BlockLabelLinkPoi", fallback.LinkPoi),
				LinkInventory = L("BlockLabelLinkInventory", fallback.LinkInventory),
				LinkChecklist = L("BlockLabelLinkChecklist", fallback.LinkChecklist),
				LinkTraining = L("BlockLabelLinkTraining", fallback.LinkTraining),
				LinkUrl = L("BlockLabelLinkUrl", fallback.LinkUrl)
			};
		}
	}
}
