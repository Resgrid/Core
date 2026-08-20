namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// The shorthand-to-spoken-English conversion library used by
	/// <see cref="TextPreprocessor"/>. Data only — matching mechanics (word
	/// boundaries, ordering, compiled patterns) live in the preprocessor.
	///
	/// Ground rules for adding entries:
	///  - <see cref="Abbreviations"/> and <see cref="DispatchShorthand"/> match
	///    case-sensitively: CAD feeds emit codes in upper case, and lower-cased
	///    tokens collide with ordinary English ("so", "co", "apt", "wit").
	///    Add explicit casing variants (like HAZMAT/HazMat) when needed.
	///  - Many feeds are written ENTIRELY in caps, so an upper-case-only entry can
	///    still collide with a real word in an all-caps sentence ("TO WIT", "SIP OF
	///    WATER"). Only add a token when its caps form overwhelmingly means the code.
	///  - Never add tokens that collide with US state or Canadian province codes —
	///    they appear in addresses: MI, CA, TX, OK, OR, IN, LA, PA, ME, HI, DE, BC.
	///    (That is why myocardial infarction, cardiac arrest and battalion chief are
	///    absent.) Deliberately skipped for ambiguity: RESP (respiratory vs
	///    responding), DIST (disturbance vs district), EXP (explosion vs exposure),
	///    PLS (point last seen vs "please" in relayed texts), LOC stays "Location"
	///    (not loss of consciousness), CP reads as "Chest Pain" (not command post —
	///    nature text outnumbers ICS radio traffic in dispatches).
	///  - Ten-codes keep their numbers ("10-4" is spoken "ten four" — the
	///    preprocessor drops the dash for pacing) because meanings vary by agency;
	///    other numeric codes ("5150", "459") are left entirely as-is.
	///  - Unit designators ("E1", "L14", "K9") are handled by the unit-identifier
	///    regex in the preprocessor, not by these maps.
	///  - Comma-separated single letters ("D, U, I") force paced letter-by-letter
	///    reading for initialisms the engine would otherwise pronounce as a word —
	///    the commas buy a prosodic pause between letters.
	///  - <see cref="SpellOut"/> holds codes that have no safe spoken expansion
	///    (state/province codes and context-dependent initialisms): they read as
	///    spaced letters ("MI" → "M I") so they stay distinct instead of being
	///    mumbled as a word. Only letter-spacing here, never a meaning — spelling
	///    is neutral when the expansion would be ambiguous.
	/// </summary>
	internal static class TtsShorthandCatalog
	{
		/// <summary>
		/// Standard acronyms across dispatch, EMS, fire, police/security, SAR,
		/// emergency management and industrial response. Case-sensitive.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> Abbreviations = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			// Incident descriptors
			{ "SFD",    "Single Family Dwelling" },
			{ "MFD",    "Multi-Family Dwelling" },
			{ "MCI",    "Mass Casualty Incident" },
			{ "MVC",    "Motor Vehicle Collision" },
			{ "MVA",    "Motor Vehicle Accident" },
			{ "TC",     "Traffic Collision" },
			{ "PI",     "Personal Injury" },
			{ "GSW",    "Gunshot Wound" },
			{ "DOA",    "Dead on Arrival" },
			{ "UTL",    "Unable to Locate" },
			{ "GOA",    "Gone on Arrival" },
			{ "ETA",    "Estimated Time of Arrival" },
			{ "FA",     "Fire Alarm" },
			{ "AFA",    "Automatic Fire Alarm" },

			// Medical
			{ "CPR",    "Cardio Pulmonary Resuscitation" },
			{ "AED",    "Automated External Defibrillator" },
			{ "CO",     "Carbon Monoxide" },
			{ "DNR",    "Do Not Resuscitate" },
			{ "CPAP",   "Continuous Positive Airway Pressure" },
			{ "BVM",    "Bag Valve Mask" },
			{ "SOB",    "Shortness of Breath" },
			{ "DIB",    "Difficulty Breathing" },
			{ "AMS",    "Altered Mental Status" },
			{ "ALOC",   "Altered Level of Consciousness" },
			{ "CP",     "Chest Pain" },
			{ "BP",     "Blood Pressure" },
			{ "OD",     "Overdose" },
			{ "ETOH",   "Alcohol Intoxication" },
			{ "CVA",    "Stroke" },
			{ "VFIB",   "Ventricular Fibrillation" },
			{ "NKA",    "No Known Allergies" },
			{ "NKDA",   "No Known Drug Allergies" },
			{ "AMA",    "Against Medical Advice" },
			{ "EDP",    "Emotionally Disturbed Person" },
			{ "OB",     "Obstetric" },
			{ "PEDS",   "Pediatric" },

			// Service types
			{ "ALS",    "Advanced Life Support" },
			{ "BLS",    "Basic Life Support" },
			{ "EMS",    "Emergency Medical Services" },
			{ "ALSEMS", "Advanced Life Support Emergency Medical Services" },

			// Agencies
			{ "HAZMAT", "Hazardous Materials" },
			{ "HazMat", "Hazardous Materials" },
			{ "WMD",    "Weapons of Mass Destruction" },
			{ "CBRN",   "Chemical Biological Radiological Nuclear" },
			{ "PD",     "Police Department" },
			{ "FD",     "Fire Department" },
			{ "SO",     "Sheriff's Office" },
			{ "SAR",    "Search and Rescue" },
			{ "USAR",   "Urban Search and Rescue" },
			{ "ERT",    "Emergency Response Team" },

			// Incident command / emergency management
			{ "IC",     "Incident Command" },
			{ "ICP",    "Incident Command Post" },
			{ "ICS",    "Incident Command System" },
			{ "IAP",    "Incident Action Plan" },
			{ "EOC",    "Emergency Operations Center" },
			{ "PIO",    "Public Information Officer" },
			{ "POV",    "Personally Owned Vehicle" },
			{ "POC",    "Point of Contact" },
			{ "SITREP", "Situation Report" },
			{ "SIP",    "Shelter in Place" },
			{ "EAS",    "Emergency Alert System" },
			{ "NWS",    "National Weather Service" },

			// Firefighting equipment / tactics
			{ "SCBA",   "Self-Contained Breathing Apparatus" },
			{ "PASS",   "Personal Alert Safety System" },
			{ "RIT",    "Rapid Intervention Team" },
			{ "RIC",    "Rapid Intervention Crew" },
			{ "PPE",    "Personal Protective Equipment" },
			{ "PAR",    "Personnel Accountability Report" },
			{ "LZ",     "Landing Zone" },
			{ "FF",     "Firefighter" },

			// Police / security
			{ "BOLO",   "Be On the Lookout" },
			{ "APB",    "All Points Bulletin" },
			{ "DV",     "Domestic Violence" },
			{ "DUI",    "D, U, I" },
			{ "DWI",    "D, W, I" },
			{ "TRO",    "Temporary Restraining Order" },
			{ "POI",    "Person of Interest" },
			{ "CCTV",   "C, C, T, V" },

			// Search and rescue
			{ "LKP",    "Last Known Position" },
			{ "LSW",    "Last Seen Wearing" },
			{ "PLB",    "Personal Locator Beacon" },
			{ "ELT",    "Emergency Locator Transmitter" },
			{ "ATV",    "All Terrain Vehicle" },
			{ "UTV",    "Utility Terrain Vehicle" },
			{ "GPS",    "G, P, S" },

			// Hazmat / industrial
			{ "SDS",    "Safety Data Sheet" },
			{ "MSDS",   "Material Safety Data Sheet" },
			{ "LEL",    "Lower Explosive Limit" },
			{ "UEL",    "Upper Explosive Limit" },
			{ "PPM",    "Parts Per Million" },
			{ "IDLH",   "Immediately Dangerous to Life and Health" },
			{ "LOTO",   "Lockout Tagout" },
			{ "LPG",    "Liquefied Petroleum Gas" },
			{ "LNG",    "Liquefied Natural Gas" },
			{ "NFPA",   "N, F, P, A" },

			// Command / operations
			{ "SOP",    "Standard Operating Procedure" },
			{ "SME",    "Subject Matter Expert" },
			{ "ASAP",   "As Soon As Possible" },

			// Miscellaneous
			{ "FAQ",    "Frequently Asked Questions" },
		};

		/// <summary>
		/// Raw CAD/dispatch feed contractions — the cryptic truncations CAD systems
		/// embed in email/API dispatch output. Case-sensitive.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> DispatchShorthand = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			// Transport & entrapment
			{ "XPORT",  "Transport" },
			{ "ENTRP",  "Entrapment" },

			// Structures
			{ "BLDG",   "Building" },
			{ "APT",    "Apartment" },
			{ "RM",     "Room" },
			{ "STRU",   "Structure" },
			{ "STRUCT", "Structure" },

			// Address references
			{ "ADDR",   "Address" },
			{ "BLK",    "Block" },
			{ "CS",     "Cross Street" },
			{ "LOC",    "Location" },
			{ "HWY",    "Highway" },
			{ "FWY",    "Freeway" },
			{ "XING",   "Crossing" },

			// Patient / person descriptors
			{ "YOM",    "Year Old Male" },
			{ "YOF",    "Year Old Female" },
			{ "PTS",    "Patients" },
			{ "PT",     "Patient" },
			{ "UNC",    "Unconscious" },
			{ "UNCON",  "Unconscious" },
			{ "UNRESP", "Unresponsive" },
			{ "UNK",    "Unknown" },
			{ "INJ",    "Injuries" },
			{ "RP",     "Reporting Party" },
			{ "ABD",    "Abdominal" },
			{ "SZ",     "Seizure" },
			{ "FX",     "Fracture" },
			// LAC collides with Los Angeles County in SoCal feeds; laceration is the
			// overwhelmingly common meaning in nature text.
			{ "LAC",    "Laceration" },
			{ "HX",     "History" },
			{ "PEDI",   "Pediatric" },
			{ "PED",    "Pedestrian" },
			{ "JUV",    "Juvenile" },
			{ "INTOX",  "Intoxicated" },
			{ "SUBJ",   "Subject" },
			{ "SUSP",   "Suspicious" },
			{ "WIT",    "Witness" },
			{ "MISPER", "Missing Person" },

			// Vehicles / apparatus
			{ "VEH",    "Vehicle" },
			{ "VEC",    "Vehicle" },
			{ "AMB",    "Ambulance" },
			{ "ENG",    "Engine" },
			{ "TRK",    "Truck" },
			{ "SQD",    "Squad" },
			{ "HELO",   "Helicopter" },

			// Status / actions
			{ "ENR",    "En Route" },
			{ "ENRT",   "En Route" },
			{ "ADV",    "Advised" },
			{ "NEG",    "Negative" },
			{ "RPT",    "Report" },
			{ "DISP",   "Dispatch" },
			{ "CANC",   "Cancelled" },
			{ "CTC",    "Contact" },
			{ "REQ",    "Request" },
			{ "POSS",   "Possible" },
			{ "AVAIL",  "Available" },
			{ "EVAC",   "Evacuation" },
			{ "DECON",  "Decontamination" },

			// Fire nature codes
			{ "SMK",    "Smoke" },
			{ "INV",    "Investigation" },
			{ "EXPL",   "Explosion" },
			{ "ELEC",   "Electrical" },
			{ "CHIM",   "Chimney" },
			{ "VEG",    "Vegetation" },
			{ "XFMR",   "Transformer" },

			// Police nature codes
			{ "ASLT",   "Assault" },
			{ "WPN",    "Weapon" },
			{ "BURG",   "Burglary" },
			{ "VAND",   "Vandalism" },
			{ "TRESP",  "Trespass" },

			// Ranks
			{ "SGT",    "Sergeant" },
			{ "OFC",    "Officer" },
			{ "CMD",    "Command" },
			{ "OPS",    "Operations" },

			// Organizational
			{ "DEPT",   "Department" },
			{ "STA",    "Station" },
			{ "EMER",   "Emergency" },
			{ "TFC",    "Traffic" },

			// Communications
			{ "PX",     "Phone Extension" },
			{ "ATTN",   "Attention" },
			{ "APPROX", "Approximately" },
			{ "BTWN",   "Between" },
			// All casings of "etc" are safe to expand (no English-word collision),
			// so list each explicitly for the case-sensitive matcher — same pattern
			// as HAZMAT/HazMat in Abbreviations.
			{ "etc",    "et cetera" },
			{ "ETC",    "et cetera" },
			{ "Etc",    "et cetera" },

			// Directional (roadway bounds and compass corners). NE reads as
			// "Northeast", not Nebraska — compass usage dominates dispatch text,
			// and the state codes live in SpellOut instead.
			{ "NB",     "Northbound" },
			{ "SB",     "Southbound" },
			{ "EB",     "Eastbound" },
			{ "WB",     "Westbound" },
			{ "NE",     "Northeast" },
			{ "NW",     "Northwest" },
			{ "SE",     "Southeast" },
			{ "SW",     "Southwest" },

			// Weather
			{ "TSTM",   "Thunderstorm" },

			// Chemical formulas heard in industrial/hazmat alarms
			{ "O2",     "Oxygen" },
			{ "CO2",    "Carbon Dioxide" },
			{ "H2S",    "Hydrogen Sulfide" },
			{ "NH3",    "Ammonia" },
			{ "CL2",    "Chlorine" },
		};

		/// <summary>
		/// Slash- and symbol-delimited notation. Matched case-insensitively with
		/// lookaround boundaries (the tokens contain non-word characters that defeat
		/// \b anchors). Longest key wins, so "W/M" is consumed before "W/".
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> SlashNotation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Y/O", "Year Old" },
			{ "W/O", "Without" },
			{ "W/",  "With" },
			{ "C/O", "Complaining Of" },
			{ "N/V", "Nausea and Vomiting" },
			{ "A/O", "Alert and Oriented" },
			{ "D/T", "Due To" },
			{ "M/A", "Mutual Aid" },
			{ "B&E", "Breaking and Entering" },

			// Person descriptors used by police/security CAD
			{ "W/M", "White Male" },
			{ "W/F", "White Female" },
			{ "B/M", "Black Male" },
			{ "B/F", "Black Female" },
			{ "H/M", "Hispanic Male" },
			{ "H/F", "Hispanic Female" },

			// Directional slash variants
			{ "N/B", "Northbound" },
			{ "S/B", "Southbound" },
			{ "E/B", "Eastbound" },
			{ "W/B", "Westbound" },
		};

		/// <summary>
		/// Codes with no safe spoken expansion, read as spaced letters so the engine
		/// speaks each letter distinctly instead of mumbling them as a word
		/// ("Detroit, MI" → "Detroit, M I"). Case-sensitive, applied after every
		/// expansion map so mapped meanings always win. US state and Canadian
		/// province codes that double as English words in an all-caps feed are
		/// excluded (OH, OK, OR, IN, ME, HI, DE, LA, AL, MA, PA, ON), as are codes
		/// already claimed by an expansion (CO, NB, NE and the other bounds).
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> SpellOut = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			// US states / district
			{ "AK", "A K" }, { "AZ", "A Z" }, { "CA", "C A" }, { "CT", "C T" },
			{ "DC", "D C" }, { "FL", "F L" }, { "GA", "G A" }, { "IA", "I A" },
			{ "ID", "I D" }, { "IL", "I L" }, { "KS", "K S" }, { "KY", "K Y" },
			{ "MD", "M D" }, { "MI", "M I" }, { "MN", "M N" }, { "MO", "M O" },
			{ "MS", "M S" }, { "MT", "M T" }, { "NC", "N C" }, { "ND", "N D" },
			{ "NH", "N H" }, { "NJ", "N J" }, { "NM", "N M" }, { "NV", "N V" },
			{ "NY", "N Y" }, { "RI", "R I" }, { "SC", "S C" }, { "SD", "S D" },
			{ "TN", "T N" }, { "TX", "T X" }, { "UT", "U T" }, { "VA", "V A" },
			{ "VT", "V T" }, { "WA", "W A" }, { "WI", "W I" }, { "WV", "W V" },
			{ "WY", "W Y" },

			// Canadian provinces
			{ "BC", "B C" }, { "AB", "A B" }, { "QC", "Q C" }, { "SK", "S K" },
			{ "MB", "M B" },
		};

		/// <summary>
		/// The subset of <see cref="AddressSuffixes"/> naming a sub-unit rather than a
		/// street. CAD address fields routinely comma-separate these ("123 Main St,
		/// Apt 4"), so their match may bridge a comma; a street suffix may not, or the
		/// house number reaches across the comma into the next clause and rewrites an
		/// unrelated word ("100 Center St, Dr Jones" → "Drive Jones").
		/// </summary>
		public static readonly IReadOnlySet<string> UnitDesignators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Apt", "Ste",
		};

		/// <summary>
		/// Street-suffix abbreviations, expanded only when they follow a house or
		/// building number ("123 Main St" → "123 Main Street"). Case-insensitive.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> AddressSuffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "St",   "Street" },
			{ "Ave",  "Avenue" },
			{ "Blvd", "Boulevard" },
			{ "Apt",  "Apartment" },
			{ "Ste",  "Suite" },
			{ "Rd",   "Road" },
			{ "Dr",   "Drive" },
			{ "Ct",   "Court" },
			{ "Ln",   "Lane" },
			{ "Cir",  "Circle" },
			{ "Pl",   "Place" },
			{ "Pkwy", "Parkway" },
			{ "Hwy",  "Highway" },
			{ "Fwy",  "Freeway" },
			{ "Tpke", "Turnpike" },
			{ "Xing", "Crossing" },
		};
	}
}
