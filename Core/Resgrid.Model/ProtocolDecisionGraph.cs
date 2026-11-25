using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>What a decision node does (Protocols P4). Stored in the version's ContentJson; never renumber.</summary>
	public enum ProtocolDecisionNodeKinds
	{
		/// <summary>Asks the caller something; the answer picks the next node.</summary>
		Question = 1,

		/// <summary>A read-aloud script block (optionally played with Piper TTS for the dispatcher), then the next node.</summary>
		Instruction = 2,

		/// <summary>Ends the case: determinant code, suggested priority, response hint and post-dispatch instructions.</summary>
		Outcome = 3
	}

	/// <summary>How a question is answered. Stored; never renumber.</summary>
	public enum ProtocolQuestionTypes
	{
		/// <summary>Two answers, keys "yes" and "no".</summary>
		YesNo = 1,

		/// <summary>Two or more labelled answers, one chosen.</summary>
		SingleChoice = 2,

		/// <summary>A number; each answer is a range (inclusive minimum and maximum, either open).</summary>
		Number = 3,

		/// <summary>Free text, recorded as said; one next node.</summary>
		FreeText = 4
	}

	/// <summary>Where a question sits in the case flow: entry questions (address, phone, what happened) come before key questions.</summary>
	public enum ProtocolCaseStages
	{
		Entry = 0,
		Key = 1
	}

	/// <summary>
	/// A Dispatch-kind protocol version's decision graph (Protocols P4), generalized from the conditional run card graph
	/// (<see cref="RunCardDecisionNode"/>): nodes with stable keys, one root, children referenced by key so a node may be shared. It lives
	/// in the version's ContentJson (<see cref="ProtocolContentDocument.Graph"/>) and is rendered into the version's HTML, so it is covered
	/// by the content hash, approved with the version, and cannot change without a new version.
	/// </summary>
	public sealed class ProtocolDecisionGraph
	{
		[JsonProperty("nodes")]
		public List<ProtocolDecisionNode> Nodes { get; set; } = new List<ProtocolDecisionNode>();

		[JsonIgnore]
		public bool IsEmpty => Nodes == null || Nodes.All(n => n == null);

		public ProtocolDecisionNode Find(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : Nodes?.FirstOrDefault(n => n != null && string.Equals(n.Key, key.Trim(), StringComparison.Ordinal));

		public ProtocolDecisionNode Root() => Nodes?.Where(n => n != null && n.IsRoot).Take(2).Count() == 1 ? Nodes.First(n => n != null && n.IsRoot) : null;

		public static ProtocolDecisionGraph Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				return JsonConvert.DeserializeObject<ProtocolDecisionGraph>(json, new JsonSerializerSettings { MaxDepth = 32 });
			}
			catch (JsonException)
			{
				return null;
			}
		}

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
	}

	public sealed class ProtocolDecisionNode
	{
		[JsonProperty("key")] public string Key { get; set; }
		[JsonProperty("root")] public bool IsRoot { get; set; }
		/// <summary><see cref="ProtocolDecisionNodeKinds"/>.</summary>
		[JsonProperty("kind")] public int Kind { get; set; }
		/// <summary><see cref="ProtocolCaseStages"/> (questions).</summary>
		[JsonProperty("stage")] public int Stage { get; set; }
		/// <summary>A short name for the builder and the reports.</summary>
		[JsonProperty("label")] public string Label { get; set; }
		/// <summary>The question as read to the caller, or the instruction script.</summary>
		[JsonProperty("text")] public string Text { get; set; }
		/// <summary><see cref="ProtocolQuestionTypes"/> (questions).</summary>
		[JsonProperty("questionType")] public int? QuestionType { get; set; }
		[JsonProperty("answers")] public List<ProtocolDecisionAnswer> Answers { get; set; }
		/// <summary>The next node of an instruction or a free-text question.</summary>
		[JsonProperty("next")] public string Next { get; set; }
		/// <summary>Instruction: offer "play" (Piper TTS through the dispatch voice prompt path) to the dispatcher.</summary>
		[JsonProperty("speak")] public bool Speak { get; set; }
		/// <summary>Instruction: may be texted to the caller over the intake SMS thread.</summary>
		[JsonProperty("caller")] public bool SendToCaller { get; set; }
		/// <summary>Outcome: the determinant code (department-authored; never MPDS/IntelliComm content).</summary>
		[JsonProperty("determinant")] public string Determinant { get; set; }
		/// <summary>Outcome: the suggested call priority (DepartmentCallPriorityId, or 0–3 for the system priorities).</summary>
		[JsonProperty("priority")] public int? PriorityId { get; set; }
		/// <summary>Outcome: the priority's name when the graph was saved (rendered into the version's text).</summary>
		[JsonProperty("priorityName")] public string PriorityName { get; set; }
		/// <summary>Outcome: a response hint (e.g. "ALS, lights and siren"); run cards still pick the resources.</summary>
		[JsonProperty("response")] public string ResponseHint { get; set; }
		/// <summary>Outcome: what the dispatcher tells the caller after dispatch (pre-arrival instructions).</summary>
		[JsonProperty("postDispatch")] public string PostDispatch { get; set; }

		[JsonIgnore] public bool IsQuestion => Kind == (int)ProtocolDecisionNodeKinds.Question;
		[JsonIgnore] public bool IsInstruction => Kind == (int)ProtocolDecisionNodeKinds.Instruction;
		[JsonIgnore] public bool IsOutcome => Kind == (int)ProtocolDecisionNodeKinds.Outcome;
	}

	public sealed class ProtocolDecisionAnswer
	{
		[JsonProperty("key")] public string Key { get; set; }
		[JsonProperty("label")] public string Label { get; set; }
		[JsonProperty("next")] public string Next { get; set; }
		/// <summary>Number questions: the range's inclusive minimum; null for open.</summary>
		[JsonProperty("min")] public decimal? Min { get; set; }
		/// <summary>Number questions: the range's inclusive maximum; null for open.</summary>
		[JsonProperty("max")] public decimal? Max { get; set; }
	}

	/// <summary>A reason a graph cannot be approved or run. Codes are stable (the web localizes them); the message is English.</summary>
	public enum ProtocolGraphProblems
	{
		TooManyNodes = 1,
		MissingKey,
		DuplicateKey,
		RootCount,
		UnknownKind,
		QuestionText,
		QuestionType,
		Answers,
		AnswerLabel,
		NextMissing,
		NumberRange,
		InstructionText,
		OutcomeBranches,
		OutcomeDeterminant,
		DeterminantFormat,
		Cycle,
		TooDeep,
		Unreachable,
		NoOutcome
	}

	public sealed class ProtocolGraphProblem
	{
		public ProtocolGraphProblems Code { get; set; }
		public string NodeKey { get; set; }
		public string Message { get; set; }
	}

	/// <summary>Labels the graph renderer writes around the authored text, in the version's content language.</summary>
	public sealed class ProtocolGraphLabels
	{
		public static ProtocolGraphLabels Default { get; } = new ProtocolGraphLabels();

		public string CaseFlow { get; set; } = "Case flow";
		public string EntryQuestion { get; set; } = "Entry question";
		public string KeyQuestion { get; set; } = "Key question";
		public string Instruction { get; set; } = "Instruction";
		public string Outcome { get; set; } = "Outcome";
		public string Determinant { get; set; } = "Determinant";
		public string Priority { get; set; } = "Priority";
		public string Response { get; set; } = "Response";
		public string PostDispatch { get; set; } = "After dispatch";
		public string GoTo { get; set; } = "go to step {0}";
		public string AnyAnswer { get; set; } = "Any answer";
		public string Yes { get; set; } = "Yes";
		public string No { get; set; } = "No";
		public string Start { get; set; } = "Start";
	}

	/// <summary>
	/// The rules for a protocol decision graph (P4): normalizing what the builder posts, validating it (one root, every path reaches an
	/// outcome, no cycles, bounded size and depth) and rendering it into the version's HTML.
	/// </summary>
	public static class ProtocolDecisionGraphs
	{
		public const int MaxNodes = 150;
		public const int MaxAnswers = 12;
		public const int MaxDepth = 40;
		public const int LabelMaxLength = 200;
		public const int TextMaxLength = 2000;
		public const int AnswerLabelMaxLength = 200;
		public const int DeterminantMaxLength = 32;
		public const int ResponseMaxLength = 200;
		public const int PostDispatchMaxLength = 4000;
		public const string Yes = "yes";
		public const string No = "no";

		private static readonly Regex DeterminantPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._/-]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		private static readonly Regex KeyPattern = new Regex(@"^[a-z0-9]{1,24}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		public static bool IsValidKey(string key) => !string.IsNullOrEmpty(key) && KeyPattern.IsMatch(key);

		public static bool IsValidDeterminant(string determinant) =>
			!string.IsNullOrWhiteSpace(determinant) && DeterminantPattern.IsMatch(determinant.Trim());

		/// <summary>A determinant as stored: trimmed, inner whitespace folded; null when blank.</summary>
		public static string NormalizeDeterminant(string determinant)
		{
			if (string.IsNullOrWhiteSpace(determinant))
				return null;
			var text = Regex.Replace(determinant.Trim(), @"\s+", " ");
			return text.Length > DeterminantMaxLength ? text.Substring(0, DeterminantMaxLength) : text;
		}

		public static bool SameDeterminant(string a, string b) =>
			string.Equals(NormalizeDeterminant(a) ?? string.Empty, NormalizeDeterminant(b) ?? string.Empty, StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// The graph as it will be stored: nulls and unknown kinds dropped, text trimmed and capped, keys lower-cased, answer keys kept
		/// when valid and issued otherwise (yes/no questions always have exactly "yes" and "no"), fields that do not belong to a node's
		/// kind cleared. Null when nothing is left. Structure (pointers, roots) is not repaired here; <see cref="Validate"/> reports it.
		/// </summary>
		public static ProtocolDecisionGraph Normalize(ProtocolDecisionGraph graph)
		{
			if (graph?.Nodes == null)
				return null;

			var nodes = new List<ProtocolDecisionNode>();
			foreach (var node in graph.Nodes.Where(n => n != null).Take(MaxNodes + 1))
			{
				if (!Enum.IsDefined(typeof(ProtocolDecisionNodeKinds), node.Kind))
					continue;

				var clean = new ProtocolDecisionNode
				{
					Key = (node.Key ?? string.Empty).Trim().ToLowerInvariant(),
					IsRoot = node.IsRoot,
					Kind = node.Kind,
					Label = Plain(node.Label, LabelMaxLength)
				};

				switch ((ProtocolDecisionNodeKinds)node.Kind)
				{
					case ProtocolDecisionNodeKinds.Question:
						clean.Stage = node.Stage == (int)ProtocolCaseStages.Entry ? (int)ProtocolCaseStages.Entry : (int)ProtocolCaseStages.Key;
						clean.Text = Plain(node.Text, TextMaxLength);
						clean.QuestionType = node.QuestionType.HasValue && Enum.IsDefined(typeof(ProtocolQuestionTypes), node.QuestionType.Value) ? node.QuestionType : null;
						if (clean.QuestionType == (int)ProtocolQuestionTypes.FreeText)
							clean.Next = Key(node.Next);
						else
							clean.Answers = NormalizeAnswers(node.Answers, clean.QuestionType);
						break;
					case ProtocolDecisionNodeKinds.Instruction:
						clean.Text = Plain(node.Text, TextMaxLength);
						clean.Next = Key(node.Next);
						clean.Speak = node.Speak;
						clean.SendToCaller = node.SendToCaller;
						break;
					case ProtocolDecisionNodeKinds.Outcome:
						clean.Determinant = NormalizeDeterminant(node.Determinant);
						clean.PriorityId = node.PriorityId.HasValue && node.PriorityId.Value >= 0 ? node.PriorityId : null;
						clean.PriorityName = clean.PriorityId.HasValue ? Plain(node.PriorityName, 100) : null;
						clean.ResponseHint = Plain(node.ResponseHint, ResponseMaxLength);
						clean.PostDispatch = Plain(node.PostDispatch, PostDispatchMaxLength);
						clean.SendToCaller = node.SendToCaller && clean.PostDispatch != null;
						break;
				}

				nodes.Add(clean);
			}

			return nodes.Count == 0 ? null : new ProtocolDecisionGraph { Nodes = nodes };
		}

		private static List<ProtocolDecisionAnswer> NormalizeAnswers(IEnumerable<ProtocolDecisionAnswer> answers, int? questionType)
		{
			var list = (answers ?? Enumerable.Empty<ProtocolDecisionAnswer>()).Where(a => a != null).Take(MaxAnswers).ToList();

			if (questionType == (int)ProtocolQuestionTypes.YesNo)
			{
				var yes = list.FirstOrDefault(a => string.Equals(a.Key, Yes, StringComparison.OrdinalIgnoreCase)) ?? list.ElementAtOrDefault(0);
				var no = list.FirstOrDefault(a => string.Equals(a.Key, No, StringComparison.OrdinalIgnoreCase)) ?? list.Where(a => a != yes).FirstOrDefault();
				return new List<ProtocolDecisionAnswer>
				{
					new ProtocolDecisionAnswer { Key = Yes, Label = Plain(yes?.Label, AnswerLabelMaxLength), Next = Key(yes?.Next) },
					new ProtocolDecisionAnswer { Key = No, Label = Plain(no?.Label, AnswerLabelMaxLength), Next = Key(no?.Next) }
				};
			}

			var used = new HashSet<string>(StringComparer.Ordinal);
			var clean = new List<ProtocolDecisionAnswer>();
			var index = 0;
			foreach (var answer in list)
			{
				index++;
				var key = (answer.Key ?? string.Empty).Trim().ToLowerInvariant();
				if (!IsValidKey(key) || !used.Add(key))
				{
					var n = index;
					do { key = "a" + n.ToString(CultureInfo.InvariantCulture); n += MaxAnswers; } while (!used.Add(key));
				}

				clean.Add(new ProtocolDecisionAnswer
				{
					Key = key,
					Label = Plain(answer.Label, AnswerLabelMaxLength),
					Next = Key(answer.Next),
					Min = questionType == (int)ProtocolQuestionTypes.Number ? answer.Min : null,
					Max = questionType == (int)ProtocolQuestionTypes.Number ? answer.Max : null
				});
			}

			return clean;
		}

		/// <summary>
		/// What stops the graph from being approved or run; empty when it is valid. A version without a graph has nothing to validate.
		/// Checked: size, keys, exactly one root, each kind's fields, pointers that exist, number ranges that do not overlap, every node
		/// reachable from the root, no cycles, at most <see cref="MaxDepth"/> nodes on a path, and every path ending at an outcome.
		/// </summary>
		public static List<ProtocolGraphProblem> Validate(ProtocolDecisionGraph graph)
		{
			var problems = new List<ProtocolGraphProblem>();
			var nodes = graph?.Nodes?.Where(n => n != null).ToList() ?? new List<ProtocolDecisionNode>();
			if (nodes.Count == 0)
				return problems;

			void Add(ProtocolGraphProblems code, string key, string message) => problems.Add(new ProtocolGraphProblem { Code = code, NodeKey = key, Message = message });

			if (nodes.Count > MaxNodes)
				Add(ProtocolGraphProblems.TooManyNodes, null, $"A case flow can have at most {MaxNodes} steps.");

			var byKey = new Dictionary<string, ProtocolDecisionNode>(StringComparer.Ordinal);
			foreach (var node in nodes)
			{
				if (!IsValidKey(node.Key))
					Add(ProtocolGraphProblems.MissingKey, node.Key, "Every step needs a key of lower-case letters and digits.");
				else if (byKey.ContainsKey(node.Key))
					Add(ProtocolGraphProblems.DuplicateKey, node.Key, $"Step key '{node.Key}' is used twice.");
				else
					byKey[node.Key] = node;
			}

			var roots = nodes.Count(n => n.IsRoot);
			if (roots != 1)
				Add(ProtocolGraphProblems.RootCount, null, "A case flow needs exactly one first step.");

			bool Exists(string key) => !string.IsNullOrWhiteSpace(key) && byKey.ContainsKey(key);

			foreach (var node in nodes)
			{
				var name = Name(node);
				switch ((ProtocolDecisionNodeKinds)node.Kind)
				{
					case ProtocolDecisionNodeKinds.Question:
						if (string.IsNullOrWhiteSpace(node.Text))
							Add(ProtocolGraphProblems.QuestionText, node.Key, $"{name}: write the question.");
						if (!node.QuestionType.HasValue || !Enum.IsDefined(typeof(ProtocolQuestionTypes), node.QuestionType.Value))
						{
							Add(ProtocolGraphProblems.QuestionType, node.Key, $"{name}: choose how the question is answered.");
							break;
						}

						if (node.QuestionType == (int)ProtocolQuestionTypes.FreeText)
						{
							if (!Exists(node.Next))
								Add(ProtocolGraphProblems.NextMissing, node.Key, $"{name}: choose the step after the answer.");
							break;
						}

						var answers = node.Answers ?? new List<ProtocolDecisionAnswer>();
						if (node.QuestionType == (int)ProtocolQuestionTypes.YesNo ? answers.Count != 2 : answers.Count < 2)
							Add(ProtocolGraphProblems.Answers, node.Key, $"{name}: a question needs at least two answers.");
						if (node.QuestionType != (int)ProtocolQuestionTypes.YesNo && answers.Any(a => string.IsNullOrWhiteSpace(a.Label)))
							Add(ProtocolGraphProblems.AnswerLabel, node.Key, $"{name}: every answer needs a label.");
						if (answers.Any(a => !Exists(a.Next)))
							Add(ProtocolGraphProblems.NextMissing, node.Key, $"{name}: every answer needs a next step that exists.");
						if (node.QuestionType == (int)ProtocolQuestionTypes.Number && !RangesValid(answers))
							Add(ProtocolGraphProblems.NumberRange, node.Key, $"{name}: number ranges must have a minimum at or below the maximum and must not overlap.");
						break;
					case ProtocolDecisionNodeKinds.Instruction:
						if (string.IsNullOrWhiteSpace(node.Text))
							Add(ProtocolGraphProblems.InstructionText, node.Key, $"{name}: write the instruction.");
						if (!Exists(node.Next))
							Add(ProtocolGraphProblems.NextMissing, node.Key, $"{name}: choose the step after the instruction.");
						break;
					case ProtocolDecisionNodeKinds.Outcome:
						if (!string.IsNullOrWhiteSpace(node.Next) || (node.Answers != null && node.Answers.Count > 0))
							Add(ProtocolGraphProblems.OutcomeBranches, node.Key, $"{name}: an outcome ends the case and cannot lead anywhere.");
						if (string.IsNullOrWhiteSpace(node.Determinant))
							Add(ProtocolGraphProblems.OutcomeDeterminant, node.Key, $"{name}: an outcome needs a determinant code.");
						else if (!IsValidDeterminant(node.Determinant))
							Add(ProtocolGraphProblems.DeterminantFormat, node.Key, $"{name}: a determinant code is letters, digits, spaces and . _ / - (at most {DeterminantMaxLength}).");
						break;
					default:
						Add(ProtocolGraphProblems.UnknownKind, node.Key, $"{name}: unknown step kind.");
						break;
				}
			}

			if (problems.Count > 0)
				return problems;

			// Structure: reachable from the root, acyclic, bounded depth, every path ends at an outcome (a node whose every child path
			// reaches one; pointers are known to exist here).
			var root = nodes.First(n => n.IsRoot);
			var reachable = new HashSet<string>(StringComparer.Ordinal);
			var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 on path, 2 done
			var cycle = false;
			var tooDeep = false;

			void Visit(ProtocolDecisionNode node, int depth)
			{
				if (cycle || tooDeep)
					return;
				if (depth > MaxDepth)
				{
					tooDeep = true;
					return;
				}

				if (state.TryGetValue(node.Key, out var s))
				{
					if (s == 1)
						cycle = true;
					return;
				}

				state[node.Key] = 1;
				reachable.Add(node.Key);
				foreach (var next in Children(node))
					Visit(byKey[next], depth + 1);
				state[node.Key] = 2;
			}

			Visit(root, 1);

			if (cycle)
				Add(ProtocolGraphProblems.Cycle, null, "The case flow loops back on itself.");
			if (tooDeep)
				Add(ProtocolGraphProblems.TooDeep, null, $"A path through the case flow is longer than {MaxDepth} steps.");
			if (cycle || tooDeep)
				return problems;

			foreach (var orphan in nodes.Where(n => !reachable.Contains(n.Key)))
				Add(ProtocolGraphProblems.Unreachable, orphan.Key, $"{Name(orphan)} cannot be reached from the first step.");

			// Acyclic: every leaf is an outcome exactly when every path ends at one.
			if (!nodes.Any(n => n.IsOutcome))
				Add(ProtocolGraphProblems.NoOutcome, null, "The case flow has no outcome.");

			return problems;
		}

		/// <summary>The keys a node leads to (answers in order, then its next).</summary>
		public static IEnumerable<string> Children(ProtocolDecisionNode node)
		{
			if (node == null || node.IsOutcome)
				yield break;

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var answer in node.Answers ?? Enumerable.Empty<ProtocolDecisionAnswer>())
			{
				if (!string.IsNullOrWhiteSpace(answer?.Next) && seen.Add(answer.Next))
					yield return answer.Next;
			}

			if (!string.IsNullOrWhiteSpace(node.Next) && seen.Add(node.Next))
				yield return node.Next;
		}

		private static bool RangesValid(List<ProtocolDecisionAnswer> answers)
		{
			if (answers.Any(a => a.Min.HasValue && a.Max.HasValue && a.Min.Value > a.Max.Value))
				return false;

			var ordered = answers.OrderBy(a => a.Min ?? decimal.MinValue).ToList();
			for (var i = 1; i < ordered.Count; i++)
			{
				var previousMax = ordered[i - 1].Max ?? decimal.MaxValue;
				var min = ordered[i].Min ?? decimal.MinValue;
				if (min <= previousMax)
					return false;
			}

			return true;
		}

		/// <summary>The answer a number falls into, or null.</summary>
		public static ProtocolDecisionAnswer RangeFor(ProtocolDecisionNode node, decimal value) =>
			node?.Answers?.FirstOrDefault(a => a != null && (!a.Min.HasValue || value >= a.Min.Value) && (!a.Max.HasValue || value <= a.Max.Value));

		/// <summary>
		/// The graph as HTML for the version's text (a "Case flow" section): each step numbered in stored order with its question,
		/// answers and where they lead, instructions as written, and outcomes with determinant, priority, response hint and post-dispatch
		/// instructions. Everything authored is encoded; ids are block anchors (<c>b-g-{key}</c>) and classes are <c>rgp-</c> tokens, so the
		/// structured allow-list keeps them. Deterministic for the same graph and labels, so the content hash covers the graph.
		/// </summary>
		public static string RenderHtml(ProtocolDecisionGraph graph, ProtocolGraphLabels labels = null)
		{
			var nodes = graph?.Nodes?.Where(n => n != null).ToList();
			if (nodes == null || nodes.Count == 0)
				return string.Empty;

			labels ??= ProtocolGraphLabels.Default;
			var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
			for (var i = 0; i < nodes.Count; i++)
			{
				if (!string.IsNullOrEmpty(nodes[i].Key) && !numbers.ContainsKey(nodes[i].Key))
					numbers[nodes[i].Key] = i + 1;
			}

			string Target(string key) => key != null && numbers.TryGetValue(key, out var n)
				? string.Format(CultureInfo.InvariantCulture, labels.GoTo, n)
				: string.Format(CultureInfo.InvariantCulture, labels.GoTo, "?");

			var html = new StringBuilder();
			html.Append("<div id=\"b-casegraph\" class=\"rgp-block rgp-graph\">");
			html.Append("<h2 id=\"b-casegraph-title\" class=\"rgp-heading\">").Append(E(labels.CaseFlow)).Append("</h2>");
			html.Append("<ol class=\"rgp-graph-steps\">");

			for (var i = 0; i < nodes.Count; i++)
			{
				var node = nodes[i];
				var id = IsValidKey(node.Key) ? " id=\"b-g-" + node.Key + "\"" : string.Empty;
				var kindClass = node.IsOutcome ? "rgp-graph-outcome" : node.IsInstruction ? "rgp-graph-instruction" : "rgp-graph-question";
				html.Append("<li").Append(id).Append(" class=\"rgp-graph-step ").Append(kindClass).Append("\">");

				var kind = node.IsOutcome ? labels.Outcome : node.IsInstruction ? labels.Instruction
					: node.Stage == (int)ProtocolCaseStages.Entry ? labels.EntryQuestion : labels.KeyQuestion;
				html.Append("<p class=\"rgp-title\"><strong>").Append(E(kind));
				if (node.IsRoot)
					html.Append(" (").Append(E(labels.Start)).Append(')');
				if (!string.IsNullOrWhiteSpace(node.Label))
					html.Append(": ").Append(E(node.Label));
				html.Append("</strong></p>");

				if (!string.IsNullOrWhiteSpace(node.Text))
					html.Append("<p>").Append(Lines(node.Text)).Append("</p>");

				if (node.IsQuestion)
				{
					html.Append("<ul class=\"rgp-graph-answers\">");
					if (node.QuestionType == (int)ProtocolQuestionTypes.FreeText)
					{
						html.Append("<li>").Append(E(labels.AnyAnswer)).Append(" &#8594; ").Append(E(Target(node.Next))).Append("</li>");
					}
					else
					{
						foreach (var answer in node.Answers ?? new List<ProtocolDecisionAnswer>())
						{
							var label = answer.Label;
							if (string.IsNullOrWhiteSpace(label) && node.QuestionType == (int)ProtocolQuestionTypes.YesNo)
								label = answer.Key == Yes ? labels.Yes : labels.No;
							html.Append("<li>").Append(E(label));
							if (node.QuestionType == (int)ProtocolQuestionTypes.Number)
								html.Append(" [").Append(E(Range(answer))).Append(']');
							html.Append(" &#8594; ").Append(E(Target(answer.Next))).Append("</li>");
						}
					}
					html.Append("</ul>");
				}
				else if (node.IsInstruction)
				{
					html.Append("<p class=\"rgp-caption\">").Append(E(Target(node.Next))).Append("</p>");
				}
				else if (node.IsOutcome)
				{
					html.Append("<p><strong>").Append(E(labels.Determinant)).Append(":</strong> ").Append(E(node.Determinant));
					if (!string.IsNullOrWhiteSpace(node.PriorityName))
						html.Append(" &#183; <strong>").Append(E(labels.Priority)).Append(":</strong> ").Append(E(node.PriorityName));
					if (!string.IsNullOrWhiteSpace(node.ResponseHint))
						html.Append(" &#183; <strong>").Append(E(labels.Response)).Append(":</strong> ").Append(E(node.ResponseHint));
					html.Append("</p>");
					if (!string.IsNullOrWhiteSpace(node.PostDispatch))
						html.Append("<p><strong>").Append(E(labels.PostDispatch)).Append(":</strong> ").Append(Lines(node.PostDispatch)).Append("</p>");
				}

				html.Append("</li>");
			}

			html.Append("</ol></div>");
			return html.ToString();
		}

		/// <summary>The marker the rendered case flow starts with (always the last section of a version's HTML).</summary>
		public const string RenderedMarker = "<div id=\"b-casegraph\"";

		/// <summary>
		/// The HTML without a rendered case flow at its end: an html editor or an older client that sends back a version's HTML must not turn
		/// the case flow into text (it is re-rendered from the graph).
		/// </summary>
		public static string StripRendered(string html)
		{
			if (string.IsNullOrEmpty(html))
				return html;

			var at = html.LastIndexOf(RenderedMarker, StringComparison.Ordinal);
			if (at < 0)
				return html;

			return html.TrimEnd().EndsWith("</ol></div>", StringComparison.Ordinal) ? html.Substring(0, at) : html;
		}

		private static string Range(ProtocolDecisionAnswer answer)
		{
			var min = answer.Min.HasValue ? answer.Min.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
			var max = answer.Max.HasValue ? answer.Max.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
			return min + ".." + max;
		}

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		private static string Lines(string value) => string.Join("<br>", (value ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(E));

		private static string Plain(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var text = value.Trim();
			return text.Length > max ? text.Substring(0, max) : text;
		}

		private static string Key(string value)
		{
			var key = (value ?? string.Empty).Trim().ToLowerInvariant();
			return key.Length == 0 ? null : key;
		}

		private static string Name(ProtocolDecisionNode node) =>
			!string.IsNullOrWhiteSpace(node?.Label) ? $"'{node.Label.Trim()}'" : $"Step '{node?.Key}'";
	}
}
