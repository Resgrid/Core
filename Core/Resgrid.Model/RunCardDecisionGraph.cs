using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Pure rules for an alarm level's decision graph (conditional run cards): validation on save and the shared helpers
	/// the evaluator and editors use. AND is a condition on another's Yes branch, OR is two conditions whose Yes branches
	/// share an outcome, NOT is swapping the branches; a node may therefore have several parents, but never a cycle.
	/// </summary>
	public static class RunCardDecisionGraph
	{
		public const int MaximumDepth = 8;
		public const int MaximumNodes = 64;
		public const int MaximumThresholdSeconds = 7200;
		public const int MaximumRequiredCount = 25;

		/// <summary>
		/// The problems that stop a level's graph from being saved, as English sentences for API callers and the editor.
		/// An empty list means the graph is valid. A level with no nodes is a fixed level and always valid.
		/// </summary>
		public static List<string> Validate(IEnumerable<RunCardDecisionNode> levelNodes)
		{
			var errors = new List<string>();
			var nodes = (levelNodes ?? Enumerable.Empty<RunCardDecisionNode>()).Where(n => n != null).ToList();

			if (!nodes.Any())
				return errors;

			if (nodes.Count > MaximumNodes)
				errors.Add($"A decision chart can have at most {MaximumNodes} nodes.");

			if (nodes.Any(n => string.IsNullOrWhiteSpace(n.NodeKey)))
				errors.Add("Every decision node needs a key.");

			var byKey = new Dictionary<string, RunCardDecisionNode>(StringComparer.OrdinalIgnoreCase);
			foreach (var node in nodes.Where(n => !string.IsNullOrWhiteSpace(n.NodeKey)))
			{
				if (byKey.ContainsKey(node.NodeKey))
					errors.Add($"Decision node key '{node.NodeKey}' is used twice.");
				else
					byKey[node.NodeKey] = node;
			}

			var roots = nodes.Where(n => n.IsRoot).ToList();
			if (roots.Count != 1)
			{
				errors.Add("A decision chart needs exactly one starting node.");
				return errors;
			}

			foreach (var node in nodes)
			{
				var name = DisplayName(node);

				if (node.NodeKind == (int)RunCardDecisionNodeKinds.Condition)
				{
					if (node.ConditionType != (int)RunCardConditionTypes.ArrivesBefore && node.ConditionType != (int)RunCardConditionTypes.AvailableWithin)
						errors.Add($"{name}: choose what the condition tests.");

					if (string.IsNullOrWhiteSpace(node.YesNodeKey) || string.IsNullOrWhiteSpace(node.NoNodeKey))
						errors.Add($"{name}: a condition needs both a Yes and a No branch.");
					else
					{
						if (!byKey.ContainsKey(node.YesNodeKey))
							errors.Add($"{name}: its Yes branch points to a node that does not exist.");

						if (!byKey.ContainsKey(node.NoNodeKey))
							errors.Add($"{name}: its No branch points to a node that does not exist.");
					}

					if (!node.ThresholdSeconds.HasValue || node.ThresholdSeconds.Value < 0 || node.ThresholdSeconds.Value > MaximumThresholdSeconds)
						errors.Add($"{name}: the time must be between 0 and {MaximumThresholdSeconds / 60} minutes.");

					if (node.SubjectTargets == null || !node.SubjectTargets.Any())
						errors.Add($"{name}: choose which resources the condition measures.");

					if (node.ConditionType == (int)RunCardConditionTypes.ArrivesBefore)
					{
						if (node.ReferenceTargets == null || !node.ReferenceTargets.Any())
							errors.Add($"{name}: choose which resources it is compared with.");
						else if (node.SubjectStatusFilter == node.ReferenceStatusFilter
							&& string.Equals(node.SubjectStatusBaseTypes ?? "", node.ReferenceStatusBaseTypes ?? "", StringComparison.OrdinalIgnoreCase)
							&& SameTargets(node.SubjectTargets, node.ReferenceTargets))
							errors.Add($"{name}: it compares a set of resources with itself.");
					}

					if (node.ConditionType == (int)RunCardConditionTypes.AvailableWithin && (node.RequiredCount < 1 || node.RequiredCount > MaximumRequiredCount))
						errors.Add($"{name}: the number of resources must be between 1 and {MaximumRequiredCount}.");

					if (node.SubjectStatusFilter == (int)RunCardStatusFilters.BaseTypes && !ParseBaseTypes(node.SubjectStatusBaseTypes).Any())
						errors.Add($"{name}: choose at least one status meaning.");

					if (node.ConditionType == (int)RunCardConditionTypes.ArrivesBefore && node.ReferenceStatusFilter == (int)RunCardStatusFilters.BaseTypes
						&& !ParseBaseTypes(node.ReferenceStatusBaseTypes).Any())
						errors.Add($"{name}: choose at least one status meaning for the comparison.");
				}
				else if (node.NodeKind == (int)RunCardDecisionNodeKinds.Outcome)
				{
					if (!string.IsNullOrWhiteSpace(node.YesNodeKey) || !string.IsNullOrWhiteSpace(node.NoNodeKey))
						errors.Add($"{name}: an outcome ends the chart and cannot branch.");

					if (!node.ResponsePackageId.HasValue && string.IsNullOrWhiteSpace(node.AdviceText))
						errors.Add($"{name}: an outcome needs a response package, advice, or both.");
				}
				else
				{
					errors.Add($"{name}: unknown node kind.");
				}
			}

			if (errors.Any())
				return errors;

			// Every node reachable from the start, no cycles, bounded depth.
			var root = roots[0];
			var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var cycle = false;
			var tooDeep = false;

			void Visit(RunCardDecisionNode node, HashSet<string> onPath, int depth)
			{
				if (cycle)
					return;

				// Questions count toward the depth, not the outcome that ends a path: eight questions in a row are allowed.
				if (depth > MaximumDepth && node.NodeKind == (int)RunCardDecisionNodeKinds.Condition)
				{
					tooDeep = true;
					return;
				}

				if (onPath.Contains(node.NodeKey))
				{
					cycle = true;
					return;
				}

				reachable.Add(node.NodeKey);

				if (node.NodeKind != (int)RunCardDecisionNodeKinds.Condition)
					return;

				onPath.Add(node.NodeKey);

				if (byKey.TryGetValue(node.YesNodeKey, out var yes))
					Visit(yes, onPath, depth + 1);

				if (byKey.TryGetValue(node.NoNodeKey, out var no))
					Visit(no, onPath, depth + 1);

				onPath.Remove(node.NodeKey);
			}

			Visit(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 1);

			if (cycle)
				errors.Add("The decision chart loops back on itself.");

			if (tooDeep)
				errors.Add($"The decision chart is more than {MaximumDepth} questions deep.");

			foreach (var orphan in nodes.Where(n => !reachable.Contains(n.NodeKey)))
				errors.Add($"{DisplayName(orphan)} cannot be reached from the starting node.");

			return errors;
		}

		/// <summary>The starting node, or null for a fixed level (or a graph that failed validation).</summary>
		public static RunCardDecisionNode Root(IEnumerable<RunCardDecisionNode> levelNodes)
		{
			var roots = (levelNodes ?? Enumerable.Empty<RunCardDecisionNode>()).Where(n => n != null && n.IsRoot).ToList();
			return roots.Count == 1 ? roots[0] : null;
		}

		public static bool HasGraph(RunCardAlarmLevel level)
		{
			return level?.DecisionNodes != null && level.DecisionNodes.Any(n => n != null);
		}

		/// <summary>Comma-separated ActionBaseTypes values as a set; anything unparseable is ignored.</summary>
		public static HashSet<int> ParseBaseTypes(string value)
		{
			var set = new HashSet<int>();

			if (string.IsNullOrWhiteSpace(value))
				return set;

			foreach (var part in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(part.Trim(), out var parsed) && Enum.IsDefined(typeof(ActionBaseTypes), parsed))
					set.Add(parsed);
			}

			return set;
		}

		public static bool SameTargets(IEnumerable<RunCardTarget> left, IEnumerable<RunCardTarget> right)
		{
			var a = new HashSet<string>((left ?? Enumerable.Empty<RunCardTarget>()).Where(t => t != null).Select(t => $"{t.TargetKind}:{t.TargetRefId}"));
			var b = new HashSet<string>((right ?? Enumerable.Empty<RunCardTarget>()).Where(t => t != null).Select(t => $"{t.TargetKind}:{t.TargetRefId}"));
			return a.SetEquals(b);
		}

		/// <summary>
		/// For an editor that does not edit conditional parts (the v4 API, or the web editor with the feature off): copies
		/// each stored level's decision graph and package onto the posted level with the same id (or alarm level number), so
		/// saving the card does not delete them. A card's own conditional options are carried by the caller.
		/// </summary>
		public static void CarryOverConditionalParts(IEnumerable<RunCardAlarmLevel> storedLevels, IEnumerable<RunCardAlarmLevel> postedLevels)
		{
			var stored = (storedLevels ?? Enumerable.Empty<RunCardAlarmLevel>()).Where(l => l != null).ToList();

			foreach (var level in (postedLevels ?? Enumerable.Empty<RunCardAlarmLevel>()).Where(l => l != null))
			{
				var match = (level.RunCardAlarmLevelId > 0 ? stored.FirstOrDefault(s => s.RunCardAlarmLevelId == level.RunCardAlarmLevelId) : null)
					?? stored.FirstOrDefault(s => s.AlarmLevel == level.AlarmLevel);

				if (match == null)
					continue;

				level.ResponsePackageId = match.ResponsePackageId;
				level.DecisionNodes = match.DecisionNodes;
			}
		}

		private static string DisplayName(RunCardDecisionNode node)
		{
			if (!string.IsNullOrWhiteSpace(node?.Label))
				return $"'{node.Label.Trim()}'";

			return node?.NodeKind == (int)RunCardDecisionNodeKinds.Outcome ? "An outcome" : "A condition";
		}
	}
}
