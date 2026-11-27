using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Resgrid.Framework;

namespace Resgrid.Model
{
	public enum ProtocolDiffKinds
	{
		Equal = 0,
		Inserted = 1,
		Deleted = 2
	}

	/// <summary>A run of words that is the same, added or removed between two versions.</summary>
	public sealed class ProtocolDiffSegment
	{
		public ProtocolDiffKinds Kind { get; set; }
		public string Text { get; set; }
	}

	/// <summary>One paragraph of the redline: unchanged, added, removed, or changed (with word-level segments).</summary>
	public sealed class ProtocolDiffParagraph
	{
		public bool IsHeading { get; set; }
		public bool Changed => Segments.Any(s => s.Kind != ProtocolDiffKinds.Equal);
		public List<ProtocolDiffSegment> Segments { get; set; } = new List<ProtocolDiffSegment>();
	}

	public sealed class ProtocolDiffResult
	{
		public List<ProtocolDiffParagraph> Paragraphs { get; set; } = new List<ProtocolDiffParagraph>();
		public int WordsAdded { get; set; }
		public int WordsRemoved { get; set; }
		public bool HasChanges => WordsAdded > 0 || WordsRemoved > 0;
	}

	/// <summary>
	/// The version compare redline (P1 "version compare"): the two versions' text split into paragraphs at block boundaries,
	/// matched paragraph by paragraph (longest common subsequence), and changed paragraphs compared word by word. Plain text in,
	/// plain text segments out: the page writes them encoded inside ins/del, so the redline never renders stored markup.
	/// Bounded: at most <see cref="MaxParagraphs"/> paragraphs and <see cref="MaxWordsPerParagraph"/> words per compared pair.
	/// </summary>
	public static class ProtocolVersionDiff
	{
		public const int MaxParagraphs = 1500;
		public const int MaxWordsPerParagraph = 600;

