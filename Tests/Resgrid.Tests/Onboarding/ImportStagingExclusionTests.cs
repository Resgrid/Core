using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Excluded data is dropped from staging whether its header or its values give it away (wizard plan section 8.2): a column
	/// of national identifiers under an innocuous header is blanked in every staged row, recorded as IMP-EXCLUDED-SENSITIVE, and
	/// a later slice of the same sheet blanks it from that record.
	/// </summary>
	[TestFixture]
	public class ImportStagingExclusionTests
	{
		private const int DepartmentId = 7;
		private const string DatasetId = "D1";

		private readonly ImportArtifact _artifact = new()
		{
			Id = "A1", DepartmentId = DepartmentId, WorkspaceId = "W1", DetectedFormat = (int)ImportFileFormat.Csv, WrappedDataKey = "wrapped"
		};

		private readonly ImportDataset _dataset = new() { Id = DatasetId, DepartmentId = DepartmentId, WorkspaceId = "W1", ArtifactId = "A1", SheetIndex = 0 };

		private List<ImportStagedRow> _staged;
		private List<ImportIssue> _issues;
		private List<string[]> _sheet;
		private ImportStagingService _service;

		[SetUp]
		public void SetUp()
		{
			_staged = new List<ImportStagedRow>();
			_issues = new List<ImportIssue>();
			_sheet = new List<string[]>();

			// The cipher is the identity here: the tests read the sealed payloads back as JSON.
			var cipher = new Mock<IImportArtifactCipher>();
			cipher.Setup(c => c.EncryptWhole(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>())).Returns((string _, string _, byte[] plain) => plain);
			cipher.Setup(c => c.DecryptWhole(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>())).Returns((string _, string _, byte[] content) => content);

			var keys = new Mock<IImportKeyService>();
			keys.Setup(k => k.UnwrapAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync("data-key");

			var reader = new Mock<IImportFileReader>();
			reader.Setup(r => r.ReadSheet(It.IsAny<Stream>(), It.IsAny<ImportFileFormat>(), It.IsAny<int>(), It.IsAny<ImportReadOptions>(),
					It.IsAny<Action<int, string[]>>(), It.IsAny<CancellationToken>()))
				.Returns((Stream _, ImportFileFormat _, int _, ImportReadOptions _, Action<int, string[]> onRow, CancellationToken _) =>
				{
					for (var i = 0; i < _sheet.Count; i++)
						onRow(i + 1, _sheet[i]);
					return new ImportSheetReadResult { RowsRead = _sheet.Count };
				});

			var rows = new Mock<IImportStagedRowsRepository>();
			rows.Setup(r => r.InsertManyAsync(It.IsAny<IReadOnlyList<ImportStagedRow>>(), It.IsAny<CancellationToken>()))
				.Callback((IReadOnlyList<ImportStagedRow> page, CancellationToken _) =>
				{
					foreach (var row in page)
						row.Id ??= Guid.NewGuid().ToString();
					_staged.AddRange(page);
				})
				.Returns(Task.CompletedTask);
			rows.Setup(r => r.GetPageAsync(DepartmentId, DatasetId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, string _, int after, int take, CancellationToken _) =>
					_staged.Where(s => s.RowNumber > after).OrderBy(s => s.RowNumber).Take(take).ToList());
			rows.Setup(r => r.DeleteForDatasetAsync(DepartmentId, DatasetId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => { var count = _staged.Count; _staged.Clear(); return count; });
			rows.Setup(r => r.DeleteAsync(It.IsAny<ImportStagedRow>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ImportStagedRow row, CancellationToken _) => _staged.RemoveAll(s => s.Id == row.Id) > 0);

			var issues = new Mock<IImportIssuesRepository>();
			issues.Setup(i => i.GetForDatasetAsync(DepartmentId, DatasetId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _issues.ToList());
			issues.Setup(i => i.InsertManyAsync(It.IsAny<IReadOnlyList<ImportIssue>>(), It.IsAny<CancellationToken>()))
				.Callback((IReadOnlyList<ImportIssue> list, CancellationToken _) => _issues.AddRange(list))
				.Returns(Task.CompletedTask);

			_service = new ImportStagingService(Mock.Of<IImportArtifactStore>(), cipher.Object, reader.Object, keys.Object, Mock.Of<IImportDatasetsRepository>(),
				rows.Object, issues.Object);
		}

		private void Sheet(int dataRows)
		{
			_sheet.Add(new[] { "Name", "Employee ref", "Phone" });
			for (var i = 1; i <= dataRows; i++)
				_sheet.Add(new[] { $"Person {i}", $"{100 + i:D3}-{10 + i % 90:D2}-{1000 + i:D4}", $"555 010 {1000 + i:D4}" });
		}

		private Task<(int LastRow, bool Finished, ImportSheetReadResult Read)> Stage(int afterRow = 0) =>
			_service.StageSheetAsync(_artifact, _dataset, Stream.Null, afterRow, 20000, DateTime.UtcNow.AddMinutes(5), null, CancellationToken.None);

		private string[] Cells(int rowNumber) =>
			JsonConvert.DeserializeObject<string[]>(Encoding.UTF8.GetString(Convert.FromBase64String(_staged.Single(s => s.RowNumber == rowNumber).SourcePayload)));

		[Test]
		public async Task Identifier_shaped_values_under_an_innocuous_header_are_never_staged()
		{
			Sheet(5);

			var (lastRow, finished, _) = await Stage();

			finished.Should().BeTrue();
			lastRow.Should().Be(6);
			_staged.Select(s => s.RowNumber).Should().BeEquivalentTo(Enumerable.Range(1, 6));
			Cells(1).Should().Equal("Name", "Employee ref", "Phone");
			foreach (var row in Enumerable.Range(2, 5))
			{
				Cells(row)[1].Should().BeNull($"row {row} holds a national identifier");
				Cells(row)[0].Should().Be($"Person {row - 1}");
				Cells(row)[2].Should().StartWith("555 010 ");
			}

			_issues.Should().ContainSingle(i => i.Code == "IMP-EXCLUDED-SENSITIVE").Which.ColumnName.Should().Be("Employee ref");

			// Profiling has nothing left to judge in the column; the recorded exclusion still marks it.
			var (profile, _, _) = await _service.ProfileAsync(_artifact, _dataset, CancellationToken.None);
			profile.Columns[1].LooksExcluded.Should().BeTrue();
			profile.Columns[1].FilledCount.Should().Be(0);
			profile.Columns[1].Examples.Should().BeEmpty();
			profile.Columns[2].LooksExcluded.Should().BeFalse();
		}

		[Test]
		public async Task Rows_after_the_sample_is_complete_are_blanked_as_they_stream()
		{
			Sheet(120);

			await Stage();

			_staged.Should().HaveCount(121);
			Enumerable.Range(2, 120).Select(row => Cells(row)[1]).Should().AllSatisfy(value => value.Should().BeNull());
			Enumerable.Range(2, 120).Select(row => Cells(row)[2]).Should().AllSatisfy(value => value.Should().NotBeNull());
			_issues.Where(i => i.Code == "IMP-EXCLUDED-SENSITIVE").Should().ContainSingle();
		}

		[Test]
		public async Task A_later_slice_blanks_the_recorded_column_without_judging_it_again()
		{
			Sheet(60);
			await Stage();
			_issues.Should().ContainSingle(i => i.Code == "IMP-EXCLUDED-SENSITIVE");

			// Resuming past row 30 replaces the rows after it; the record, not a fresh sample, blanks the column.
			var (lastRow, finished, _) = await Stage(afterRow: 30);

			finished.Should().BeTrue();
			lastRow.Should().Be(61);
			_staged.Select(s => s.RowNumber).Should().BeEquivalentTo(Enumerable.Range(1, 61));
			Enumerable.Range(2, 60).Select(row => Cells(row)[1]).Should().AllSatisfy(value => value.Should().BeNull());
			_issues.Where(i => i.Code == "IMP-EXCLUDED-SENSITIVE").Should().ContainSingle("the record is not repeated");
		}

		[Test]
		public async Task Ordinary_columns_are_staged_as_read()
		{
			_sheet.Add(new[] { "Name", "Phone" });
			for (var i = 1; i <= 10; i++)
				_sheet.Add(new[] { $"Person {i}", $"555 010 {1000 + i:D4}" });

			await Stage();

			Cells(11).Should().Equal("Person 10", "555 010 1010");
			_issues.Should().BeEmpty();
		}
	}
}
