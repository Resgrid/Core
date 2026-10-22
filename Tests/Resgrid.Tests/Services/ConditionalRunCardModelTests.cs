using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.RunCards;

namespace Resgrid.Tests.Services
{
	/// <summary>Conditional run cards: decision graph validation, target rules and the template catalog.</summary>
	[TestFixture]
	public class ConditionalRunCardModelTests
	{
		private static RunCardTarget Unit(int id) => new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitType, TargetRefId = id };

		private static RunCardDecisionNode Condition(string key, string yes, string no, bool root = false, int threshold = 60)
		{
			return new RunCardDecisionNode
			{
				NodeKey = key, IsRoot = root, NodeKind = (int)RunCardDecisionNodeKinds.Condition, Label = key, ConditionType = (int)RunCardConditionTypes.ArrivesBefore,
				ThresholdSeconds = threshold, SubjectTargets = new List<RunCardTarget> { Unit(1) }, ReferenceTargets = new List<RunCardTarget> { Unit(2) }, YesNodeKey = yes, NoNodeKey = no
			};
		}

		private static RunCardDecisionNode Outcome(string key, int? packageId = 7, string advice = null, bool root = false)
		{
			return new RunCardDecisionNode { NodeKey = key, IsRoot = root, NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = key, ResponsePackageId = packageId, AdviceText = advice };
		}

		[Test]
		public void a_well_formed_graph_is_valid()
		{
			RunCardDecisionGraph.Validate(new[] { Condition("a", "b", "c", root: true), Outcome("b"), Outcome("c") }).Should().BeEmpty();
		}

		[Test]
		public void graph_validation_reports_the_problems_a_dispatcher_can_fix()
		{
			RunCardDecisionGraph.Validate(new[] { Condition("a", "b", "missing"), Outcome("b") }).Should().NotBeEmpty("there is no start node and a branch points nowhere");
			RunCardDecisionGraph.Validate(new[] { Condition("a", "b", "c", root: true), Condition("b", "a", "c"), Outcome("c") }).Should().NotBeEmpty("the graph loops");
			RunCardDecisionGraph.Validate(new[] { Condition("a", "b", "c", root: true, threshold: 9000), Outcome("b"), Outcome("c") }).Should().NotBeEmpty("the threshold is over two hours");
			RunCardDecisionGraph.Validate(new[] { Condition("a", "b", "b", root: true), Outcome("b"), Outcome("orphan") }).Should().NotBeEmpty("a node is unreachable");
			RunCardDecisionGraph.Validate(new[] { Outcome("a", null, null, root: true) }).Should().NotBeEmpty("an outcome needs a package or advice");

			var self = Condition("a", "b", "c", root: true);
			self.ReferenceTargets = new List<RunCardTarget> { Unit(1) };
			RunCardDecisionGraph.Validate(new[] { self, Outcome("b"), Outcome("c") }).Should().NotBeEmpty("it compares a set with itself");

			self.ReferenceStatusFilter = (int)RunCardStatusFilters.InQuarters;
			RunCardDecisionGraph.Validate(new[] { self, Outcome("b"), Outcome("c") }).Should().BeEmpty("different status filters make different sets");
		}

		/// <summary>A chain of <paramref name="questions"/> conditions, each No ending at one shared outcome.</summary>
		private static List<RunCardDecisionNode> Chain(int questions)
		{
			var nodes = new List<RunCardDecisionNode>();

			for (var i = 1; i <= questions; i++)
				nodes.Add(Condition($"q{i}", i == questions ? "yes" : $"q{i + 1}", "no", root: i == 1));

			nodes.Add(Outcome("yes"));
			nodes.Add(Outcome("no"));
			return nodes;
		}

		[Test]
		public void the_depth_limit_counts_questions_not_the_outcome_that_ends_the_path()
		{
			RunCardDecisionGraph.Validate(Chain(RunCardDecisionGraph.MaximumDepth)).Should().BeEmpty("eight questions in a row are allowed");
			RunCardDecisionGraph.Validate(Chain(RunCardDecisionGraph.MaximumDepth + 1)).Should().Contain(e => e.Contains("questions deep"));
		}