		private static readonly Regex BlockBreak = new Regex(@"</?(p|div|li|ul|ol|h[1-6]|blockquote|pre|br)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
		private static readonly Regex HeadingStart = new Regex(@"^\u0001H", RegexOptions.Compiled);
		private static readonly Regex Words = new Regex(@"\S+", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

		public static ProtocolDiffResult Compare(string fromHtml, string toHtml)
		{
			var from = Paragraphs(fromHtml);
			var to = Paragraphs(toHtml);
			var result = new ProtocolDiffResult();

			var ops = Lcs(from.Select(p => p.Text).ToList(), to.Select(p => p.Text).ToList(), StringComparer.Ordinal);
			var pendingDeleted = new List<(string Text, bool Heading)>();
			var pendingInserted = new List<(string Text, bool Heading)>();

			void Flush()
			{
				// Removed and added paragraphs between two unchanged ones are paired up and compared word by word.
				var pairs = Math.Min(pendingDeleted.Count, pendingInserted.Count);
				for (var i = 0; i < pairs; i++)
					result.Paragraphs.Add(WordDiff(pendingDeleted[i].Text, pendingInserted[i].Text, pendingInserted[i].Heading, result));
				foreach (var removed in pendingDeleted.Skip(pairs))
				{
					result.WordsRemoved += CountWords(removed.Text);
					result.Paragraphs.Add(new ProtocolDiffParagraph { IsHeading = removed.Heading, Segments = { new ProtocolDiffSegment { Kind = ProtocolDiffKinds.Deleted, Text = removed.Text } } });
				}
				foreach (var added in pendingInserted.Skip(pairs))
				{
					result.WordsAdded += CountWords(added.Text);
					result.Paragraphs.Add(new ProtocolDiffParagraph { IsHeading = added.Heading, Segments = { new ProtocolDiffSegment { Kind = ProtocolDiffKinds.Inserted, Text = added.Text } } });
				}
				pendingDeleted.Clear();
				pendingInserted.Clear();
			}

			foreach (var op in ops)
			{
				switch (op.Kind)
				{
					case ProtocolDiffKinds.Equal:
						Flush();
						result.Paragraphs.Add(new ProtocolDiffParagraph { IsHeading = to[op.ToIndex].Heading, Segments = { new ProtocolDiffSegment { Kind = ProtocolDiffKinds.Equal, Text = to[op.ToIndex].Text } } });
						break;
					case ProtocolDiffKinds.Deleted:
						pendingDeleted.Add((from[op.FromIndex].Text, from[op.FromIndex].Heading));
						break;
					case ProtocolDiffKinds.Inserted:
						pendingInserted.Add((to[op.ToIndex].Text, to[op.ToIndex].Heading));
						break;
				}
			}

			Flush();
			return result;
		}

		private static int CountWords(string text) => string.IsNullOrWhiteSpace(text) ? 0 : Words.Matches(text).Count;

		private static ProtocolDiffParagraph WordDiff(string oldText, string newText, bool heading, ProtocolDiffResult totals)
		{
			var oldWords = Words.Matches(oldText ?? string.Empty).Select(m => m.Value).Take(MaxWordsPerParagraph).ToList();
			var newWords = Words.Matches(newText ?? string.Empty).Select(m => m.Value).Take(MaxWordsPerParagraph).ToList();
			var paragraph = new ProtocolDiffParagraph { IsHeading = heading };

			foreach (var op in Lcs(oldWords, newWords, StringComparer.Ordinal))
			{
				var word = op.Kind == ProtocolDiffKinds.Deleted ? oldWords[op.FromIndex] : newWords[op.ToIndex];
				if (op.Kind == ProtocolDiffKinds.Inserted) totals.WordsAdded++;
				if (op.Kind == ProtocolDiffKinds.Deleted) totals.WordsRemoved++;

				var last = paragraph.Segments.LastOrDefault();
				if (last != null && last.Kind == op.Kind)
					last.Text += " " + word;
				else
					paragraph.Segments.Add(new ProtocolDiffSegment { Kind = op.Kind, Text = word });
			}

			return paragraph;
		}

		private sealed class Op
		{
			public ProtocolDiffKinds Kind;
			public int FromIndex;
			public int ToIndex;
		}

		private static List<Op> Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b, IEqualityComparer<string> comparer)
		{
			var n = a.Count;
			var m = b.Count;
			var table = new int[n + 1, m + 1];
			for (var i = n - 1; i >= 0; i--)
				for (var j = m - 1; j >= 0; j--)
					table[i, j] = comparer.Equals(a[i], b[j]) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);

			var ops = new List<Op>(n + m);
			int x = 0, y = 0;
			while (x < n && y < m)
			{
				if (comparer.Equals(a[x], b[y]))
				{
					ops.Add(new Op { Kind = ProtocolDiffKinds.Equal, FromIndex = x, ToIndex = y });
					x++;
					y++;
				}
				else if (table[x + 1, y] >= table[x, y + 1])
				{
					ops.Add(new Op { Kind = ProtocolDiffKinds.Deleted, FromIndex = x });
					x++;
				}
				else
				{
					ops.Add(new Op { Kind = ProtocolDiffKinds.Inserted, ToIndex = y });
					y++;
				}
			}

			for (; x < n; x++) ops.Add(new Op { Kind = ProtocolDiffKinds.Deleted, FromIndex = x });
			for (; y < m; y++) ops.Add(new Op { Kind = ProtocolDiffKinds.Inserted, ToIndex = y });
			return ops;
		}

		private sealed class Paragraph
		{
			public string Text;
			public bool Heading;
		}

		/// <summary>The HTML as plain-text paragraphs, split at block elements; headings are flagged.</summary>
		private static List<Paragraph> Paragraphs(string html)
		{
			var paragraphs = new List<Paragraph>();
			if (string.IsNullOrWhiteSpace(html))
				return paragraphs;

			string marked;
			try
			{
				// A control character marks where each block starts, and headings get a second marker, before the tags go.
				marked = BlockBreak.Replace(html, m =>
				{
					var isHeadingOpen = m.Value.Length > 2 && m.Value[1] != '/' && (m.Value[1] == 'h' || m.Value[1] == 'H') && char.IsDigit(m.Value[2]);
					return isHeadingOpen ? "\u0002\u0001H" : "\u0002";
				});
			}
			catch (RegexMatchTimeoutException)
			{
				marked = html;
			}

			foreach (var part in marked.Split('\u0002'))
			{
				var heading = HeadingStart.IsMatch(part);
				var text = heading ? part.Substring(2) : part;
				text = WebUtility.HtmlDecode(StringHelpers.HtmlToPlainText(text) ?? string.Empty);
				text = Regex.Replace(text, @"\s+", " ").Trim();
				if (text.Length == 0)
					continue;
				paragraphs.Add(new Paragraph { Text = text, Heading = heading });
				if (paragraphs.Count >= MaxParagraphs)
					break;
			}

			return paragraphs;
		}
	}
}
