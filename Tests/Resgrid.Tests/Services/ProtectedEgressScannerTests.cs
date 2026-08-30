using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The response-boundary net (ADP plan 7.5). The catalog proves a field is protected and the
	/// binding-parity guard proves a read accessor exists, but nothing proves a surface CALLS the
	/// resolve method — four real leaks were found by hand and every one was invisible to the
	/// suite. This walks whatever is about to leave and redacts what still carries an envelope.
	/// </summary>
	[TestFixture]
	public class ProtectedEgressScannerTests
	{
		private const string Envelope = "rgdp:1:2:c29tZS1jaXBoZXJ0ZXh0";

		private static byte[] BinaryEnvelope() =>
			Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix).Concat(new byte[] { 1, 2, 3 }).ToArray();

		public class Leaky
		{
			public string Name { get; set; }
			public string Plain { get; set; }
			public byte[] Payload { get; set; }
			public Leaky Child { get; set; }
			public List<Leaky> Children { get; set; }
			public List<string> Values { get; set; }
			public Dictionary<string, string> Map { get; set; }
			public string ReadOnlyEnvelope => Envelope;
			public string Throws => throw new InvalidOperationException("computed getter blew up");
			public Uri External { get; set; }
		}

		[Test]
		public void An_enveloped_string_is_replaced_with_the_placeholder()
		{
			var model = new Leaky { Name = Envelope, Plain = "Engine 1" };

			var result = ProtectedEgressScanner.Sanitize(model);

			model.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			model.Plain.Should().Be("Engine 1", "values that were never protected are untouched");
			result.Redacted.Should().BeGreaterThan(0);
			result.Paths.Should().Contain(p => p.EndsWith(".Name"));
		}

		[Test]
		public void An_enveloped_binary_payload_is_nulled_rather_than_served()
		{
			// There is no readable placeholder for a file; ciphertext bytes must simply not go out.
			var model = new Leaky { Payload = BinaryEnvelope() };

			ProtectedEgressScanner.Sanitize(model);

			model.Payload.Should().BeNull();
		}

		[Test]
		public void Nested_objects_and_collections_are_walked()
		{
			var model = new Leaky
			{
				Child = new Leaky { Name = Envelope },
				Children = new List<Leaky> { new Leaky { Name = Envelope }, new Leaky { Name = "fine" } },
				Values = new List<string> { Envelope, "fine" },
				Map = new Dictionary<string, string> { ["a"] = Envelope, ["b"] = "fine" }
			};

			ProtectedEgressScanner.Sanitize(model);

			model.Child.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			model.Children[0].Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			model.Children[1].Name.Should().Be("fine");
			model.Values[0].Should().Be(ProtectedDataEnvelope.RedactionValue);
			model.Values[1].Should().Be("fine");
			model.Map["a"].Should().Be(ProtectedDataEnvelope.RedactionValue);
			model.Map["b"].Should().Be("fine");
		}

		[Test]
		public void A_reference_cycle_does_not_hang_the_walk()
		{
			var a = new Leaky { Name = Envelope };
			var b = new Leaky { Child = a };
			a.Child = b;

			var result = ProtectedEgressScanner.Sanitize(a);

			a.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.Truncated.Should().BeFalse("the visited set should end the walk, not the node budget");
		}

		[Test]
		public void A_read_only_member_is_reported_rather_than_silently_passed()
		{
			// Nothing can be done about it here, so the count and path have to say so — claiming a
			// clean scan when ciphertext is still leaving would be worse than not scanning.
			var result = ProtectedEgressScanner.Sanitize(new Leaky());

			result.Unfixable.Should().BeGreaterThan(0);
			result.Paths.Should().Contain(p => p.Contains("ReadOnlyEnvelope"));
		}

		[Test]
		public void A_throwing_getter_is_skipped_instead_of_breaking_the_response()
		{
			var model = new Leaky { Name = Envelope };

			Action scan = () => ProtectedEgressScanner.Sanitize(model);

			scan.Should().NotThrow();
			model.Name.Should().Be(ProtectedDataEnvelope.RedactionValue, "the rest of the graph is still scanned");
		}

		[Test]
		public void The_walk_stays_inside_the_resgrid_object_model()
		{
			// A view model that happens to hold a framework object must not drag the walk into it.
			var model = new Leaky { External = new Uri("https://example.org/a") };

			Action scan = () => ProtectedEgressScanner.Sanitize(model);

			scan.Should().NotThrow();
		}

		[Test]
		public void A_graph_over_the_node_budget_reports_truncation()
		{
			// A silent cap would read as "nothing found" on exactly the large payloads most likely
			// to be carrying something.
			var root = new Leaky { Children = new List<Leaky>() };
			for (var i = 0; i < 50; i++)
				root.Children.Add(new Leaky { Name = "fine" });

			var result = ProtectedEgressScanner.Sanitize(root, maxNodes: 5);

			result.Truncated.Should().BeTrue();
		}

		[Test]
		public void Null_and_empty_graphs_are_handled()
		{
			ProtectedEgressScanner.Sanitize(null).FoundAnything.Should().BeFalse();
			ProtectedEgressScanner.Sanitize(new Leaky()).Redacted.Should().Be(0);
		}

		[Test]
		public void Binary_prefix_detection_matches_only_the_marker()
		{
			ProtectedEgressScanner.HasBinaryEnvelopePrefix(BinaryEnvelope()).Should().BeTrue();
			ProtectedEgressScanner.HasBinaryEnvelopePrefix(new byte[] { 1, 2, 3 }).Should().BeFalse();
			ProtectedEgressScanner.HasBinaryEnvelopePrefix(Array.Empty<byte>()).Should().BeFalse();
			ProtectedEgressScanner.HasBinaryEnvelopePrefix(null).Should().BeFalse();
		}

		[Test]
		public void A_real_entity_graph_is_covered()
		{
			// The shape that actually leaked: an entity mapped into a result without resolving.
			var call = new Call
			{
				CallId = 4,
				Name = Envelope,
				Number = "26-45",
				NatureOfCall = Envelope,
				CallNotes = new List<CallNote> { new CallNote { CallNoteId = 1, Note = Envelope } }
			};

			var result = ProtectedEgressScanner.Sanitize(call);

			call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			call.NatureOfCall.Should().Be(ProtectedDataEnvelope.RedactionValue);
			call.Number.Should().Be("26-45", "the call number is deliberately not cataloged");
			call.CallNotes.First().Note.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.Redacted.Should().BeGreaterThanOrEqualTo(3);
		}
	}
}
