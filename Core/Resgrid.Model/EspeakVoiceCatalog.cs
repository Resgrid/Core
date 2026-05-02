using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	public static class EspeakVoiceCatalog
	{
		private static readonly IReadOnlyList<TtsVoiceOption> VoicesInternal = new List<TtsVoiceOption>
		{
			new TtsVoiceOption("af", "Afrikaans"),
			new TtsVoiceOption("sq", "Albanian"),
			new TtsVoiceOption("am", "Amharic"),
			new TtsVoiceOption("ar", "Arabic"),
			new TtsVoiceOption("an", "Aragonese"),
			new TtsVoiceOption("hy", "Armenian", "East Armenian"),
			new TtsVoiceOption("hyw", "Armenian", "West Armenian"),
			new TtsVoiceOption("as", "Assamese"),
			new TtsVoiceOption("az", "Azerbaijani"),
			new TtsVoiceOption("ba", "Bashkir"),
			new TtsVoiceOption("cu", "Chuvash"),
			new TtsVoiceOption("eu", "Basque"),
			new TtsVoiceOption("be", "Belarusian"),
			new TtsVoiceOption("bn", "Bengali"),
			new TtsVoiceOption("bpy", "Bishnupriya Manipuri"),
			new TtsVoiceOption("bs", "Bosnian"),
			new TtsVoiceOption("bg", "Bulgarian"),
			new TtsVoiceOption("my", "Burmese"),
			new TtsVoiceOption("ca", "Catalan"),
			new TtsVoiceOption("chr", "Cherokee", "Western/C.E.D."),
			new TtsVoiceOption("yue", "Chinese", "Cantonese"),
			new TtsVoiceOption("hak", "Chinese", "Hakka"),
			new TtsVoiceOption("haw", "Hawaiian"),
			new TtsVoiceOption("cmn", "Chinese", "Mandarin"),
			new TtsVoiceOption("hr", "Croatian"),
			new TtsVoiceOption("cs", "Czech"),
			new TtsVoiceOption("da", "Danish"),
			new TtsVoiceOption("nl", "Dutch"),
			new TtsVoiceOption("en-us", "English", "American"),
			new TtsVoiceOption("en-us+f3", "English", "American Female 3"),
			new TtsVoiceOption("en", "English", "British"),
			new TtsVoiceOption("en-029", "English", "Caribbean"),
			new TtsVoiceOption("en-gb-x-gbclan", "English", "Lancastrian"),
			new TtsVoiceOption("en-gb-x-rp", "English", "Received Pronunciation"),
			new TtsVoiceOption("en-gb-scotland", "English", "Scottish"),
			new TtsVoiceOption("en-gb-x-gbcwmd", "English", "West Midlands"),
			new TtsVoiceOption("eo", "Esperanto"),
			new TtsVoiceOption("et", "Estonian"),
			new TtsVoiceOption("fa", "Persian"),
			new TtsVoiceOption("fa-latn", "Persian"),
			new TtsVoiceOption("fi", "Finnish"),
			new TtsVoiceOption("fr-be", "French", "Belgium"),
			new TtsVoiceOption("fr", "French", "France"),
			new TtsVoiceOption("fr-ch", "French", "Switzerland"),
			new TtsVoiceOption("ga", "Gaelic", "Irish"),
			new TtsVoiceOption("gd", "Gaelic", "Scottish"),
			new TtsVoiceOption("ka", "Georgian"),
			new TtsVoiceOption("de", "German"),
			new TtsVoiceOption("grc", "Greek", "Ancient"),
			new TtsVoiceOption("el", "Greek", "Modern"),
			new TtsVoiceOption("kl", "Greenlandic"),
			new TtsVoiceOption("gn", "Guarani"),
			new TtsVoiceOption("gu", "Gujarati"),
			new TtsVoiceOption("ht", "Haitian Creole"),
			new TtsVoiceOption("he", "Hebrew"),
			new TtsVoiceOption("hi", "Hindi"),
			new TtsVoiceOption("hu", "Hungarian"),
			new TtsVoiceOption("is", "Icelandic"),
			new TtsVoiceOption("id", "Indonesian"),
			new TtsVoiceOption("ia", "Interlingua"),
			new TtsVoiceOption("io", "Ido"),
			new TtsVoiceOption("it", "Italian"),
			new TtsVoiceOption("ja", "Japanese"),
			new TtsVoiceOption("kn", "Kannada"),
			new TtsVoiceOption("kok", "Konkani"),
			new TtsVoiceOption("ko", "Korean"),
			new TtsVoiceOption("ku", "Kurdish"),
			new TtsVoiceOption("kk", "Kazakh"),
			new TtsVoiceOption("ky", "Kyrgyz"),
			new TtsVoiceOption("la", "Latin"),
			new TtsVoiceOption("lb", "Luxembourgish"),
			new TtsVoiceOption("ltg", "Latgalian"),
			new TtsVoiceOption("lv", "Latvian"),
			new TtsVoiceOption("lfn", "Lingua Franca Nova"),
			new TtsVoiceOption("lt", "Lithuanian"),
			new TtsVoiceOption("jbo", "Lojban"),
			new TtsVoiceOption("mi", "Māori"),
			new TtsVoiceOption("mk", "Macedonian"),
			new TtsVoiceOption("ms", "Malay"),
			new TtsVoiceOption("ml", "Malayalam"),
			new TtsVoiceOption("mt", "Maltese"),
			new TtsVoiceOption("mr", "Marathi"),
			new TtsVoiceOption("nci", "Nahuatl", "Classical"),
			new TtsVoiceOption("ne", "Nepali"),
			new TtsVoiceOption("nb", "Norwegian Bokmål"),
			new TtsVoiceOption("nog", "Nogai"),
			new TtsVoiceOption("or", "Oriya"),
			new TtsVoiceOption("om", "Oromo"),
			new TtsVoiceOption("pap", "Papiamento"),
			new TtsVoiceOption("py", "Pyash"),
			new TtsVoiceOption("pl", "Polish"),
			new TtsVoiceOption("qdb", "Lang Belta"),
			new TtsVoiceOption("qu", "Quechua"),
			new TtsVoiceOption("quc", "K'iche'"),
			new TtsVoiceOption("qya", "Quenya"),
			new TtsVoiceOption("pt", "Portuguese", "Portugal"),
			new TtsVoiceOption("pt-br", "Portuguese", "Brazil"),
			new TtsVoiceOption("pa", "Punjabi"),
			new TtsVoiceOption("piqd", "Klingon"),
			new TtsVoiceOption("ro", "Romanian"),
			new TtsVoiceOption("ru", "Russian"),
			new TtsVoiceOption("ru-lv", "Russian", "Latvia"),
			new TtsVoiceOption("uk", "Ukrainian"),
			new TtsVoiceOption("sjn", "Sindarin"),
			new TtsVoiceOption("sr", "Serbian"),
			new TtsVoiceOption("tn", "Setswana"),
			new TtsVoiceOption("sd", "Sindhi"),
			new TtsVoiceOption("shn", "Shan (Tai Yai)"),
			new TtsVoiceOption("si", "Sinhala"),
			new TtsVoiceOption("sk", "Slovak"),
			new TtsVoiceOption("sl", "Slovenian"),
			new TtsVoiceOption("smj", "Lule Saami"),
			new TtsVoiceOption("es", "Spanish", "Spain"),
			new TtsVoiceOption("es-419", "Spanish", "Latin American"),
			new TtsVoiceOption("sw", "Swahili"),
			new TtsVoiceOption("sv", "Swedish"),
			new TtsVoiceOption("ta", "Tamil"),
			new TtsVoiceOption("th", "Thai"),
			new TtsVoiceOption("tk", "Turkmen"),
			new TtsVoiceOption("tt", "Tatar"),
			new TtsVoiceOption("te", "Telugu"),
			new TtsVoiceOption("tr", "Turkish"),
			new TtsVoiceOption("ug", "Uyghur"),
			new TtsVoiceOption("ur", "Urdu"),
			new TtsVoiceOption("uz", "Uzbek"),
			new TtsVoiceOption("vi-vn-x-central", "Vietnamese", "Central Vietnam"),
			new TtsVoiceOption("vi", "Vietnamese", "Northern Vietnam"),
			new TtsVoiceOption("vi-vn-x-south", "Vietnamese", "Southern Vietnam"),
			new TtsVoiceOption("cy", "Welsh"),
		};

		private static readonly Dictionary<string, TtsVoiceOption> VoiceLookup = VoicesInternal.ToDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);

		public const string DefaultIdentifier = "en-us+f3";

		public static IReadOnlyList<TtsVoiceOption> Voices => VoicesInternal;

		public static bool TryNormalizeIdentifier(string voice, out string normalizedVoice)
		{
			normalizedVoice = null;

			if (string.IsNullOrWhiteSpace(voice))
				return false;

			if (!VoiceLookup.TryGetValue(voice.Trim(), out var option))
				return false;

			normalizedVoice = option.Identifier;
			return true;
		}

		public static string GetDisplayName(string voice)
		{
			if (TryGetOption(voice, out var option))
				return option.DisplayName;

			return string.IsNullOrWhiteSpace(voice) ? DefaultIdentifier : voice.Trim();
		}

		public static bool TryGetOption(string voice, out TtsVoiceOption option)
		{
			option = null;

			if (string.IsNullOrWhiteSpace(voice))
				return false;

			return VoiceLookup.TryGetValue(voice.Trim(), out option);
		}
	}

	public sealed class TtsVoiceOption
	{
		public TtsVoiceOption(string identifier, string language, string accent = null)
		{
			Identifier = identifier;
			Language = language;
			Accent = accent;
		}

		public string Identifier { get; }
		public string Language { get; }
		public string Accent { get; }
		public string DisplayName => string.IsNullOrWhiteSpace(Accent) ? $"{Language} ({Identifier})" : $"{Language} - {Accent} ({Identifier})";
	}
}
