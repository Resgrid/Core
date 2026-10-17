using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A module row whose JSON <c>Content</c> packs operational metadata together with members that can identify a person
	/// (adp-catalog-review-2026-10-17 §8, option a). The private members are stored apart in <c>ProtectedContent</c>, the
	/// row's only ADP slot, and <c>Content</c> holds plaintext metadata the system runs on and decision models may read.
	/// In memory, a row read through its module store or revealed through its module seam has the private members merged
	/// back into <c>Content</c>, so services keep working with one document.
	/// </summary>
	public interface ISplitContentRow
	{
		string Content { get; set; }
		string ProtectedContent { get; set; }
		/// <summary>The members of Content that are private, as <see cref="SplitContent"/> paths.</summary>
		IReadOnlyCollection<string> PrivateMemberPaths { get; }
		/// <summary>The private members that hold text, which a display copy shows as the redaction placeholder.</summary>
		IReadOnlyCollection<string> PrivateTextMemberPaths { get; }
	}

	/// <summary>What <see cref="SplitContent.PrepareWrite"/> decided for one row.</summary>
	public readonly struct SplitContentWrite
	{
		public SplitContentWrite(bool allowed, string inMemoryContent, string priorProtectedContent)
		{
			Allowed = allowed;
			InMemoryContent = inMemoryContent;
			PriorProtectedContent = priorProtectedContent;
		}

		/// <summary>False when the row's private members are still sealed and the write carries new private values.</summary>
		public bool Allowed { get; }

		/// <summary>The merged document to put back on the row once it is written.</summary>
		public string InMemoryContent { get; }

		/// <summary>The row's ProtectedContent before the write was prepared.</summary>
		public string PriorProtectedContent { get; }

		/// <summary>The write went through: the row holds the merged document again.</summary>
		public void Done(ISplitContentRow row)
		{
			if (row != null)
				row.Content = InMemoryContent;
		}

		/// <summary>The write failed: the row is put back exactly as it was before the write was prepared.</summary>
		public void Undo(ISplitContentRow row)
		{
			if (row == null)
				return;
			row.Content = InMemoryContent;
			row.ProtectedContent = PriorProtectedContent;
		}
	}

	/// <summary>
	/// Moves the private members of a JSON document out to a second document and back. A path names one member:
	/// <c>Note</c>, <c>Snapshot.Description</c>, or <c>Lines[].Note</c> (<c>[]</c> walks every element of an array; the
	/// private copy keeps the same indices, with null for an element that has nothing private). Dates and decimals pass
	/// through as written.
	/// </summary>
	public static class SplitContent
	{
		/// <summary>
		/// Splits <paramref name="content"/> into plaintext metadata and the private members named by
		/// <paramref name="paths"/>. A null private member is dropped from both sides, so a document with nothing private
		/// returns a null private part. Empty content and an envelope pass through; any other content that is not a JSON
		/// object cannot be told apart, so all of it is private (fail closed).
		/// </summary>
		public static (string Content, string Private) Split(string content, IReadOnlyCollection<string> paths)
		{
			if (paths == null || paths.Count == 0 || string.IsNullOrWhiteSpace(content) || ProtectedDataEnvelope.HasEnvelopePrefix(content))
				return (content, null);
			if (!TryParseObject(content, out var document))
				return (null, content);

			var hidden = new JObject();
			foreach (var path in paths)
				Move(document, hidden, path.Split('.'), 0);

			return (document.ToString(Formatting.None), HasValues(hidden) ? hidden.ToString(Formatting.None) : null);
		}

		/// <summary>
		/// Fills in every private member <paramref name="content"/> lacks from <paramref name="privateMembers"/>. A member
		/// already present in <paramref name="content"/> wins, even when it is null, so a value cleared by the caller stays
		/// cleared. A sealed or redacted private part is left out (the caller decides what that means).
		/// </summary>
		public static string Merge(string content, string privateMembers)
		{
			if (string.IsNullOrEmpty(privateMembers) || IsSealed(privateMembers))
				return content;
			if (!TryParseObject(privateMembers, out var hidden))
				// A private part that is not a JSON object was the whole value (see Split).
				return string.IsNullOrEmpty(content) ? privateMembers : content;

			if (!TryParseObject(content, out var document))
			{
				if (!string.IsNullOrEmpty(content))
					return content;
				document = new JObject();
			}

			Fill(document, hidden);
			return document.ToString(Formatting.None);
		}

		/// <summary>True when the stored private part is an envelope or the redaction placeholder (it cannot be merged).</summary>
		public static bool IsSealed(string privateMembers) =>
			ProtectedDataEnvelope.HasEnvelopePrefix(privateMembers) || privateMembers == ProtectedDataEnvelope.RedactionValue;

		/// <summary>True when the stored private part is plaintext JSON (not empty, sealed or redacted).</summary>
		public static bool IsPlaintext(string privateMembers) => !string.IsNullOrEmpty(privateMembers) && !IsSealed(privateMembers);

		/// <summary>Store reads: merge a plaintext private part back into Content. A sealed part waits for the module's reveal.</summary>
		public static void MergeStored(ISplitContentRow row)
		{
			if (row != null && IsPlaintext(row.ProtectedContent))
				row.Content = Merge(row.Content, row.ProtectedContent);
		}

		/// <summary>
		/// Prepares a row for its write: Content keeps the metadata and ProtectedContent the private members. A row whose
		/// private part is still sealed (it was never revealed) keeps that envelope, which is only safe when the write
		/// carries no private value of its own; otherwise the write is refused. An unmerged copy with a plaintext private
		/// part is merged first, so members it never loaded are kept rather than dropped.
		/// </summary>
		public static SplitContentWrite PrepareWrite(ISplitContentRow row)
		{
			var paths = row?.PrivateMemberPaths;
			if (row == null || paths == null || paths.Count == 0)
				return new SplitContentWrite(true, row?.Content, row?.ProtectedContent);

			var prior = row.ProtectedContent;
			// A display copy (its private part shown as redacted) is read-only: writing it would replace what is stored.
			if (prior == ProtectedDataEnvelope.RedactionValue)
				return new SplitContentWrite(false, row.Content, prior);
			// A private member posted back as the redaction placeholder means "unchanged": the stored value is kept.
			var content = WithoutPlaceholders(row.Content, paths);
			if (IsSealed(prior))
			{
				var (metadata, hidden) = Split(content, paths);
				if (hidden != null)
					return new SplitContentWrite(false, row.Content, prior);
				row.Content = metadata;
				return new SplitContentWrite(true, content, prior);
			}

			var full = Merge(content, prior);
			(row.Content, row.ProtectedContent) = Split(full, paths);
			return new SplitContentWrite(true, full, prior);
		}

		/// <summary>
		/// Store writes: whatever path wrote the row, the stored shape is split. The module seam has normally done this
		/// (and sealed the private part) already, which makes this a no-op. Returns the document to put back on the row
		/// once it is written; throws rather than store new private values beside a sealed private part.
		/// </summary>
		public static string EnsureSplitForStore(ISplitContentRow row)
		{
			var write = PrepareWrite(row);
			if (!write.Allowed)
				throw new InvalidOperationException("Protected content is sealed: reveal the row before writing new private values.");
			return write.InMemoryContent;
		}

		/// <summary>
		/// Display without a reveal: the private members leave Content and the private part shows as redacted. Content that
		/// is sealed, or that is private as a whole (not a JSON object), shows as redacted too.
		/// </summary>
		public static void Redact(ISplitContentRow row)
		{
			if (row == null)
				return;
			var stripped = false;
			if (ProtectedDataEnvelope.HasEnvelopePrefix(row.Content))
				row.Content = ProtectedDataEnvelope.RedactionValue;
			else
			{
				var (metadata, hidden) = Split(row.Content, row.PrivateMemberPaths);
				stripped = metadata != null && hidden != null;
				row.Content = metadata == null && hidden != null ? ProtectedDataEnvelope.RedactionValue : metadata;
			}
			// The private part shows as withheld whenever there was one, stored apart or merged into Content.
			if (stripped || !string.IsNullOrEmpty(row.ProtectedContent))
				row.ProtectedContent = ProtectedDataEnvelope.RedactionValue;
		}

		/// <summary>
		/// A display copy for a reader without a grant: the private members leave Content, each text member whose parent is
		/// present shows the redaction placeholder (so a form built from it posts the placeholder back, which keeps the stored
		/// value), and ProtectedContent shows as redacted, which makes the copy read-only. A row with nothing private is
		/// left as it is.
		/// </summary>
		public static void RedactForDisplay(ISplitContentRow row)
		{
			if (row == null)
				return;
			Redact(row);
			if (row.ProtectedContent != ProtectedDataEnvelope.RedactionValue || !TryParseObject(row.Content, out var document))
				return;
			foreach (var path in row.PrivateTextMemberPaths ?? Array.Empty<string>())
				Placeholder(document, path.Split('.'), 0);
			row.Content = document.ToString(Formatting.None);
		}

		private static string WithoutPlaceholders(string content, IReadOnlyCollection<string> paths)
		{
			if (string.IsNullOrEmpty(content) || content.IndexOf(ProtectedDataEnvelope.RedactionValue, StringComparison.Ordinal) < 0 || !TryParseObject(content, out var document))
				return content;
			var changed = false;
			foreach (var path in paths)
				changed |= RemovePlaceholder(document, path.Split('.'), 0);
			return changed ? document.ToString(Formatting.None) : content;
		}

		private static bool RemovePlaceholder(JObject source, string[] segments, int index)
		{
			var segment = segments[index];
			var last = index == segments.Length - 1;
			if (segment.EndsWith("[]", StringComparison.Ordinal))
			{
				var changed = false;
				if (!last && source[segment.Substring(0, segment.Length - 2)] is JArray items)
					foreach (var element in items.OfType<JObject>())
						changed |= RemovePlaceholder(element, segments, index + 1);
				return changed;
			}
			if (last)
			{
				var property = source.Property(segment, StringComparison.Ordinal);
				if (property?.Value is JValue { Type: JTokenType.String } value && (string)value == ProtectedDataEnvelope.RedactionValue)
				{
					property.Remove();
					return true;
				}
				return false;
			}
			return source[segment] is JObject child && RemovePlaceholder(child, segments, index + 1);
		}

		private static void Placeholder(JObject target, string[] segments, int index)
		{
			var segment = segments[index];
			var last = index == segments.Length - 1;
			if (segment.EndsWith("[]", StringComparison.Ordinal))
			{
				if (!last && target[segment.Substring(0, segment.Length - 2)] is JArray items)
					foreach (var element in items.OfType<JObject>())
						Placeholder(element, segments, index + 1);
				return;
			}
			if (last)
				target[segment] = ProtectedDataEnvelope.RedactionValue;
			else if (target[segment] is JObject child)
				Placeholder(child, segments, index + 1);
		}

		/// <summary>
		/// True when a private member is held in plaintext outside an envelope: a plaintext private part, or private members
		/// still inside Content with no private part beside them (a row written before its table was split). A protected
		/// department fails closed on either. Content merged in memory beside a sealed part is not counted.
		/// </summary>
		public static bool CarriesUnsealedPrivate(ISplitContentRow row) =>
			row != null && (IsPlaintext(row.ProtectedContent)
				|| string.IsNullOrEmpty(row.ProtectedContent) && Split(row.Content, row.PrivateMemberPaths).Private != null);

		private static void Move(JObject source, JObject target, string[] segments, int index)
		{
			var segment = segments[index];
			var last = index == segments.Length - 1;
			if (segment.EndsWith("[]", StringComparison.Ordinal))
			{
				var name = segment.Substring(0, segment.Length - 2);
				if (last || !(source[name] is JArray items))
					return;

				var copy = target[name] as JArray;
				if (copy == null)
				{
					copy = new JArray(items.Select(_ => (JToken)JValue.CreateNull()));
					target[name] = copy;
				}

				for (var i = 0; i < items.Count && i < copy.Count; i++)
				{
					if (!(items[i] is JObject element))
						continue;
					if (!(copy[i] is JObject slot))
					{
						slot = new JObject();
						copy[i] = slot;
					}
					Move(element, slot, segments, index + 1);
				}
				return;
			}

			if (last)
			{
				var property = source.Property(segment, StringComparison.Ordinal);
				if (property == null)
					return;
				property.Remove();
				if (property.Value.Type != JTokenType.Null)
					target[segment] = property.Value;
				return;
			}

			if (!(source[segment] is JObject child))
				return;
			if (!(target[segment] is JObject childTarget))
			{
				childTarget = new JObject();
				target[segment] = childTarget;
			}
			Move(child, childTarget, segments, index + 1);
		}

		private static void Fill(JObject document, JObject hidden)
		{
			foreach (var property in hidden.Properties())
			{
				switch (property.Value)
				{
					case JObject nested:
						if (document[property.Name] is JObject existing)
							Fill(existing, nested);
						else if (document.Property(property.Name, StringComparison.Ordinal) == null)
							document[property.Name] = nested.DeepClone();
						break;
					case JArray hiddenItems:
						if (document[property.Name] is JArray items)
						{
							for (var i = 0; i < hiddenItems.Count && i < items.Count; i++)
								if (hiddenItems[i] is JObject hiddenElement && items[i] is JObject element)
									Fill(element, hiddenElement);
						}
						else if (document.Property(property.Name, StringComparison.Ordinal) == null)
							document[property.Name] = hiddenItems.DeepClone();
						break;
					default:
						if (document.Property(property.Name, StringComparison.Ordinal) == null)
							document[property.Name] = property.Value.DeepClone();
						break;
				}
			}
		}

		private static bool HasValues(JToken token)
		{
			switch (token)
			{
				case JObject value:
					foreach (var property in value.Properties().ToList())
						if (!HasValues(property.Value))
							property.Remove();
					return value.HasValues;
				case JArray value:
					var any = false;
					for (var i = 0; i < value.Count; i++)
					{
						if (HasValues(value[i]))
							any = true;
						else
							value[i] = JValue.CreateNull();
					}
					return any;
				default:
					return token != null && token.Type != JTokenType.Null;
			}
		}

		private static bool TryParseObject(string json, out JObject document)
		{
			document = null;
			if (string.IsNullOrWhiteSpace(json) || ProtectedDataEnvelope.HasEnvelopePrefix(json) || json.TrimStart()[0] != '{')
				return false;
			try
			{
				using var reader = new JsonTextReader(new StringReader(json))
				{
					DateParseHandling = DateParseHandling.None,
					FloatParseHandling = FloatParseHandling.Decimal
				};
				document = JObject.Load(reader);
				return true;
			}
			catch (JsonReaderException)
			{
				return false;
			}
		}
	}
}
