using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.NLU.Providers
{
	public class KeywordIntentClassifier : INLUProvider
	{
		public string ProviderName => "Keyword";
		public int Priority => 0;

		private static readonly List<(Regex pattern, string intent, Func<Match, Dictionary<string, string>> extractParams)> _patterns = new()
		{
			// === Status Commands (rigid + natural language) ===
			(new Regex(@"^(responding|1)$", RegexOptions.IgnoreCase), "set_status", m => P("actionType", "1")),
			(new Regex(@"^(not\s*responding|2)$", RegexOptions.IgnoreCase), "set_status", m => P("actionType", "2")),
			(new Regex(@"^(on\s*scene|onscene|3)$", RegexOptions.IgnoreCase), "set_status", m => P("actionType", "3")),
			(new Regex(@"^(standing\s*by|standingby|4)$", RegexOptions.IgnoreCase), "set_status", m => P("actionType", "4")),
			(new Regex(@"^(i'?m|i\s+am)\s+(responding|on\s*scene|standing\s*by|en\s*route|available|not\s*responding)", RegexOptions.IgnoreCase),
				"set_status", m => P("actionType", MapStatusWord(m.Groups[2].Value))),
			(new Regex(@"^(set|change|mark)\s+(my\s+)?status\s+to\s+(.+)", RegexOptions.IgnoreCase),
				"set_status", m => P("statusName", m.Groups[3].Value.Trim())),

			// === Staffing Commands ===
			(new Regex(@"^(available|s1)$", RegexOptions.IgnoreCase), "set_staffing", m => P("staffingType", "1")),
			(new Regex(@"^(delayed|s2)$", RegexOptions.IgnoreCase), "set_staffing", m => P("staffingType", "2")),
			(new Regex(@"^(unavailable|s3)$", RegexOptions.IgnoreCase), "set_staffing", m => P("staffingType", "3")),
			(new Regex(@"^(committed|s4)$", RegexOptions.IgnoreCase), "set_staffing", m => P("staffingType", "4")),
			(new Regex(@"^(on\s*shift|onshift|s5)$", RegexOptions.IgnoreCase), "set_staffing", m => P("staffingType", "5")),
			(new Regex(@"^(i'?m|i\s+am)\s+(available|delayed|unavailable|committed|on\s*shift)", RegexOptions.IgnoreCase),
				"set_staffing", m => P("staffingType", MapStaffingWord(m.Groups[2].Value))),
			(new Regex(@"^(set|change|mark)\s+(my\s+)?staffing\s+to\s+(.+)", RegexOptions.IgnoreCase),
				"set_staffing", m => P("staffingName", m.Groups[3].Value.Trim())),

			// === Query Commands (rigid) ===
			(new Regex(@"^calls?$", RegexOptions.IgnoreCase), "list_calls", null),
			(new Regex(@"^c(\d+)$", RegexOptions.IgnoreCase), "call_detail", m => P("callId", m.Groups[1].Value)),
			(new Regex(@"^units?$", RegexOptions.IgnoreCase), "list_units", null),
			(new Regex(@"^(my\s+)?status$", RegexOptions.IgnoreCase), "my_status", null),
			(new Regex(@"^messages?$", RegexOptions.IgnoreCase), "list_messages", null),
			(new Regex(@"^(calendar|events?)$", RegexOptions.IgnoreCase), "list_calendar", null),
			(new Regex(@"^shifts?$", RegexOptions.IgnoreCase), "list_shifts", null),
			(new Regex(@"^(personnel|staff)$", RegexOptions.IgnoreCase), "personnel_lookup", null),
			(new Regex(@"^weather$", RegexOptions.IgnoreCase), "weather_alert", null),

			// === Help / Stop ===
			(new Regex(@"^(help|info|commands|menu|what\s+can\s+you\s+do)$", RegexOptions.IgnoreCase), "help", null),
			(new Regex(@"^(stop|end|quit|cancel|unsubscribe)$", RegexOptions.IgnoreCase), "stop", null),

			// === Emergency ===
			(new Regex(@"^(mayday|emergency|sos|help\s*me|officer\s*down|ff?\s*down|firefighter\s*down)$", RegexOptions.IgnoreCase),
				"emergency_mayday", null),

			// === Link / Unlink ===
			(new Regex(@"^(link|login|verify|auth)$", RegexOptions.IgnoreCase), "link_account", null),
			(new Regex(@"^(unlink|logout|unauth)$", RegexOptions.IgnoreCase), "unlink_account", null),

			// === Natural Language Query Commands ===
			(new Regex(@"^(show|list|get|what)\s+(are\s+)?(active|open)?\s*(calls|incidents)", RegexOptions.IgnoreCase),
				"list_calls", null),
			(new Regex(@"^(show|tell|get|details?|what\s+about).*\bc(\d+)\b", RegexOptions.IgnoreCase),
				"call_detail", m => P("callId", m.Groups[m.Groups.Count - 1].Value)),
			(new Regex(@"^(show|list|get|what)\s+(are\s+)?(units?|apparatus|rigs?)", RegexOptions.IgnoreCase),
				"list_units", null),
			(new Regex(@"^(who|where)\s+(is|are)\s+(.+)", RegexOptions.IgnoreCase),
				"personnel_lookup", m => P("query", m.Groups[3].Value.Trim())),
			(new Regex(@"^(show|list|get)\s+(personnel|staff|members|crew)", RegexOptions.IgnoreCase),
				"personnel_lookup", null),
			(new Regex(@"^(what'?s|what\s+is)\s+(my\s+)?(status|staffing)", RegexOptions.IgnoreCase),
				"my_status", null),
			(new Regex(@"^(check|read|show)\s+(my\s+)?(messages?|inbox)", RegexOptions.IgnoreCase),
				"list_messages", null),
			(new Regex(@"^(show|list|get|what'?s)\s+(on\s+)?(the\s+)?(calendar|schedule|agenda)", RegexOptions.IgnoreCase),
				"list_calendar", null),
			(new Regex(@"^(show|list|get|my)\s+shifts?", RegexOptions.IgnoreCase),
				"list_shifts", null),
			(new Regex(@"^(weather\s+)?(alerts?|warnings?)", RegexOptions.IgnoreCase),
				"weather_alert", null),

			// === Send Message ===
			(new Regex(@"^send\s+message\s+to\s+(.+?):?\s+(.+)", RegexOptions.IgnoreCase),
				"send_message", m => P2("recipient", m.Groups[1].Value.Trim(), "body", m.Groups[2].Value.Trim())),
			(new Regex(@"^(msg|message)\s+to\s+(.+?):?\s+(.+)", RegexOptions.IgnoreCase),
				"send_message", m => P2("recipient", m.Groups[2].Value.Trim(), "body", m.Groups[3].Value.Trim())),
			(new Regex(@"^tell\s+(.+?)\s+(.+)", RegexOptions.IgnoreCase),
				"send_message", m => P2("recipient", m.Groups[1].Value.Trim(), "body", m.Groups[2].Value.Trim())),

			// === Dispatch ===
			(new Regex(@"^(dispatch|create\s+call|new\s+call)\s+(.+)", RegexOptions.IgnoreCase),
				"dispatch_call", m => P("description", m.Groups[2].Value.Trim())),
			(new Regex(@"^report\s+(.+)", RegexOptions.IgnoreCase),
				"dispatch_call", m => P("description", m.Groups[1].Value.Trim())),

			// === Close Call ===
			(new Regex(@"^(close|end|cancel)\s+call\s+c?(\d+)", RegexOptions.IgnoreCase),
				"close_call", m => P("callId", m.Groups[2].Value)),
			(new Regex(@"^(close|end|cancel)\s+c(\d+)", RegexOptions.IgnoreCase),
				"close_call", m => P("callId", m.Groups[2].Value)),

			// === Respond to Call ===
			(new Regex(@"^(respond|en\s*route|going)\s+to\s+c?(\d+)", RegexOptions.IgnoreCase),
				"respond_to_call", m => P("callId", m.Groups[2].Value)),

			// === Shift Signup ===
			(new Regex(@"^(sign\s*up|take)\s+shift\s+(.+)", RegexOptions.IgnoreCase),
				"shift_signup", m => P("shiftId", m.Groups[2].Value.Trim())),

			// === RSVP Calendar ===
			(new Regex(@"^rsvp\s+(yes|no|maybe)\s+to\s+(.+)", RegexOptions.IgnoreCase),
				"rsvp_calendar", m => P2("response", m.Groups[1].Value, "eventId", m.Groups[2].Value.Trim())),

			// === Calendar / Shift Detail (query suffix) ===
			(new Regex(@"^(calendar|events?)\s+(.+)$", RegexOptions.IgnoreCase),
				"calendar_detail", m => P("query", m.Groups[2].Value.Trim())),
			(new Regex(@"^shifts?\s+(.+)$", RegexOptions.IgnoreCase),
				"shift_detail", m => P("query", m.Groups[1].Value.Trim())),

			// === Set Unit Status ===
			(new Regex(@"^set\s+unit\s+(.+?)\s+to\s+(.+)", RegexOptions.IgnoreCase),
				"set_unit_status", m => P2("unitName", m.Groups[1].Value.Trim(), "status", m.Groups[2].Value.Trim())),

			// === Department Management ===
			(new Regex(@"^(departments|depts|my\s+departments|my\s+depts|which\s+departments)$", RegexOptions.IgnoreCase),
				"list_departments", null),
			(new Regex(@"^(show|list|get|what|what'?s)\s+(my\s+)?(departments?|depts?)$", RegexOptions.IgnoreCase),
				"list_departments", null),
			(new Regex(@"^(active\s+department|current\s+department|which\s+department|what\s+department)\s*(am\s+i\s+in)?\??$", RegexOptions.IgnoreCase),
				"get_active_department", null),
			(new Regex(@"^(switch|change|set)\s+(to\s+)?(department|dept)\s+(.+)$", RegexOptions.IgnoreCase),
				"switch_department", m => P("departmentIdentifier", m.Groups[4].Value.Trim())),
			(new Regex(@"^(switch|change|set)\s+(my\s+)?(active\s+)?(department|dept)\s*$", RegexOptions.IgnoreCase),
				"list_departments", null),
		};

		public Task<NLUResult> ClassifyAsync(string text, string context = null, int departmentId = 0)
		{
			if (string.IsNullOrWhiteSpace(text))
				return Task.FromResult(new NLUResult { IntentName = "unknown", Confidence = 0, ProviderName = ProviderName });

			var trimmed = text.Trim();

			// Check all patterns in priority order
			foreach (var (pattern, intent, extractor) in _patterns)
			{
				var match = pattern.Match(trimmed);
				if (match.Success)
				{
					return Task.FromResult(new NLUResult
					{
						IntentName = intent,
						Parameters = extractor?.Invoke(match) ?? new Dictionary<string, string>(),
						Confidence = 1.0,
						ProviderName = ProviderName
					});
				}
			}

			// Fuzzy fallback: check partial keyword matches for common intents
			var lower = trimmed.ToLowerInvariant();
			if (lower.Contains("call") && (lower.Contains("active") || lower.Contains("open") || lower.Contains("list")))
				return Task.FromResult(new NLUResult { IntentName = "list_calls", Parameters = new Dictionary<string, string>(), Confidence = 0.7, ProviderName = ProviderName });

			if (lower.Contains("message") && (lower.Contains("send") || lower.Contains("tell")))
				return Task.FromResult(new NLUResult { IntentName = "send_message", Parameters = new Dictionary<string, string> { ["body"] = trimmed }, Confidence = 0.6, ProviderName = ProviderName });

			if (lower.Contains("status") && (lower.Contains("my") || lower.Contains("what")))
				return Task.FromResult(new NLUResult { IntentName = "my_status", Confidence = 0.6, ProviderName = ProviderName });

			if (lower.Contains("shift"))
				return Task.FromResult(new NLUResult { IntentName = "list_shifts", Confidence = 0.5, ProviderName = ProviderName });

			if (lower.Contains("who") || lower.Contains("where"))
				return Task.FromResult(new NLUResult { IntentName = "personnel_lookup", Parameters = new Dictionary<string, string> { ["query"] = trimmed }, Confidence = 0.5, ProviderName = ProviderName });

			return Task.FromResult(new NLUResult
			{
				IntentName = "unknown",
				Confidence = 0,
				ProviderName = ProviderName
			});
		}

		public Task<bool> IsAvailableAsync()
		{
			return Task.FromResult(true);
		}

		private static Dictionary<string, string> P(string key, string value)
		{
			return new Dictionary<string, string> { [key] = value };
		}

		private static Dictionary<string, string> P2(string k1, string v1, string k2, string v2)
		{
			return new Dictionary<string, string> { [k1] = v1, [k2] = v2 };
		}

		private static string MapStatusWord(string word)
		{
			var w = word.ToLowerInvariant().Replace(" ", "");
			return w switch
			{
				"responding" => "1",
				"notresponding" => "2",
				"onscene" => "3",
				"standingby" => "4",
				"enroute" => "1",
				"available" => "4",
				_ => "1"
			};
		}

		private static string MapStaffingWord(string word)
		{
			var w = word.ToLowerInvariant().Replace(" ", "");
			return w switch
			{
				"available" => "1",
				"delayed" => "2",
				"unavailable" => "3",
				"committed" => "4",
				"onshift" => "5",
				_ => "1"
			};
		}
	}
}
