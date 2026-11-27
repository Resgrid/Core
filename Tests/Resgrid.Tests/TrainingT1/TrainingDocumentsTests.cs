using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>
	/// The printable Training T1 documents: the certificate says who issued it and that Resgrid is not an accreditor, prints a
	/// verification block only for the LMS variant, encodes every value; custom templates fill placeholders with encoded values
	/// in one pass; the transcript counts only counted lines; CSVs are formula-safe; the hours report shows open duplicates.
	/// </summary>
	[TestFixture]
	public class TrainingDocumentsTests
	{
		private const string NotAccreditor = "Resgrid is not a continuing-education accreditor.";

		private static TrainingDocumentContext Context(Dictionary<string, string> labels = null, bool rtl = false) => new TrainingDocumentContext
		{
			DepartmentName = "Station 9 Fire",
			Labels = labels ?? new Dictionary<string, string>(),
			Culture = rtl ? "ar" : "en",
			RightToLeft = rtl,
			GeneratedOnUtc = new DateTime(2025, 6, 1, 15, 0, 0, DateTimeKind.Utc),
			GeneratedBy = "Officer Grey"
		};

		private static CertificateDocument Certificate(string member = "Jane Doe") => new CertificateDocument
		{
			Context = Context(),
			MemberName = member,
			Course = "Vehicle Extrication",
			CourseCode = "FF-EXT-1",
			CompletedOn = new DateTime(2025, 3, 14, 9, 30, 0),
			Hours = 3.5m,
			CeHours = 2m,
			IssuedBy = "Station 9 Fire · Capt. Lee"
		};

		[Test]
		public void LabelKeys_are_distinct_and_every_one_has_an_english_fallback()
		{
			TrainingDocuments.LabelKeys.Should().OnlyHaveUniqueItems();
			TrainingDocuments.LabelKeys.Should().Contain(new[] { "Doc.Transcript", "Doc.NotAccreditor", "Doc.GeneratedOn", "Doc.Level", "Doc.UnresolvedDuplicates" });

			// With no labels at all, nothing prints a raw resource key.
			var context = Context();
			var outputs = new[]
			{
				TrainingDocuments.CertificateHtml(Certificate()),
				TrainingDocuments.TranscriptHtml(new TranscriptDocument { Context = context, MemberName = "Jane", Lines = { new TranscriptLine { Title = "A", Hours = 1, Counts = false } } }),
				TrainingDocuments.TranscriptCsv(new TranscriptDocument { Context = context, MemberName = "Jane" }),
				TrainingDocuments.HoursReportHtml(new HoursReportDocument { Context = context, Report = new TrainingHoursReport { UnresolvedDuplicates = 1, PendingRecords = 1 } }),
				TrainingDocuments.HoursReportCsv(new HoursReportDocument { Context = context, Report = new TrainingHoursReport() }),
				TrainingDocuments.IsoFsrsReportHtml(new IsoFsrsReport(), context),
				TrainingDocuments.NccpProgressHtml(new NccpProgressReport(), context),
				TrainingDocuments.NccpProgressCsv(new NccpProgressReport(), context)
			};
			foreach (var output in outputs)
				output.Should().NotContain("Doc.");
		}

		[Test]
		public void Certificate_names_member_course_date_issuer_and_says_resgrid_is_not_an_accreditor()
		{
			var html = TrainingDocuments.CertificateHtml(Certificate());

			html.Should().StartWith("<!DOCTYPE html>");
			html.Should().Contain("Jane Doe");
			html.Should().Contain("Vehicle Extrication");
			html.Should().Contain("FF-EXT-1");
			html.Should().Contain("2025-03-14");
			html.Should().Contain("Station 9 Fire");
			html.Should().Contain("Capt. Lee");
			html.Should().Contain(">3.5<");
			html.Should().Contain(NotAccreditor);
			html.Should().Contain("Certificate of completion");
			html.Should().NotContain("Verification code");
			html.Should().NotContain("<img");
			html.Should().NotContain("<script");
		}

		[Test]
		public void Certificate_without_an_issuer_is_issued_by_the_department()
		{
			var document = Certificate();
			document.IssuedBy = null;

			TrainingDocuments.CertificateHtml(document).Should().Contain("Issued by: <strong>Station 9 Fire</strong>");
		}

		[Test]
		public void Lms_certificate_prints_verification_code_url_and_qr()
		{
			var document = Certificate();
			document.VerificationCode = "ABCD-1234";
			document.VerificationUrl = "https://resgrid.example/verify/certificate/ABCD-1234";
			document.QrImageDataUrl = "data:image/png;base64,iVBORw0KGgo=";

			var html = TrainingDocuments.CertificateHtml(document);

			html.Should().Contain("Verification code");
			html.Should().Contain("ABCD-1234");
			html.Should().Contain("https://resgrid.example/verify/certificate/ABCD-1234");
			html.Should().Contain("<img class=\"qr\" alt=\"QR\" src=\"data:image/png;base64,iVBORw0KGgo=\"/>");
			html.Should().Contain(NotAccreditor);
		}

		[TestCase("https://evil.example/qr.png")]
		[TestCase("data:image/svg+xml;base64,PHN2Zz4=")]
		[TestCase("data:image/png;base64,abc\"onerror=\"x")]
		public void Qr_is_printed_only_for_a_clean_png_data_url(string qr)
		{
			var document = Certificate();
			document.VerificationCode = "ABCD-1234";
			document.QrImageDataUrl = qr;

			var html = TrainingDocuments.CertificateHtml(document);

			html.Should().Contain("ABCD-1234");
			html.Should().NotContain("<img");
		}

		[Test]
		public void Certificate_values_are_html_encoded()
		{
			var document = Certificate("<script>alert(1)</script>");
			document.Course = "Fire & \"Rescue\"";
			document.Context.DepartmentName = "<b>Dept</b>";

			var html = TrainingDocuments.CertificateHtml(document);

			html.Should().NotContain("<script>");
			html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
			html.Should().Contain("Fire &amp; &quot;Rescue&quot;");
			html.Should().NotContain("<b>Dept</b>");
			html.Should().Contain("&lt;b&gt;Dept&lt;/b&gt;");
		}

		[Test]
		public void Localized_labels_are_used_and_right_to_left_is_marked()
		{
			var document = Certificate();
			document.Context = Context(new Dictionary<string, string>
			{
				["Doc.CertificateTitle"] = "شهادة إتمام",
				["Doc.HasCompleted"] = "Doc.HasCompleted"
			}, rtl: true);

			var html = TrainingDocuments.CertificateHtml(document);

			html.Should().Contain("dir=\"rtl\"");
			html.Should().Contain("lang=\"ar\"");
			html.Should().Contain("شهادة إتمام");
			// A label that came back as its own key (missing resource) prints the English text instead.
			html.Should().Contain("has completed");
			html.Should().NotContain("Doc.HasCompleted");
		}

		[Test]
		public void Custom_template_fills_placeholders_with_encoded_values_in_one_pass()
		{
			var document = Certificate("<b>Jo</b>");
			document.Course = "{{member}}";
			const string template = "<div class=\"x\">{{member}} | {{ Course }} | {{code}} | {{date}} | {{hours}} | {{cehours}} | {{department}} | {{issuedby}} | [{{verificationcode}}] [{{verifyurl}}] [{{qr}}] {{unknown}}</div>";

			var html = TrainingDocuments.CustomCertificateHtml(template, document);

			html.Should().StartWith("<!DOCTYPE html>");
			// WebUtility.HtmlEncode writes the middle dot (U+00B7) as a numeric reference.
			html.Should().Contain("<div class=\"x\">&lt;b&gt;Jo&lt;/b&gt; | {{member}} | FF-EXT-1 | 2025-03-14 | 3.5 | 2 | Station 9 Fire | Station 9 Fire &#183; Capt. Lee | [] [] [] {{unknown}}</div>");
			html.Should().Contain(NotAccreditor);
		}

		[Test]
		public void Custom_template_prints_verification_placeholders_for_the_lms_variant()
		{
			var document = Certificate();
			document.VerificationCode = "ZX-9";
			document.VerificationUrl = "https://resgrid.example/verify/certificate/ZX-9?a=1&b=2";
			document.QrImageDataUrl = "data:image/png;base64,iVBORw0KGgo=";

			var html = TrainingDocuments.CustomCertificateHtml("<p>{{verificationcode}} {{verifyurl}} {{qr}}</p>", document);

			html.Should().Contain("<p>ZX-9 https://resgrid.example/verify/certificate/ZX-9?a=1&amp;b=2 <img class=\"qr\"");
		}

		[Test]
		public void Transcript_marks_not_counted_lines_and_leaves_them_out_of_the_totals()
		{
			var document = new TranscriptDocument
			{
				Context = Context(),
				MemberName = "Jane Doe",
				MemberIdentifier = "E100",
				Lines =
				{
					new TranscriptLine { CompletedOn = new DateTime(2025, 2, 1), Title = "Pump ops", Category = "Company", Source = "Import", Status = "Verified", Hours = 2m, Counts = true },
					new TranscriptLine { CompletedOn = new DateTime(2025, 2, 2), Title = "Rejected class", Category = "Company", Source = "Submission", Status = "Rejected", Hours = 5m, Counts = false },
					new TranscriptLine { CompletedOn = new DateTime(2025, 2, 3), Title = "Ladders", Category = null, Source = "Import", Status = "Verified", Hours = 1.5m, Counts = true,
						CeProvider = "State Academy", CeCourseNumber = "CE-1" }
				}
			};

			var html = TrainingDocuments.TranscriptHtml(document);

			html.Should().Contain("Jane Doe (E100)");
			html.Should().Contain("Rejected class");
			html.Should().Contain("<span class=\"tag\">Not counted</span>");
			html.Should().Contain("<tr class=\"muted\">");
			html.Should().Contain("<tr class=\"total\"><td>Total hours</td><td class=\"num\">3.5</td></tr>");
			html.Should().Contain("<td>Company</td><td class=\"num\">2</td>");
			html.Should().Contain("<td>Uncategorized</td><td class=\"num\">1.5</td>");
			html.Should().Contain("CE provider: State Academy &#183; CE course number: CE-1");
			html.Should().NotContain("8.5");
			html.Should().Contain("Generated 2025-06-01 15:00 by Officer Grey");
		}

		[Test]
		public void Transcript_period_is_printed_in_department_dates()
		{
			var document = new TranscriptDocument
			{
				Context = Context(),
				MemberName = "Jane Doe",
				// Midnight Pacific (UTC-8) on 2025-01-01, and the exclusive end: midnight on 2026-01-01.
				FromUtc = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc),
				ToUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)
			};
			document.Context.TimeZone = "Pacific Standard Time";

			TrainingDocuments.TranscriptHtml(document).Should().Contain("2025-01-01 – 2025-12-31");
		}

		[Test]
		public void Documents_are_self_contained()
		{
			var html = TrainingDocuments.TranscriptHtml(new TranscriptDocument { Context = Context(), MemberName = "Jane" });

			html.Should().Contain("<style>");
			html.Should().NotContain("<script");
			html.Should().NotContain("<link");
			html.Should().NotContain("src=\"http");
			html.Should().NotContain("url(");
		}

		[Test]
		public void Transcript_csv_has_a_header_row_crlf_line_ends_and_neutralises_formulas()
		{
			var document = new TranscriptDocument
			{
				Context = Context(),
				MemberName = "@Jane",
				Lines =
				{
					new TranscriptLine { CompletedOn = new DateTime(2025, 2, 1), Title = "=HYPERLINK(\"http://x\")", Hours = 1.25m, Counts = true },
					new TranscriptLine { CompletedOn = new DateTime(2025, 2, 2), Title = "-2+3", Hours = 4m, Counts = false }
				}
			};

			var csv = TrainingDocuments.TranscriptCsv(document);
			var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);

			lines.Should().HaveCount(4);
			lines[3].Should().BeEmpty();
			lines[0].Should().StartWith("\"Member\",\"Date\",\"Course\"");
			lines[1].Should().Contain("\"'@Jane\",\"2025-02-01\",\"'=HYPERLINK(\"\"http://x\"\")\"");
			lines[1].Should().Contain("\"1.25\"");
			lines[2].Should().Contain("\"'-2+3\"");
			lines[2].Should().EndWith("\"Not counted\"");
			csv.Replace("\r\n", string.Empty).Should().NotContain("\n");
		}

		[Test]
		public void Hours_report_shows_unresolved_duplicates_and_pending_records_beside_the_total()
		{
			var document = new HoursReportDocument
			{
				Context = Context(),
				GroupTitle = "Category",
				Report = new TrainingHoursReport
				{
					Query = new TrainingHoursReportQuery { From = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), To = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc) },
					Rows =
					{
						new TrainingHoursReportRow { Key = "3", Label = "Company <training>", Records = 4, Members = 2, Hours = 12.5m, TargetHours = 192m },
						new TrainingHoursReportRow { Key = "u1", Label = "Bob Smith", Records = 1, Members = 1, Hours = 2m, IsFormerMember = true },
						new TrainingHoursReportRow { Key = "", Label = null, Records = 1, Members = 1, Hours = 1m }
					},
					TotalHours = 15.5m,
					TotalRecords = 6,
					Members = 3,
					UnresolvedDuplicates = 4,
					PendingRecords = 2
				}
			};

			var html = TrainingDocuments.HoursReportHtml(document);

			html.Should().Contain("Training hours report");
			html.Should().Contain("2025-01-01 – 2025-06-30");
			html.Should().Contain("Company &lt;training&gt;");
			html.Should().Contain("<span class=\"tag\">Former member</span>");
			html.Should().Contain("<td>Uncategorized</td>");
			html.Should().Contain(">15.5<");
			html.Should().Contain("Unresolved possible duplicates: 4");
			html.Should().Contain("Pending records: 2");

			var csv = TrainingDocuments.HoursReportCsv(document);
			csv.Should().StartWith("\"Category\",\"Records\",\"Members\",\"Hours\",\"CE hours\",\"Target\",\"Former member\"\r\n");
			csv.Should().Contain("\"Total hours\",\"6\",\"3\",\"15.5\"");
			csv.Should().Contain("\"Unresolved possible duplicates: 4\"");
		}

		[Test]
		public void Iso_report_has_one_column_per_category_code_with_its_target()
		{
			var report = new IsoFsrsReport
			{
				From = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				To = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				Categories =
				{
					new TrainingCategory { TrainingCategoryId = 1, Code = "ISO-CO", Name = "Company training", TargetHours = 192m, SortOrder = 1 },
					new TrainingCategory { TrainingCategoryId = 2, Code = "ISO-HAZ", Name = "Hazardous materials training", TargetHours = 6m, SortOrder = 2 }
				},
				Rows =
				{
					new IsoFsrsReportRow { UserId = "u1", Name = "Jane Doe", HoursByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["iso-co"] = 20.5m, ["ISO-HAZ"] = 6m } }
				}
			};

			var csv = TrainingDocuments.IsoFsrsReportCsv(report, Context());
			csv.Should().StartWith("\"Member\",\"ISO-CO (Target 192)\",\"ISO-HAZ (Target 6)\",\"Total hours\"\r\n");
			csv.Should().Contain("\"Jane Doe\",\"20.5\",\"6\",\"26.5\"\r\n");

			var html = TrainingDocuments.IsoFsrsReportHtml(report, Context());
			html.Should().Contain("ISO FSRS training report");
			html.Should().Contain("<td class=\"num met\">6</td>");
		}

		[Test]
		public void Nccp_report_prints_each_component_against_the_level_target()
		{
			var report = new NccpProgressReport
			{
				CategoriesApplied = true,
				Rows =
				{
					new NccpProgressRow
					{
						UserId = "u1", Name = "Jane Doe", CertificationName = "NREMT Paramedic", CycleStart = new DateTime(2024, 4, 1), CycleEnd = new DateTime(2026, 3, 31),
						Target = TrainingCategoryTemplateCatalog.NccpTargets[0], National = 31m, Local = 7.5m, Individual = 0m
					}
				}
			};

			var csv = TrainingDocuments.NccpProgressCsv(report, Context());
			csv.Should().StartWith("\"Member\",\"Level\",\"Cycle\",\"National\",\"National (Target)\",\"Local\",\"Local (Target)\",\"Individual\",\"Individual (Target)\"\r\n");
			csv.Should().Contain("\"Jane Doe\",\"Paramedic · NREMT Paramedic\",\"2024-04-01 – 2026-03-31\",\"31\",\"30\",\"7.5\",\"15\",\"0\",\"15\"");

			var html = TrainingDocuments.NccpProgressHtml(report, Context());
			html.Should().Contain("NREMT NCCP progress");
			html.Should().Contain("<td class=\"num met\">31 / 30</td>");
			html.Should().Contain("<td class=\"num\">7.5 / 15</td>");
		}
	}
}
