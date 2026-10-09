using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	/// <summary>Department -> Document Numbering (setting 117): work order, invoice, bid and daily time report numbers.</summary>
	public class DocumentNumberingView : BaseUserModel
	{
		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		/// <summary>The day the numbering year starts (January 1 unless documents follow a fiscal year) and which year names it.</summary>
		public int YearStartMonth { get; set; }
		public int YearStartDay { get; set; }
		public int YearLabel { get; set; }

		/// <summary>Department-local now, for the pattern previews.</summary>
		public DateTime PreviewDate { get; set; }

		public List<DocumentNumberRow> Rows { get; set; } = new List<DocumentNumberRow>();
	}

	/// <summary>
	/// One document kind on a numbering screen (Document Numbering, or Records Settings for the Records kinds): the pattern the
	/// department set (blank = the built-in numbers), and for a custom pattern what it issues next and a raise of that.
	/// </summary>
	public class DocumentNumberRow
	{
		public string Kind { get; set; }
		public string Label { get; set; }

		/// <summary>The department's pattern; blank keeps the built-in numbers.</summary>
		public string Pattern { get; set; }
		public int SequenceWidth { get; set; }

		public string LegacyPattern { get; set; }
		public int LegacyWidth { get; set; }
		public int MaxLength { get; set; }

		public bool Custom { get; set; }
		public CallNumberResetPeriod Period { get; set; }

		/// <summary>The sequence the next number was shown for; a raise against any other is not applied.</summary>
		public string ScopeKey { get; set; }
		public string NextNumber { get; set; }
		public int CurrentNextSequence { get; set; }
		public string Example { get; set; }

		/// <summary>The sequence to raise the next number to; blank leaves it.</summary>
		public int? RaiseTo { get; set; }

		public static DocumentNumberRow From(DocumentNumberStatus status, string label)
		{
			var kind = DocumentNumberKinds.Find(status.Kind);
			return new DocumentNumberRow
			{
				Kind = status.Kind,
				Label = label,
				Pattern = status.Custom ? status.Pattern : null,
				SequenceWidth = status.Custom ? status.SequenceWidth : kind?.LegacyWidth ?? CallNumberFormat.DefaultWidth,
				LegacyPattern = kind?.LegacyPattern,
				LegacyWidth = kind?.LegacyWidth ?? CallNumberFormat.DefaultWidth,
				MaxLength = kind?.MaxLength ?? 50,
				Custom = status.Custom,
				Period = status.Period,
				ScopeKey = status.ScopeKey,
				NextNumber = status.NextNumber,
				CurrentNextSequence = status.NextSequence,
				Example = status.Example
			};
		}

		public DocumentNumberPatternUpdate ToUpdate()
		{
			return new DocumentNumberPatternUpdate { Kind = Kind, Pattern = Pattern, SequenceWidth = SequenceWidth, ScopeKey = ScopeKey, NextSequence = RaiseTo };
		}
	}
}
