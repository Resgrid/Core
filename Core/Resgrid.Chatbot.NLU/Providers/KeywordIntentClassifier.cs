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
			// The SMS shortcut numbers (1-4) are the user-facing scheme from the legacy text commands;
			// the emitted actionType values are ActionTypes enum values (Responding=2, NotResponding=1,
			// OnScene=3, StandingBy/Available=0) — the handler passes them straight to SetUserActionAsync.
			(R(@"^(responding|1)$"), "set_status", m => P("actionType", "2")),
			(R(@"^(not\s*responding|2)$"), "set_status", m => P("actionType", "1")),
			(R(@"^(on\s*scene|onscene|3)$"), "set_status", m => P("actionType", "3")),
			(R(@"^(standing\s*by|standingby|4)$"), "set_status", m => P("actionType", "0")),
			// Responder shorthand for "responding" with no target call.
			(R(@"^(omw|on\s+my\s+way|enroute|en\s+route)$"),
				"set_status", m => P("actionType", "2")),
			(R(@"^(i'?m|i\s+am)\s+(responding|on\s*scene|standing\s*by|en\s*route|available|not\s*responding)"),
				"set_status", m => P("actionType", MapStatusWord(m.Groups[2].Value))),
			(R(@"^(set|change|mark)\s+(my\s+)?status\s+to\s+(.+)"),
				"set_status", m => P("statusName", m.Groups[3].Value.Trim())),

			// === Staffing Commands ===
			// S1-S5 is the user-facing scheme; emitted staffingType values are UserStateTypes enum values
			// (Available=0, Delayed=1, Unavailable=2, Committed=3, OnShift=4) — passed straight to CreateUserState.
			(R(@"^(available|s1)$"), "set_staffing", m => P("staffingType", "0")),
			(R(@"^(delayed|s2)$"), "set_staffing", m => P("staffingType", "1")),
			(R(@"^(unavailable|s3)$"), "set_staffing", m => P("staffingType", "2")),
			(R(@"^(committed|s4)$"), "set_staffing", m => P("staffingType", "3")),
			(R(@"^(on\s*shift|onshift|s5)$"), "set_staffing", m => P("staffingType", "4")),
			(R(@"^(i'?m|i\s+am)\s+(available|delayed|unavailable|committed|on\s*shift)"),
				"set_staffing", m => P("staffingType", MapStaffingWord(m.Groups[2].Value))),
			(R(@"^(set|change|mark)\s+(my\s+)?staffing\s+to\s+(.+)"),
				"set_staffing", m => P("staffingName", m.Groups[3].Value.Trim())),

			// === Query Commands (rigid) ===
			(R(@"^calls?$"), "list_calls", null),
			(R(@"^c(\d+)$"), "call_detail", m => P("callId", m.Groups[1].Value)),
			// Call number form ("26-1" / "C26-1"): two-digit year + sequence — resolved by the handler.
			(R(@"^c?(\d{2,4}-\d+)$"), "call_detail", m => P("callRef", m.Groups[1].Value)),
			(R(@"^units?$"), "list_units", null),
			(R(@"^(my\s+)?status$"), "my_status", null),
			// "my staffing" — the my_status handler reports both status and staffing.
			(R(@"^(my\s+)?staffing$"), "my_status", null),
			(R(@"^(messages?|msgs?)$"), "list_messages", null),
			// Unread/new message forms route to the same list handler (it lists unread only).
			(R(@"^(any\s+|my\s+)?(unread|new)\s+(messages?|msgs?)$"), "list_messages", null),
			(R(@"^(calendar|events?|cal)$"), "list_calendar", null),
			(R(@"^shifts?$"), "list_shifts", null),
			(R(@"^(personnel|staff)$"), "personnel_lookup", null),
			(R(@"^weather$"), "weather_alert", null),

			// === Help / Stop ===
			(R(@"^(help|info|commands|menu|what\s+can\s+you\s+do)$"), "help", null),
			// STOP is explicit-only (plus UNSUBSCRIBE, the other unambiguous opt-out word). END/QUIT/CANCEL
			// must NOT trigger the opt-out flow — they carry other meanings in conversation flows.
			(R(@"^(stop|unsubscribe)$"), "stop", null),

			// === Emergency ===
			(R(@"^(mayday|emergency|sos|help\s*me|officer\s*down|ff?\s*down|firefighter\s*down)$"),
				"emergency_mayday", null),

			// === Help topic detail (after Emergency; "me" excluded so "help me!" stays a mayday even
			// when the punctuated original is matched before the stripped copy reaches the pattern above) ===
			(R(@"^(help|info|commands|menu)\s+(?!me\b)(.+)$"),
				"help", m => P("topic", m.Groups[2].Value.Trim())),

			// === Link / Unlink ===
			(R(@"^(link|login|verify|auth)$"), "link_account", null),
			(R(@"^(unlink|logout|unauth)$"), "unlink_account", null),

			// === Message Detail / Delete / Respond (must precede the natural-language message patterns) ===
			(R(@"^#(\d+)$"),
				"message_detail", m => P("messageId", m.Groups[1].Value)),
			(R(@"^(read|show|open|view|get)\s+(message|msg)\s+#?(\d+)"),
				"message_detail", m => P("messageId", m.Groups[3].Value)),
			(R(@"^(delete|remove|del)\s+(message|msg)?\s*#?(\d+)$"),
				"delete_message", m => P("messageId", m.Groups[3].Value)),
			(R(@"^(reply|respond)\s+(yes|no|acknowledge|ack)\s+to\s+(message|msg|#)?\s*#?(\d+)"),
				"respond_to_message", m => P2("response", m.Groups[2].Value, "messageId", m.Groups[4].Value)),

			// === Availability / Call Responder Queries (must precede the generic
			// "who is X" personnel_lookup and "what ... calls" list_calls patterns) ===

			// "who's available?", "who is around?", "anyone free?", "who can respond?"
			(R(@"^(who'?s|who\s+is|who\s+are|anyone|anybody|any\s*one)\s+(around|available|free)(\s+to\s+respond)?$"),
				"who_available", null),
			(R(@"^who\s+can\s+respond$"),
				"who_available", null),

			// "units available?", "available units", "what units are available/free/in service"
			(R(@"^(available|free)\s+units?$"),
				"units_available", null),
			(R(@"^units?\s+(are\s+)?(available|free|in\s+service)$"),
				"units_available", null),
			(R(@"^(what|which)\s+units?\s+(are\s+)?(available|free|in\s+service)$"),
				"units_available", null),

			// "who's on scene at the fire" — on-scene responders for a call.
			(R(@"^(who'?s|who\s+is|who\s+are)\s+on\s*scene(\s+(?:at|on|for)\s+(.+))?$"),
				"call_responders", m => P2("mode", "onscene", "callRef", m.Groups[3].Value.Trim())),

			// "who's in route to the fire", "who is responding to c1445", "who's coming"
			(R(@"^(who'?s|who\s+is|who\s+are)\s+((?:in|en)\s*route|responding|headed|heading|going|coming)(\s+(?:to|for)\s+(.+))?$"),
				"call_responders", m => P2("mode", "enroute", "callRef", m.Groups[4].Value.Trim())),

			// "who got dispatched to the medical", "who's dispatched to 26-1" — the full dispatch
			// list (personnel, groups, roles and units) rather than live statuses.
			(R(@"^(who'?s|who\s+is|who\s+are|who\s+got|who\s+was|who\s+were|who)\s+dispatched(\s+(?:to|on|for)\s+(.+))?$"),
				"call_dispatched", m => P("callRef", m.Groups[3].Value.Trim())),

			// "who's on call 26-1", "who is on the fire" — responding + on-scene for a call.
			(R(@"^(who'?s|who\s+is|who\s+are)\s+on(\s+call)?(\s+(.+))?$"),
				"call_responders", m => P2("mode", "all", "callRef", m.Groups[4].Value.Trim())),

			// "what calls am I on?", "my calls" — calls the user was dispatched to.
			(R(@"^(what\s+)?calls?\s+am\s+i\s+(on|dispatched\s+to|assigned\s+to)\b.*$"),
				"my_calls", null),
			(R(@"^my\s+calls?$"),
				"my_calls", null),
			(R(@"^what\s+am\s+i\s+dispatched\s+to$"),
				"my_calls", null),

			// "what calls is Rescue 6 on?" — calls a unit was dispatched to.
			(R(@"^(what\s+)?calls?\s+(is|are)\s+(.+?)\s+(on|dispatched\s+to|assigned\s+to)$"),
				"unit_calls", m => P("unitName", m.Groups[3].Value.Trim())),
			(R(@"^what\s+is\s+(.+?)\s+dispatched\s+to$"),
				"unit_calls", m => P("unitName", m.Groups[1].Value.Trim())),

			// "what's my schedule?", "my schedule for 7/22" — shifts + RSVP'd events.
			(R(@"^(what'?s\s+|what\s+is\s+)?my\s+schedule(\s+(?:for\s+|on\s+)?(.+))?$"),
				"my_schedule", m => P("day", m.Groups[3].Value.Trim())),

			// "poll members to see who's available for a red flag on 7/22" — the handler strips
			// leading audience/verb filler from the question text.
			(R(@"^(send\s+a\s+poll|send\s+poll|poll)\s+(.+)$"),
				"create_poll", m => P("question", m.Groups[2].Value.Trim())),

			// === Natural Language Query Commands ===
			(R(@"^(show|list|get|what)\s+(are\s+)?(active|open)?\s*(calls|incidents)"),
				"list_calls", null),
			// Hyphenated call-number references ("what about c26-1") must match before the plain
			// numeric form below — its \b sits at the hyphen and would bind just "c26" (the wrong call).
			(R(@"^(show|tell|get|details?|what\s+about).*\bc?(\d{2,4}-\d+)\b"),
				"call_detail", m => P("callRef", m.Groups[2].Value)),
			(R(@"^(show|tell|get|details?|what\s+about).*\bc(\d+)\b"),
				"call_detail", m => P("callId", m.Groups[m.Groups.Count - 1].Value)),
			(R(@"^(show|list|get|what)\s+(are\s+)?(units?|apparatus|rigs?)"),
				"list_units", null),
			(R(@"^(who|where)\s+(is|are)\s+(.+)"),
				"personnel_lookup", m => P("query", m.Groups[3].Value.Trim())),
			(R(@"^(show|list|get)\s+(personnel|staff|members|crew)"),
				"personnel_lookup", null),
			(R(@"^(what'?s|what\s+is)\s+(my\s+)?(status|staffing)"),
				"my_status", null),
			(R(@"^(check|read|show)\s+(my\s+)?(messages?|inbox)"),
				"list_messages", null),
			(R(@"^(show|list|get|what'?s)\s+(on\s+)?(the\s+)?(calendar|schedule|agenda)"),
				"list_calendar", null),
			(R(@"^(show|list|get|my)\s+shifts?"),
				"list_shifts", null),
			(R(@"^(weather\s+)?(alerts?|warnings?)"),
				"weather_alert", null),

			// === Send Message ===
			(R(@"^send\s+message\s+to\s+(.+?):?\s+(.+)"),
				"send_message", m => P2("recipient", m.Groups[1].Value.Trim(), "body", m.Groups[2].Value.Trim())),
			(R(@"^(msg|message)\s+to\s+(.+?):?\s+(.+)"),
				"send_message", m => P2("recipient", m.Groups[2].Value.Trim(), "body", m.Groups[3].Value.Trim())),
			(R(@"^tell\s+(.+?)\s+(.+)"),
				"send_message", m => P2("recipient", m.Groups[1].Value.Trim(), "body", m.Groups[2].Value.Trim())),

			// === Dispatch ===
			(R(@"^(dispatch|create\s+call|new\s+call)\s+(.+)"),
				"dispatch_call", m => P("description", m.Groups[2].Value.Trim())),
			(R(@"^report\s+(.+)"),
				"dispatch_call", m => P("description", m.Groups[1].Value.Trim())),

			// === Close Call ===
			(R(@"^(close|end|cancel)\s+call\s+c?(\d+)"),
				"close_call", m => P("callId", m.Groups[2].Value)),
			(R(@"^(close|end|cancel)\s+c(\d+)"),
				"close_call", m => P("callId", m.Groups[2].Value)),

			// === Respond to Call ===
			(R(@"^(respond|en\s*route|going)\s+to\s+c?(\d+)$"),
				"respond_to_call", m => P("callId", m.Groups[2].Value)),
			// Responder shorthand: "omw to 26-1", "omw to fire", "enroute to c1445", "headed to Main St".
			// The reference can be a call id, a call number (yy-N), or a term matched against active
			// calls — resolved by the handler.
			(R(@"^(omw|on\s+my\s+way|respond(?:ing)?|going|headed|enroute|en\s+route)\s+(?:to\s+)?(.+)$"),
				"respond_to_call", m => P("callRef", m.Groups[2].Value.Trim())),

			// === Shift Drop (must precede shift signup/detail so 'drop shift 5' isn't misread) ===
			(R(@"^(drop|cancel|release)\s+(my\s+)?shift\s+#?(\d+)"),
				"shift_drop", m => P("shiftId", m.Groups[3].Value)),

			// === Shift Signup ===
			(R(@"^(sign\s*up|take)\s+shift\s+(.+)"),
				"shift_signup", m => P("shiftId", m.Groups[2].Value.Trim())),

			// === RSVP Calendar ===
			(R(@"^rsvp\s+(yes|no|maybe)\s+to\s+(.+)"),
				"rsvp_calendar", m => P2("response", m.Groups[1].Value, "eventId", m.Groups[2].Value.Trim())),

			// === Calendar / Shift Detail (query suffix) ===
			(R(@"^(calendar|events?)\s+(.+)$"),
				"calendar_detail", m => P("query", m.Groups[2].Value.Trim())),
			(R(@"^shifts?\s+(.+)$"),
				"shift_detail", m => P("query", m.Groups[1].Value.Trim())),

			// === Set Unit Status ===
			(R(@"^set\s+unit\s+(.+?)\s+to\s+(.+)"),
				"set_unit_status", m => P2("unitName", m.Groups[1].Value.Trim(), "status", m.Groups[2].Value.Trim())),

			// === Department Management ===
			(R(@"^(departments|depts|my\s+departments|my\s+depts|which\s+departments)$"),
				"list_departments", null),
			(R(@"^(show|list|get|what|what'?s)\s+(my\s+)?(departments?|depts?)$"),
				"list_departments", null),
			(R(@"^(active\s+department|current\s+department|which\s+department|what\s+department)\s*(am\s+i\s+in)?\??$"),
				"get_active_department", null),
			(R(@"^(switch|change|set)\s+(to\s+)?(department|dept)\s+(.+)$"),
				"switch_department", m => P("departmentIdentifier", m.Groups[4].Value.Trim())),
			(R(@"^(switch|change|set)\s+(my\s+)?(active\s+)?(department|dept)\s*$"),
				"list_departments", null),

			// SMS-style short forms — parity with the legacy SWITCH text command ("SWITCH" lists the
			// options, "SWITCH <number or name>" switches). Placed after the wordier forms above so
			// "switch department X" keeps binding the identifier without the "department" word in it.
			(R(@"^switch$"),
				"list_departments", null),
			(R(@"^switch\s+(to\s+)?(.+)$"),
				"switch_department", m => P("departmentIdentifier", m.Groups[2].Value.Trim())),
		};

		public Task<NLUResult> ClassifyAsync(string text, string context = null, int departmentId = 0)
		{
			if (string.IsNullOrWhiteSpace(text))
				return Task.FromResult(new NLUResult { IntentName = "unknown", Confidence = 0, ProviderName = ProviderName });

			var trimmed = text.Trim();

			// Texters end questions/commands with punctuation ("My status?", "omw to 26-1."). The
			// patterns are anchored, so a trailing-punctuation-stripped copy is also tried — but only as
			// a FALLBACK: the original input goes first so free-form parameters (message bodies, dispatch
			// descriptions) are extracted with their punctuation intact.
			var normalized = trimmed.TrimEnd('?', '!', '.', ',', ' ');
			var candidates = normalized.Length > 0 && !string.Equals(normalized, trimmed, StringComparison.Ordinal)
				? new[] { trimmed, normalized }
				: new[] { trimmed };

			// Check all patterns in priority order
			foreach (var candidate in candidates)
			{
				foreach (var (pattern, intent, extractor) in _patterns)
				{
					var match = pattern.Match(candidate);
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

		// All patterns run against untrusted inbound SMS text, so every one gets a match timeout to
		// bound pathological backtracking (same convention as WebhookUrlValidator/UdfValidationHelper).
		// The timeout is inlined rather than a static field: _patterns is declared above and static
		// field initializers run in declaration order, so a field here would still be zero when the
		// pattern list is built.
		private static Regex R(string pattern)
		{
			return new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
		}

		private static Dictionary<string, string> P(string key, string value)
		{
			return new Dictionary<string, string> { [key] = value };
		}

		private static Dictionary<string, string> P2(string k1, string v1, string k2, string v2)
		{
			return new Dictionary<string, string> { [k1] = v1, [k2] = v2 };
		}

		// ActionTypes enum values: Responding=2, NotResponding=1, OnScene=3, StandingBy/Available=0.
		private static string MapStatusWord(string word)
		{
			var w = word.ToLowerInvariant().Replace(" ", "");
			return w switch
			{
				"responding" => "2",
				"notresponding" => "1",
				"onscene" => "3",
				"standingby" => "0",
				"enroute" => "2",
				"available" => "0",
				_ => "2"
			};
		}

		// UserStateTypes enum values: Available=0, Delayed=1, Unavailable=2, Committed=3, OnShift=4.
		private static string MapStaffingWord(string word)
		{
			var w = word.ToLowerInvariant().Replace(" ", "");
			return w switch
			{
				"available" => "0",
				"delayed" => "1",
				"unavailable" => "2",
				"committed" => "3",
				"onshift" => "4",
				_ => "0"
			};
		}
	}
}