		[Test]
		public void response_code_colours_keep_only_hex_colours()
		{
			DispatchResponseCodesConfig.NormalizeColor(" #d9534f ").Should().Be("#d9534f");
			DispatchResponseCodesConfig.NormalizeColor("#FFF").Should().Be("#FFF");
			DispatchResponseCodesConfig.NormalizeColor("red").Should().BeNull();
			DispatchResponseCodesConfig.NormalizeColor("#fff\" onmouseover=\"alert(1)").Should().BeNull("it goes into a style attribute");
			DispatchResponseCodesConfig.NormalizeColor("").Should().BeNull();
			DispatchResponseCodesConfig.NormalizeColor(null).Should().BeNull();
		}

		[Test]
		public void an_advice_only_outcome_is_a_valid_graph()
		{
			RunCardDecisionGraph.Validate(new[] { Outcome("aid", null, "Request mutual aid.", root: true) }).Should().BeEmpty();
		}

		[Test]
		public void target_rules_match_units_by_type_class_or_any_and_people_by_role()
		{
			var targets = new List<RunCardTarget>
			{
				Unit(10),
				new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitClass, TargetRefId = (int)UnitTypeClasses.Ambulance },
				new RunCardTarget { TargetKind = (int)RunCardTargetKinds.PersonnelRole, TargetRefId = 5 }
			};

