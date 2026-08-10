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

		/// <summary>
		/// Capturing alternation of ICS position vocabulary (NIMS command/general staff plus the
		/// fireground/EMS/HazMat positions Resgrid models in <c>IncidentRoleType</c>, and the
		/// non-modeled-but-always-asked RIT/RIC). Used by the incident_roles patterns so a role lookup
		/// only fires on an actual position name — "who is Smith" must stay a personnel lookup.
		/// A <c>const</c> (not a static field) so it is available to the <c>_patterns</c> initializer.
		/// </summary>
		private const string RoleWords =
			@"(ic|incident\s+commander|deputy(\s+incident)?(\s+commander)?|commander|unified\s+command|safety(\s+officer)?|" +
			@"ops(\s+chief)?|operations(\s+section)?(\s+chief)?|planning(\s+section)?(\s+chief)?|logistics(\s+section)?(\s+chief)?|" +
			@"finance(\s+admin)?(\s+section)?(\s+chief)?|pio|public\s+information\s+officer|liaison(\s+officer)?|" +
			@"staging(\s+area)?\s+manager|resources?\s+unit\s+leader|situation\s+unit\s+leader|documentation\s+unit\s+leader|" +
			@"communications\s+unit\s+leader|division\s+supervisor|group\s+supervisor|branch\s+director|" +
			@"strike\s+team\s+leader|task\s+force\s+leader|medical\s+unit\s+leader|rehab(\s+officer)?|medical\s+branch\s+director|" +
			@"triage(\s+officer)?|treatment(\s+officer)?|transport(\s+officer)?|hazmat\s+group\s+supervisor|decon(\s+officer)?|" +
			@"entry\s+team\s+leader|search\s+group\s+supervisor|air\s+operations(\s+branch)?(\s+director)?|" +
			@"shelter(\s+mass\s+care)?\s+coordinator|mass\s+care\s+coordinator|damage\s+assessment\s+lead|" +
			@"rit|ric|rapid\s+intervention(\s+team|\s+crew)?|accountability\s+officer)";

		private static readonly List<(Regex pattern, string intent, Func<Match, Dictionary<string, string>> extractParams)> _patterns = new()
		{
			// === Status Commands (rigid + natural language) ===
			// The SMS shortcut numbers (1-4) are the user-facing scheme from the legacy text commands;
			// the emitted actionType values are ActionTypes enum values (Responding=2, NotResponding=1,
			// OnScene=3, StandingBy/Available=0). The handler maps them through department custom states.
			// Bare response phrases acknowledge the user's most recent call dispatch. Explicit status
			// commands ("set my status to ...") continue through the general status handler.
			(R(@"^(responding|omw|on\s+my\s+way|enroute|en\s+route)$"),
				"respond_to_call", m => P("response", "yes")),
			(R(@"^(not\s*responding|not\s+going|unable\s+to\s+respond)$"),
				"respond_to_call", m => P("response", "no")),
			(R(@"^1$"), "set_status", m => P("actionType", "2")),
			(R(@"^2$"), "set_status", m => P("actionType", "1")),
			(R(@"^(on\s*scene|onscene|3)$"), "set_status", m => P("actionType", "3")),
			(R(@"^(standing\s*by|standingby|4)$"), "set_status", m => P("actionType", "0")),
			(R(@"^(i'?m|i\s+am)\s+(responding|on\s+my\s+way|en\s*route|not\s*responding|not\s+going)$"),
				"respond_to_call", m => P("response", IsNegativeCallResponse(m.Groups[2].Value) ? "no" : "yes")),
			(R(@"^(i'?m|i\s+am)\s+(on\s*scene|standing\s*by|available)"),
				"set_status", m => P("actionType", MapStatusWord(m.Groups[2].Value))),
			(R(@"^(set|change|mark)\s+(my\s+)?status\s+to\s+(.+)"),
				"set_status", m => P("statusName", m.Groups[3].Value.Trim())),

			// === Staffing Commands ===
			// S1-S5 is the user-facing scheme; emitted staffingType values are UserStateTypes enum values
			// (Available=0, Delayed=1, Unavailable=2, Committed=3, OnShift=4). The handler maps them
			// through the department's configured staffing levels.
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
			(R(@"^(my\s+)?(current\s+)?status$"), "my_status", null),
			// Staffing queries use the my_status handler because it reports both status and staffing.
			(R(@"^(my\s+)?(current\s+)?staffing$"), "my_status", null),
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

			// === Incident Command (ICS) board questions ===
			// Placed BEFORE the responder/personnel/unit query blocks: phrasings like "what units do I
			// have on scene" or "who is working Division A" would otherwise bind the department-wide
			// list_units / call_responders patterns instead of the incident board. Every pattern here
			// is deliberately narrow (it names an ICS concept) so general queries keep their meaning.
			// A trailing "for <ref>" scopes the answer to a specific call; without one the handler
			// falls back to the incident context the client sent, then to the user's only active command.

			// PAR / accountability — the single most-asked question on a working incident.
			(R(@"^(par|par\s+check|accountability|accountability\s+check|personnel\s+accountability(\s+report)?)(\s+(?:for|on)\s+(.+))?$"),
				"incident_par", m => P("callRef", CleanReference(m.Groups[4].Value))),
			(R(@"^(give|get|run|do)\s+(me\s+)?(a\s+|the\s+)?par(\s+check)?(\s+(?:for|on)\s+(.+))?$"),
				"incident_par", m => P("callRef", CleanReference(m.Groups[6].Value))),
			(R(@"^(who'?s|who\s+is|who\s+are|anyone)\s+(overdue|unaccounted(\s+for)?|not\s+accounted\s+for|missing)(\s+.*)?$"),
				"incident_par", null),

			// Span of control — must precede the generic resources patterns ("which lanes are over...").
			(R(@"^span(\s+of\s+control)?(\s+check)?$"), "incident_span_of_control", null),
			(R(@"^(what|which)\s+(lanes?|divisions?|groups?|branches|sectors?)\s+(are\s+)?(over|under)\s*-?\s*(staffed|filled|loaded|manned|resourced)?$"),
				"incident_span_of_control", null),
			(R(@"^(am\s+i|are\s+we)\s+(over|under)\s*-?\s*(staffed|filled|loaded|manned|resourced)$"),
				"incident_span_of_control", null),

			// Resources on the incident, optionally scoped to one lane.
			(R(@"^(who'?s|who\s+is|who\s+are|what'?s|what\s+is|what)\s+(assigned\s+to|working|in|on)\s+(division|group|branch|sector|strike\s+team|task\s+force|staging|lane)\s*(.*)$"),
				"incident_resources", m => P("laneName", BuildLaneName(m.Groups[3].Value, m.Groups[4].Value))),
			(R(@"^(what|which)\s+(resources|units?|crews?|companies|apparatus|personnel)\s+(do\s+i\s+have|do\s+we\s+have|are|is)\s+(on\s*scene|assigned|working|committed|on\s+(?:the\s+)?incident)(\s+.*)?$"),
				"incident_resources", null),
			(R(@"^(incident\s+)?(resources|assignments|resource\s+list)$"), "incident_resources", null),
			(R(@"^(what|who)\s+(do\s+i|do\s+we)\s+have\s+(on\s*scene|working|committed|assigned)(\s+.*)?$"),
				"incident_resources", null),
			(R(@"^(who'?s|who\s+is|what'?s|what\s+is|what)\s+(un|not\s+)assigned$"), "incident_resources", m => P("laneName", "unassigned")),

			// Objectives / benchmarks.
			(R(@"^(incident\s+)?(objectives?|benchmarks?|tactical\s+objectives?)$"), "incident_objectives", null),
			(R(@"^(what|which)\s+(objectives?|benchmarks?)\s+(are\s+)?(open|outstanding|incomplete|remaining|left|still\s+open|not\s+(?:done|complete))$"),
				"incident_objectives", null),
			(R(@"^(what'?s|what\s+is)\s+(still\s+)?(open|outstanding|left|remaining|incomplete)(\s+on\s+(?:the\s+|this\s+)?(incident|scene|board))?$"),
				"incident_objectives", null),
			(R(@"^(what'?s|what\s+is)\s+(my|our|the)\s+next\s+benchmark$"), "incident_objectives", null),

			// Needs / resource orders.
			(R(@"^(incident\s+)?(needs?|resource\s+orders?|orders?)$"), "incident_needs", null),
			(R(@"^(what|which)\s+(needs?|orders?|requests?)\s+(are\s+)?(open|unfilled|outstanding|pending|not\s+(?:met|filled))$"),
				"incident_needs", null),
			(R(@"^what\s+(did|have)\s+(i|we)\s+order(ed)?(\s+.*)?$"), "incident_needs", null),
			(R(@"^(what'?s|what\s+is)\s+(not\s+)?(been\s+)?(filled|met|arrived)$"), "incident_needs", null),
			(R(@"^what\s+(am\s+i|are\s+we)\s+(waiting\s+on|short\s+on|short)$"), "incident_needs", null),

			// ICS positions / command roles.
			(R(@"^(ics\s+)?(roles?|positions?|command\s+staff|general\s+staff)$"), "incident_roles", null),
			// Role lookups are keyed off explicit ICS position vocabulary (RoleWords) so an open-ended
			// "who is <name>" stays a personnel_lookup instead of being swallowed here.
			(R(@"^(who'?s|who\s+is|who\s+has)\s+(my|the|our)?\s*" + RoleWords + @"\s*\??$"),
				"incident_roles", m => P("roleQuery", m.Groups[3].Value.Trim())),
			(R(@"^(what|which)\s+(ics\s+)?(roles?|positions?)\s+(are\s+)?(unfilled|open|vacant|empty|not\s+assigned|missing)$"),
				"incident_roles", null),
			(R(@"^(do\s+i|do\s+we|have\s+i|have\s+we)\s+(have|got|assigned)\s+(an?\s+)?" + RoleWords + @"\s*\??$"),
				"incident_roles", m => P("roleQuery", m.Groups[4].Value.Trim())),

			// Incident (ICS-201) timeline.
			(R(@"^(incident\s+)?(timeline|incident\s+log|command\s+log|log)$"), "incident_timeline", null),
			(R(@"^what\s+(has\s+)?happened(\s+(?:in\s+)?(?:the\s+)?last\s+(\d+)\s*(minutes?|mins?|hours?|hrs?))?(\s+.*)?$"),
				"incident_timeline", m => P("minutes", ToMinutes(m.Groups[3].Value, m.Groups[4].Value))),
			(R(@"^(read|show|give|list)\s+(me\s+)?(the\s+)?last\s+(\d+)\s+(log\s+)?(entries|entry|events)$"),
				"incident_timeline", m => P("count", m.Groups[4].Value)),

			// Incident timers (PAR/benchmark reminders).
			(R(@"^(incident\s+)?timers?$"), "incident_timers", null),
			(R(@"^(what|which)\s+timers?\s+(are\s+)?(running|due|up|active)$"), "incident_timers", null),
			(R(@"^(what'?s|what\s+is|when'?s|when\s+is)\s+(my|the|our)\s+next\s+(par|check\s*-?\s*in|timer)(\s+.*)?$"),
				"incident_timers", null),

			// Transfer-of-command / ICS-201 briefing.
			(R(@"^(briefing|brief\s+me|transfer\s+of\s+command|ics\s*-?\s*201|command\s+brief(ing)?)$"),
				"incident_briefing", null),
			(R(@"^(give|draft|write|prepare|build|make)\s+(me\s+)?(a\s+|the\s+)?(briefing|brief|transfer\s+of\s+command(\s+briefing)?|ics\s*-?\s*201|hand\s*-?\s*off(\s+briefing)?)$"),
				"incident_briefing", null),

			// Incident-type ICS checklist / playbook.
			(R(@"^(checklist|playbook|what\s+am\s+i\s+missing|what\s+are\s+we\s+missing|what'?s\s+next)$"),
				"incident_checklist", null),
			(R(@"^(what|anything)\s+(am\s+i|are\s+we)\s+(missing|forgetting)(\s+.*)?$"), "incident_checklist", null),
			(R(@"^what\s+should\s+(i|we)\s+(be\s+)?(doing|do|consider|think\s+about)(\s+.*)?$"), "incident_checklist", null),
			(R(@"^(checklist|playbook)\s+(?:for\s+)?(?:an?\s+)?(.+)$"),
				"incident_checklist", m => P("incidentType", m.Groups[2].Value.Trim())),

			// Weather at the incident location (distinct from the department-wide weather alerts).
			(R(@"^(incident\s+weather|scene\s+weather|weather\s+(?:at|on)\s+(?:the\s+)?(?:scene|incident|icp|command\s+post))$"),
				"incident_weather", null),
			(R(@"^(what'?s|what\s+is)\s+(the\s+)?(wind|weather)\s*(doing|at\s+(?:the\s+)?(?:scene|incident|icp))?$"),
				"incident_weather", null),
			(R(@"^(wind|wind\s+direction|wind\s+speed)$"), "incident_weather", null),

			// Operational status notes on the incident.
			(R(@"^(incident\s+)?(notes|situation\s+updates?)$"), "incident_notes", null),
			(R(@"^(what|any)\s+(notes|situation\s+updates?)(\s+.*)?$"), "incident_notes", null),

			// Overall incident status / size-up. Last in the block so the sharper questions above win.
			(R(@"^(incident|command|scene)\s+(status|summary|snapshot|overview)$"), "incident_status", null),
			(R(@"^(size\s*-?\s*up|sizeup|sitrep|situation\s+report|can\s+report|status\s+board)$"), "incident_status", null),
			(R(@"^(what'?s|what\s+is)\s+(the\s+)?(status|situation|picture)\s+(of|on|at)\s+(the\s+|this\s+)?(incident|command|scene|call)$"),
				"incident_status", null),
			(R(@"^(where\s+(do|are)\s+we\s+(stand|at)|how\s+are\s+we\s+doing|how'?s\s+it\s+going\s+out\s+there)\s*$"),
				"incident_status", null),

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
				"call_responders", m => P2("mode", "onscene", "callRef", CleanReference(m.Groups[3].Value))),

			// "who's in route to the fire", "who is responding to c1445", "who's coming"
			(R(@"^(who'?s|who\s+is|who\s+are)\s+((?:in|en)\s*route|responding|headed|heading|going|coming)(\s+(?:to|for)\s+(.+))?$"),
				"call_responders", m => P2("mode", "enroute", "callRef", CleanReference(m.Groups[4].Value))),

			// "who got dispatched to the medical", "who's dispatched to 26-1" — the full dispatch
			// list (personnel, groups, roles and units) rather than live statuses.
			(R(@"^(who'?s|who\s+is|who\s+are|who\s+got|who\s+was|who\s+were|who)\s+dispatched(\s+(?:to|on|for)\s+(.+))?$"),
				"call_dispatched", m => P("callRef", CleanReference(m.Groups[3].Value))),

			// "who's on call 26-1", "who is on the fire" — responding + on-scene for a call.
			(R(@"^(who'?s|who\s+is|who\s+are)\s+on(\s+call)?(\s+(.+))?$"),
				"call_responders", m => P2("mode", "all", "callRef", CleanReference(m.Groups[4].Value))),

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
			// Upcoming-calendar phrasings: "when is the next event?", "what is upcoming in the
			// calendar?", "upcoming events", "what's coming up", "next events".
			(R(@"^when('?s|\s+is)\s+(the\s+)?next\s+(event|meeting|training|class)(s|es)?$"),
				"list_calendar", null),
			(R(@"^(what('?s|\s+is)\s+)?(upcoming|coming\s+up)(\s+(events?|meetings?|trainings?))?(\s+(on|in)\s+(the\s+)?(calendar|schedule|agenda))?$"),
				"list_calendar", null),
			(R(@"^(next|upcoming)\s+(events?|meetings?|trainings?)$"),
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
			(R(@"^(not\s*responding|not\s+going|unable\s+to\s+respond)\s+(?:to\s+)?(.+)$"),
				"respond_to_call", m => P2("callRef", CleanReference(m.Groups[2].Value), "response", "no")),
			(R(@"^(respond|en\s*route|going)\s+to\s+c?(\d+)$"),
				"respond_to_call", m => P2("callId", m.Groups[2].Value, "response", "yes")),
			// Responder shorthand: "omw to 26-1", "omw to fire", "enroute to c1445", "headed to Main St".
			// The reference can be a call id, a call number (yy-N), or a term matched against active
			// calls — resolved by the handler.
			(R(@"^(omw|on\s+my\s+way|respond(?:ing)?|going|headed|enroute|en\s+route)\s+(?:to\s+)?(.+)$"),
				"respond_to_call", m => P2("callRef", CleanReference(m.Groups[2].Value), "response", "yes")),

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
			foreach (var (pattern, intent, extractor) in _patterns)
			{
				foreach (var candidate in candidates)
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

		private static string CleanReference(string value)
		{
			return value?.Trim().TrimEnd('?', '!', '.', ',');
		}

		/// <summary>
		/// Rebuilds the lane reference an incident question named ("division a", "staging", "group 2")
		/// from the ICS node word and whatever followed it. The handler matches this loosely against the
		/// board's lane names, so "division" alone (no designator) is passed through as-is.
		/// </summary>
		private static string BuildLaneName(string nodeWord, string remainder)
		{
			var designator = CleanReference(remainder);
			var word = nodeWord?.Trim() ?? string.Empty;
			return string.IsNullOrWhiteSpace(designator) ? word : $"{word} {designator}".Trim();
		}

		/// <summary>
		/// Normalizes a "last N minutes/hours" timeline window to whole minutes. Returns an empty string
		/// when the question carried no window, letting the handler apply its own default.
		/// </summary>
		private static string ToMinutes(string amount, string unit)
		{
			if (!int.TryParse(amount?.Trim(), out var value) || value <= 0)
				return string.Empty;

			var u = unit?.Trim().ToLowerInvariant() ?? string.Empty;
			if (u.StartsWith("h"))
				value *= 60;

			return value.ToString();
		}

		private static bool IsNegativeCallResponse(string value)
		{
			var normalized = value?.ToLowerInvariant().Replace(" ", string.Empty);
			return normalized == "notresponding" || normalized == "notgoing";
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
