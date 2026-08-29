using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// Last-resort net at the response boundary: walks an outgoing model graph and replaces any
	/// value still carrying an ADP envelope with the REDACTED placeholder.
	///
	/// This exists because of a gap no other guard can see. The catalog proves a field is
	/// protected; the binding-parity test proves a read accessor EXISTS; nothing proves a
	/// controller actually calls the resolve method. Four real leaks were found by hand across the
	/// MVC and v4 surfaces — serialized contact fields, raw UDF values, an enveloped call name
	/// baked into a persisted chat channel name — and each was invisible to the test suite.
	/// Detecting the envelope itself cannot be forgotten the way a per-surface call can.
	///
	/// It is a NET, not a substitute for resolving properly: a caller holding a grant still needs
	/// the resolve call to see real values, and a redaction here means a surface was missed, which
	/// is why the filter logs every hit with its path.
	/// </summary>
	public static class ProtectedEgressScanner
	{
		/// <summary>Reflection is cached per type — the walk runs on a request path.</summary>
		private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

		/// <summary>
		/// Types whose contents are never walked. Anything outside the Resgrid object model is
		/// framework machinery: walking it wastes the node budget and risks touching getters with
		/// side effects that have nothing to do with protected data.
		/// </summary>
		private static readonly HashSet<Type> ScalarTypes = new()
		{
			typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
			typeof(Guid), typeof(Uri), typeof(byte[])
		};

		public sealed class EgressScanResult
		{
			/// <summary>Values replaced with the placeholder.</summary>
			public int Redacted { get; set; }

			/// <summary>
			/// Enveloped values found on read-only members, which cannot be rewritten. These still
			/// leave with the response — the count exists so the log says so plainly rather than
			/// implying the net caught everything.
			/// </summary>
			public int Unfixable { get; set; }

			/// <summary>Member paths that carried an envelope, for the log line.</summary>
			public List<string> Paths { get; } = new();

			/// <summary>True when the node budget ran out before the graph did.</summary>
			public bool Truncated { get; set; }

			public bool FoundAnything => Redacted > 0 || Unfixable > 0;
		}

		/// <summary>
		/// Walks <paramref name="root"/> and redacts in place. Bounded on both depth and node count:
		/// this runs per request for a protected department, and an unbounded reflective walk over
		/// an arbitrary view model is not something to put on a dispatch path.
		/// </summary>
		public static EgressScanResult Sanitize(object root, int maxDepth = 12, int maxNodes = 20000)
		{
			var result = new EgressScanResult();
			if (root == null)
				return result;

			var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
			var nodes = 0;

			Walk(root, "$", 0, maxDepth, maxNodes, visited, result, ref nodes);
			return result;
		}

		private static void Walk(object node, string path, int depth, int maxDepth, int maxNodes,
			HashSet<object> visited, EgressScanResult result, ref int nodes)
		{
			if (node == null || depth > maxDepth)
				return;

			if (nodes >= maxNodes)
			{
				result.Truncated = true;
				return;
			}

			nodes++;

			var type = node.GetType();
			if (type.IsPrimitive || type.IsEnum || ScalarTypes.Contains(type))
				return;

			// Reference cycles and shared child objects are both common in these graphs.
			if (!visited.Add(node))
				return;

			if (node is IDictionary dictionary)
			{
				foreach (DictionaryEntry entry in dictionary)
				{
					if (TryRedactValue(entry.Value, out var replacement))
					{
						// A dictionary slot can always be rewritten, unlike a read-only property.
						dictionary[entry.Key] = replacement;
						result.Redacted++;
						result.Paths.Add($"{path}[{entry.Key}]");
						continue;
					}

					Walk(entry.Value, $"{path}[{entry.Key}]", depth + 1, maxDepth, maxNodes, visited, result, ref nodes);
				}

				return;
			}

			if (node is IList list)
			{
				for (var i = 0; i < list.Count; i++)
				{
					if (TryRedactValue(list[i], out var replacement))
					{
						list[i] = replacement;
						result.Redacted++;
						result.Paths.Add($"{path}[{i}]");
						continue;
					}

					Walk(list[i], $"{path}[{i}]", depth + 1, maxDepth, maxNodes, visited, result, ref nodes);
				}

				return;
			}

			if (node is IEnumerable enumerable && !(node is string))
			{
				// Read-only sequences: nothing can be rewritten in place, but the elements
				// themselves may be objects whose properties can.
				var index = 0;
				foreach (var item in enumerable)
				{
					if (item != null && IsEnvelopedValue(item))
					{
						result.Unfixable++;
						result.Paths.Add($"{path}[{index}] (read-only sequence)");
					}
					else
					{
						Walk(item, $"{path}[{index}]", depth + 1, maxDepth, maxNodes, visited, result, ref nodes);
					}

					index++;
				}

				return;
			}

			if (!IsResgridType(type))
				return;

			foreach (var property in GetProperties(type))
			{
				object value;
				try
				{
					value = property.GetValue(node);
				}
				catch
				{
					// A computed getter that throws must not take the response down with it.
					continue;
				}

				if (value == null)
					continue;

				var childPath = $"{path}.{property.Name}";

				if (IsEnvelopedValue(value))
				{
					if (!property.CanWrite)
					{
						result.Unfixable++;
						result.Paths.Add($"{childPath} (read-only)");
						continue;
					}

					try
					{
						property.SetValue(node, value is byte[] ? null : ProtectedDataEnvelope.RedactionValue);
						result.Redacted++;
						result.Paths.Add(childPath);
					}
					catch
					{
						result.Unfixable++;
						result.Paths.Add($"{childPath} (not writable)");
					}

					continue;
				}

				Walk(value, childPath, depth + 1, maxDepth, maxNodes, visited, result, ref nodes);
			}
		}

		/// <summary>
		/// True for a text envelope, and for a binary envelope whose leading bytes are the rgdpb
		/// marker. Binary payloads are checked by prefix rather than decoded — an attachment can be
		/// megabytes.
		/// </summary>
		public static bool IsEnvelopedValue(object value)
		{
			if (value is string text)
				return ProtectedDataEnvelope.HasEnvelopePrefix(text);

			if (value is byte[] bytes)
				return HasBinaryEnvelopePrefix(bytes);

			return false;
		}

		private static bool TryRedactValue(object value, out object replacement)
		{
			replacement = null;

			if (value is string text && ProtectedDataEnvelope.HasEnvelopePrefix(text))
			{
				replacement = ProtectedDataEnvelope.RedactionValue;
				return true;
			}

			if (value is byte[] bytes && HasBinaryEnvelopePrefix(bytes))
				return true;

			return false;
		}

		/// <summary>True when a byte payload starts with the raw rgdpb marker.</summary>
		public static bool HasBinaryEnvelopePrefix(byte[] value)
		{
			if (value == null || value.Length < BinaryPrefixBytes.Length)
				return false;

			for (var i = 0; i < BinaryPrefixBytes.Length; i++)
			{
				if (value[i] != BinaryPrefixBytes[i])
					return false;
			}

			return true;
		}

		private static readonly byte[] BinaryPrefixBytes = Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix);

		/// <summary>
		/// Only Resgrid's own types are walked. A view model that happens to hold a framework
		/// object (an HttpContext, a logger, a DbConnection) must not drag the walk into it.
		/// </summary>
		private static bool IsResgridType(Type type)
		{
			var ns = type.Namespace;
			return ns != null && ns.StartsWith("Resgrid", StringComparison.Ordinal);
		}

		private static PropertyInfo[] GetProperties(Type type)
		{
			return PropertyCache.GetOrAdd(type, static t =>
			{
				var properties = new List<PropertyInfo>();
				foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				{
					// Indexers cannot be read without arguments.
					if (property.GetIndexParameters().Length > 0)
						continue;

					if (!property.CanRead)
						continue;

					properties.Add(property);
				}

				return properties.ToArray();
			});
		}

		private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
		{
			public static readonly ReferenceEqualityComparer Instance = new();

			public new bool Equals(object x, object y) => ReferenceEquals(x, y);

			public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
		}
	}
}