			RunCardTargetRules.MatchesUnit(targets, 10, (int)UnitTypeClasses.Engine).Should().BeTrue();
			RunCardTargetRules.MatchesUnit(targets, 11, (int)UnitTypeClasses.Ambulance).Should().BeTrue();
			RunCardTargetRules.MatchesUnit(targets, 12, (int)UnitTypeClasses.Engine).Should().BeFalse();
			RunCardTargetRules.MatchesPerson(targets, new[] { 5 }).Should().BeTrue();
			RunCardTargetRules.MatchesPerson(targets, new[] { 6 }).Should().BeFalse();
			RunCardTargetRules.MatchesUnit(new[] { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.AnyUnit } }, 99, -1).Should().BeTrue();

			RunCardTargetRules.Describe(targets, new Dictionary<int, string> { { 10, "PIT" } }, new Dictionary<int, string> { { 5, "Paramedic" } })
				.Should().Be("PIT or Any Ambulance or Paramedic");
		}

		[Test]
		public void template_catalog_covers_every_category_with_unique_slugs()
		{
			RunCardTemplateCatalog.All.Count.Should().BeGreaterThanOrEqualTo(40);
			RunCardTemplateCatalog.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
			RunCardTemplateCatalog.All.Select(t => t.Category).Distinct().Should().Contain(new[]
			{
				"Fire Departments", "EMS", "Law Enforcement", "Search and Rescue", "Emergency Management", "Disaster Response",
				"Security Companies", "Event Medical / Security", "Industrial Response", "Delivery Companies", "General"
			});
			RunCardTemplateCatalog.GetById("FIRE-STRUCTURE-FIRST-ALARM").Should().NotBeNull();
			RunCardTemplateCatalog.Search("cardiac").Should().Contain(t => t.Id == "ems-cardiac-arrest-all-call");
		}

		[Test]
		public void every_template_is_internally_consistent()
		{
			foreach (var template in RunCardTemplateCatalog.All)
			{
				var packageKeys = new HashSet<string>(template.Packages.Select(p => p.Key));

				template.Levels.Should().NotBeEmpty(template.Id);
				template.Levels.Select(l => l.AlarmLevel).Should().OnlyHaveUniqueItems(template.Id);

				foreach (var level in template.Levels)
				{
					(level.PackageKey != null || level.Nodes.Any()).Should().BeTrue($"{template.Id} level {level.AlarmLevel} sends something");
					if (level.PackageKey != null)
						packageKeys.Should().Contain(level.PackageKey, template.Id);

					foreach (var node in level.Nodes.Where(n => n.PackageKey != null))
						packageKeys.Should().Contain(node.PackageKey, $"{template.Id} outcome {node.Key}");
				}

				foreach (var row in template.Packages.SelectMany(p => p.Rows))
				{
					row.Targets.Should().NotBeEmpty($"{template.Id} row {row.Label}");
					if (row.SubstitutePackageKey != null)
						packageKeys.Should().Contain(row.SubstitutePackageKey, $"{template.Id} substitute");
					if (row.ResponseCode != null)
					{
						var set = ResponseCodeTemplateCatalog.GetById(template.ResponseCodeSetId);
						set.Should().NotBeNull($"{template.Id} uses codes");
						set.Codes.Should().Contain(c => c.Code == row.ResponseCode, $"{template.Id} code {row.ResponseCode}");
					}
				}

				foreach (var target in template.AllTargets())
				{
					if (target.Kind == RunCardTargetKinds.UnitType || target.Kind == RunCardTargetKinds.UnitClass)
						(target.UnitClass.HasValue || target.SuggestedNames.Any()).Should().BeTrue($"{template.Id} unit target has a class or a name");
					if (target.Kind == RunCardTargetKinds.PersonnelRole)
						target.SuggestedNames.Should().NotBeEmpty($"{template.Id} role target has a name");
				}
			}
		}

		[Test]
		public void every_template_graph_passes_validation()
		{
			foreach (var template in RunCardTemplateCatalog.All.Where(t => t.HasGraph))
			{
				foreach (var level in template.Levels.Where(l => l.Nodes.Any()))
				{
					var nodes = level.Nodes.Select(n => new RunCardDecisionNode
					{
						NodeKey = n.Key,
						IsRoot = n.IsRoot,
						NodeKind = (int)n.Kind,
						Label = n.Label,
						ConditionType = n.Kind == RunCardDecisionNodeKinds.Condition ? (int)n.ConditionType : 0,
						SubjectStatusFilter = (int)n.SubjectStatusFilter,
						ReferenceStatusFilter = (int)n.ReferenceStatusFilter,
						ThresholdSeconds = n.ThresholdSeconds,
						RequiredCount = n.RequiredCount,
						YesNodeKey = n.YesKey,
						NoNodeKey = n.NoKey,
						ResponsePackageId = n.PackageKey != null ? 1 : (int?)null,
						AdviceText = n.AdviceText,
						SubjectTargets = n.SubjectTargets.Select(Stand).ToList(),
						ReferenceTargets = n.ReferenceTargets.Select(Stand).ToList()
					}).ToList();

					RunCardDecisionGraph.Validate(nodes).Should().BeEmpty($"{template.Id} level {level.AlarmLevel}");
				}
			}
		}

		[Test]
		public void the_template_code_sets_have_unique_codes()
		{
			foreach (var set in ResponseCodeTemplateCatalog.All)
				set.Codes.Select(c => c.Code).Should().OnlyHaveUniqueItems(set.Id);

			ResponseCodeTemplateCatalog.GetById("care-categories").Codes.Should().HaveCount(5);
		}

		// A stand-in department target that keeps two template targets distinct exactly when they differ.
		private static RunCardTarget Stand(RunCardTemplateTarget target)
		{
			var identity = $"{target.Kind}|{target.UnitClass}|{string.Join(",", target.SuggestedNames)}";
			var reference = target.Kind == RunCardTargetKinds.AnyUnit || target.Kind == RunCardTargetKinds.AnyPerson ? (int?)null : Math.Abs(identity.GetHashCode() % 100000) + 1;
			return new RunCardTarget { TargetKind = (int)(target.Kind == RunCardTargetKinds.UnitClass ? RunCardTargetKinds.UnitType : target.Kind), TargetRefId = reference };
		}
	}
}
