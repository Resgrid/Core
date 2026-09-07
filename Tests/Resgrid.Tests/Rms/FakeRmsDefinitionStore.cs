using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// In-memory stand-ins for the RMS-1B/1C repositories (definitions, versions, materialized sections/fields, typed values,
	/// saved reports, template packs, jurisdiction profiles, external orders). Only the members the services call are wired.
	/// Pass a <see cref="FakeRmsStore"/> to also wire the record-side lookups the definition services use.
	/// </summary>
	public sealed class FakeRmsDefinitionStore
	{
		public List<RmsRecordDefinition> Definitions { get; } = new List<RmsRecordDefinition>();
		public List<RmsRecordDefinitionVersion> Versions { get; } = new List<RmsRecordDefinitionVersion>();
		public List<RmsRecordSectionDefinition> Sections { get; } = new List<RmsRecordSectionDefinition>();
		public List<RmsRecordFieldDefinition> Fields { get; } = new List<RmsRecordFieldDefinition>();
		public List<RmsRecordValueGroup> Groups { get; } = new List<RmsRecordValueGroup>();
		public List<RmsRecordValue> Values { get; } = new List<RmsRecordValue>();
		public List<RmsSavedReportDefinition> Reports { get; } = new List<RmsSavedReportDefinition>();
		public List<RmsTemplatePackVersion> Packs { get; } = new List<RmsTemplatePackVersion>();
		public List<RmsJurisdictionProfileVersion> Profiles { get; } = new List<RmsJurisdictionProfileVersion>();
		public List<RmsExternalOrder> Orders { get; } = new List<RmsExternalOrder>();
		public List<RmsExternalOrderFill> Fills { get; } = new List<RmsExternalOrderFill>();
		public List<RmsExternalReference> References { get; } = new List<RmsExternalReference>();

		public Mock<IRmsRecordDefinitionsRepository> DefinitionsRepo { get; } = new Mock<IRmsRecordDefinitionsRepository>();
		public Mock<IRmsRecordDefinitionVersionsRepository> VersionsRepo { get; } = new Mock<IRmsRecordDefinitionVersionsRepository>();
		public Mock<IRmsRecordSectionDefinitionsRepository> SectionsRepo { get; } = new Mock<IRmsRecordSectionDefinitionsRepository>();
		public Mock<IRmsRecordFieldDefinitionsRepository> FieldsRepo { get; } = new Mock<IRmsRecordFieldDefinitionsRepository>();
		public Mock<IRmsRecordValueGroupsRepository> GroupsRepo { get; } = new Mock<IRmsRecordValueGroupsRepository>();
		public Mock<IRmsRecordValuesRepository> ValuesRepo { get; } = new Mock<IRmsRecordValuesRepository>();
		public Mock<IRmsSavedReportDefinitionsRepository> ReportsRepo { get; } = new Mock<IRmsSavedReportDefinitionsRepository>();
		public Mock<IRmsTemplatePackVersionsRepository> PacksRepo { get; } = new Mock<IRmsTemplatePackVersionsRepository>();
		public Mock<IRmsJurisdictionProfileVersionsRepository> ProfilesRepo { get; } = new Mock<IRmsJurisdictionProfileVersionsRepository>();
		public Mock<IRmsExternalOrdersRepository> OrdersRepo { get; } = new Mock<IRmsExternalOrdersRepository>();
		public Mock<IRmsExternalOrderFillsRepository> FillsRepo { get; } = new Mock<IRmsExternalOrderFillsRepository>();
		public Mock<IRmsExternalReferencesRepository> ReferencesRepo { get; } = new Mock<IRmsExternalReferencesRepository>();

		public FakeRmsDefinitionStore(FakeRmsStore records = null)
		{
			// Definitions
			DefinitionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordDefinition>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordDefinition e, CancellationToken c, bool f) => { Definitions.Add(e); return e; });
			DefinitionsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordDefinition>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordDefinition e, CancellationToken c, bool f) => { Definitions.RemoveAll(x => x.RmsRecordDefinitionId == e.RmsRecordDefinitionId); Definitions.Add(e); return e; });
			DefinitionsRepo.Setup(r => r.GetByKeyAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string k) => Definitions.FirstOrDefault(x => x.DepartmentId == d && x.DeletedOn == null && string.Equals(x.DefinitionKey, k, StringComparison.OrdinalIgnoreCase)));
			DefinitionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Definitions.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordDefinitionId == id));
			DefinitionsRepo.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, bool retired) => Definitions.Where(x => x.DepartmentId == d && x.DeletedOn == null && (retired || !x.IsRetired)).ToList());
			DefinitionsRepo.Setup(r => r.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long expected, CancellationToken c) => Bump(Definitions.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordDefinitionId == id), expected, (x, v) => x.RowVersion = v, x => x.RowVersion));

			// Versions
			VersionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordDefinitionVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordDefinitionVersion e, CancellationToken c, bool f) => { Versions.Add(e); return e; });
			VersionsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordDefinitionVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordDefinitionVersion e, CancellationToken c, bool f) => { Versions.RemoveAll(x => x.RmsRecordDefinitionVersionId == e.RmsRecordDefinitionVersionId); Versions.Add(e); return e; });
			VersionsRepo.Setup(r => r.DeleteAsync(It.IsAny<RmsRecordDefinitionVersion>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RmsRecordDefinitionVersion e, CancellationToken c) => Versions.RemoveAll(x => x.RmsRecordDefinitionVersionId == e.RmsRecordDefinitionVersionId) > 0);
			VersionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Versions.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == id));
			VersionsRepo.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()))
				.ReturnsAsync((int d, string k, int v) => Versions.FirstOrDefault(x => x.DepartmentId == d && string.Equals(x.DefinitionKey, k, StringComparison.OrdinalIgnoreCase) && x.Version == v));
			VersionsRepo.Setup(r => r.GetForDefinitionAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string k) => Versions.Where(x => x.DepartmentId == d && string.Equals(x.DefinitionKey, k, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Version).ToList());
			VersionsRepo.Setup(r => r.GetPublishedForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Versions.Where(x => x.DepartmentId == d && x.IsPublished
					&& Definitions.Any(def => def.DepartmentId == d && def.DefinitionKey == x.DefinitionKey && def.CurrentPublishedVersion == x.Version && !def.IsRetired && def.DeletedOn == null)).ToList());
			VersionsRepo.Setup(r => r.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long expected, CancellationToken c) => Bump(Versions.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == id), expected, (x, v) => x.RowVersion = v, x => x.RowVersion));

			// Materialized sections / fields
			SectionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordSectionDefinition>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordSectionDefinition e, CancellationToken c, bool f) => { Sections.Add(e); return e; });
			SectionsRepo.Setup(r => r.GetForVersionAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string v) => Sections.Where(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == v).OrderBy(x => x.Ordinal).ToList());
			SectionsRepo.Setup(r => r.DeleteForVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string v, CancellationToken c) => Sections.RemoveAll(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == v));
			FieldsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordFieldDefinition>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordFieldDefinition e, CancellationToken c, bool f) => { Fields.Add(e); return e; });
			FieldsRepo.Setup(r => r.GetForVersionAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string v) => Fields.Where(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == v).OrderBy(x => x.Ordinal).ToList());
			FieldsRepo.Setup(r => r.DeleteForVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string v, CancellationToken c) => Fields.RemoveAll(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == v));

			// Typed values
			GroupsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordValueGroup>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordValueGroup e, CancellationToken c, bool f) => { Groups.Add(e); return e; });
			GroupsRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id, string rev) => Groups.Where(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == rev).OrderBy(x => x.Ordinal).ToList());
			GroupsRepo.Setup(r => r.GetForRecordsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, IEnumerable<string> ids, bool drafts) => { var set = ids.ToList(); return Groups.Where(x => x.DepartmentId == d && set.Contains(x.RecordId) && (!drafts || x.RevisionId == null)).ToList(); });
			GroupsRepo.Setup(r => r.GetForRevisionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToList(); return Groups.Where(x => x.DepartmentId == d && x.RevisionId != null && set.Contains(x.RevisionId)).ToList(); });
			GroupsRepo.Setup(r => r.DeleteDraftForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken c) => Groups.RemoveAll(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == null));
			ValuesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordValue>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordValue e, CancellationToken c, bool f) => { Values.Add(e); return e; });
			ValuesRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id, string rev) => Values.Where(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == rev).ToList());
			ValuesRepo.Setup(r => r.GetForRecordsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, IEnumerable<string> ids, bool drafts) => { var set = ids.ToList(); return Values.Where(x => x.DepartmentId == d && set.Contains(x.RecordId) && (!drafts || x.RevisionId == null)).ToList(); });
			ValuesRepo.Setup(r => r.GetForRevisionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToList(); return Values.Where(x => x.DepartmentId == d && x.RevisionId != null && set.Contains(x.RevisionId)).ToList(); });
			ValuesRepo.Setup(r => r.DeleteDraftForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken c) => Values.RemoveAll(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == null));
			ValuesRepo.Setup(r => r.CountRecordsOnVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, string v, bool drafts) => Values.Where(x => x.DepartmentId == d && x.RmsRecordDefinitionVersionId == v && (!drafts || x.RevisionId == null)).Select(x => x.RecordId).Distinct().Count());

			// Saved reports
			ReportsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsSavedReportDefinition>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSavedReportDefinition e, CancellationToken c, bool f) => { Reports.Add(e); return e; });
			ReportsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsSavedReportDefinition>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSavedReportDefinition e, CancellationToken c, bool f) => { Reports.RemoveAll(x => x.RmsSavedReportDefinitionId == e.RmsSavedReportDefinitionId); Reports.Add(e); return e; });
			ReportsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Reports.FirstOrDefault(x => x.DepartmentId == d && x.RmsSavedReportDefinitionId == id && x.DeletedOn == null));
			ReportsRepo.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Reports.Where(x => x.DepartmentId == d && x.DeletedOn == null).ToList());

			// Product catalog mirrors
			PacksRepo.Setup(r => r.SaveOrUpdateAsync(It.IsAny<RmsTemplatePackVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsTemplatePackVersion e, CancellationToken c, bool f) => { Packs.RemoveAll(x => x.PackKey == e.PackKey && x.Version == e.Version); Packs.Add(e); return e; });
			PacksRepo.Setup(r => r.GetCatalogAsync()).ReturnsAsync(() => Packs.ToList());
			PacksRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((string k, int v) => Packs.FirstOrDefault(x => x.PackKey == k && x.Version == v));
			ProfilesRepo.Setup(r => r.SaveOrUpdateAsync(It.IsAny<RmsJurisdictionProfileVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsJurisdictionProfileVersion e, CancellationToken c, bool f) => { Profiles.RemoveAll(x => x.ProfileKey == e.ProfileKey && x.Version == e.Version); Profiles.Add(e); return e; });
			ProfilesRepo.Setup(r => r.GetCatalogAsync()).ReturnsAsync(() => Profiles.ToList());
			ProfilesRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((string k, int v) => Profiles.FirstOrDefault(x => x.ProfileKey == k && x.Version == v));
			ProfilesRepo.Setup(r => r.GetLatestAsync(It.IsAny<string>())).ReturnsAsync((string k) => Profiles.Where(x => x.ProfileKey == k).OrderByDescending(x => x.Version).FirstOrDefault());

			// External orders
			OrdersRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExternalOrder>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalOrder e, CancellationToken c, bool f) => { Orders.Add(e); return e; });
			OrdersRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsExternalOrder>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalOrder e, CancellationToken c, bool f) => { Orders.RemoveAll(x => x.RmsExternalOrderId == e.RmsExternalOrderId); Orders.Add(e); return e; });
			OrdersRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, string id, bool a) => Orders.FirstOrDefault(x => x.DepartmentId == d && x.RmsExternalOrderId == id));
			OrdersRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Orders.FirstOrDefault(x => x.DepartmentId == d && x.RecordId == id));
			OrdersRepo.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, bool closed) => Orders.Where(x => x.DepartmentId == d && (closed || x.Status != (int)RmsExternalOrderStatus.ClosedOut)).ToList());
			OrdersRepo.Setup(r => r.GetArtifactAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Orders.FirstOrDefault(x => x.DepartmentId == d && x.RmsExternalOrderId == id)?.ArtifactData);
			FillsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExternalOrderFill>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalOrderFill e, CancellationToken c, bool f) => { Fills.Add(e); return e; });
			FillsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsExternalOrderFill>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalOrderFill e, CancellationToken c, bool f) => { Fills.RemoveAll(x => x.RmsExternalOrderFillId == e.RmsExternalOrderFillId); Fills.Add(e); return e; });
			FillsRepo.Setup(r => r.GetForOrderAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Fills.Where(x => x.DepartmentId == d && x.RmsExternalOrderId == id).ToList());
			FillsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Fills.FirstOrDefault(x => x.DepartmentId == d && x.RmsExternalOrderFillId == id));
			ReferencesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExternalReference>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalReference e, CancellationToken c, bool f) => { References.Add(e); return e; });

			if (records != null)
			{
				records.RecordsRepo.Setup(r => r.GetByDefinitionVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
					.ReturnsAsync((int d, string k, int v, IEnumerable<int> states) => { var set = states?.ToList(); return records.Records.Where(x => x.DepartmentId == d && x.DeletedOn == null && string.Equals(x.DefinitionKey, k, StringComparison.OrdinalIgnoreCase) && x.DefinitionVersion == v && (set == null || set.Count == 0 || set.Contains(x.State))).ToList(); });
				records.RecordsRepo.Setup(r => r.GetByIdsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
					.ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToList(); return records.Records.Where(x => x.DepartmentId == d && set.Contains(x.RmsOperationalRecordId)).ToList(); });
				records.ProjectionsRepo.Setup(r => r.QueryAsync(It.IsAny<int>(), It.IsAny<RmsRecordQuery>()))
					.ReturnsAsync((int d, RmsRecordQuery q) => records.Projections.Where(p => p.DepartmentId == d
						&& (string.IsNullOrEmpty(q.DefinitionKey) || string.Equals(p.DefinitionKey, q.DefinitionKey, StringComparison.OrdinalIgnoreCase))
						&& (q.States == null || q.States.Count == 0 || q.States.Contains(p.State)))
						.OrderByDescending(p => p.RecordCreatedOn).Skip(q.Skip).Take(q.Take <= 0 ? int.MaxValue : q.Take).ToList());
			}
		}

		private static bool Bump<T>(T row, long expected, Action<T, long> set, Func<T, long> get) where T : class
		{
			if (row == null || get(row) != expected) return false;
			set(row, expected + 1);
			return true;
		}
	}
}
