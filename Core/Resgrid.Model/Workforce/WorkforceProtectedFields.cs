using System;
using System.Collections.Generic;

namespace Resgrid.Model.Workforce
{
	/// <summary>
	/// ADP catalog 28 (Workforce &amp; Business Operations plan, Phase E; registered with M0220–M0224): every value that
	/// identifies or prices a person — employer / affiliate / contractor identifiers and addresses, external worker keys,
	/// compensation amounts, pay and cost components, approved payroll cost, annual earnings, demographic responses,
	/// report snapshots, aggregate rows, remarks and export files. Personnel family (ViewProtectedPersonnelData).
	/// Hours, dates, codes, counts and run totals are routing metadata. Accessor maps drive the generic RMS seams;
	/// costing and reporting workloads decrypt through the <c>workforce-costing</c> / <c>pay-data-reporting</c> purposes.
	/// </summary>
	public static class WorkforceProtectedFields
	{
		public const int CatalogVersion = 28;
		public const string CostingWorkloadPurpose = "workforce-costing";
		public const string ReportingWorkloadPurpose = "pay-data-reporting";

		private static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Map<T>(params (string Field, Func<T, string> Get, Action<T, string> Set)[] entries)
		{
			var map = new Dictionary<string, (Func<T, string>, Action<T, string>)>(StringComparer.OrdinalIgnoreCase);
			foreach (var (field, get, set) in entries) map[field] = (get, set);
			return map;
		}

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceEmployerProfile, string> Get, Action<WorkforceEmployerProfile, string> Set)> Employer = Map<WorkforceEmployerProfile>(
			("workforceemployerprofiles.fein", e => e.Fein, (e, v) => e.Fein = v),
			("workforceemployerprofiles.sein", e => e.Sein, (e, v) => e.Sein = v),
			("workforceemployerprofiles.sosnumber", e => e.SosNumber, (e, v) => e.SosNumber = v),
			("workforceemployerprofiles.eddaddress", e => e.EddAddress, (e, v) => e.EddAddress = v),
			("workforceemployerprofiles.headquartersaddress", e => e.HeadquartersAddress, (e, v) => e.HeadquartersAddress = v),
			("workforceemployerprofiles.filingcontactname", e => e.FilingContactName, (e, v) => e.FilingContactName = v),
			("workforceemployerprofiles.filingcontactemail", e => e.FilingContactEmail, (e, v) => e.FilingContactEmail = v),
			("workforceemployerprofiles.filingcontactphone", e => e.FilingContactPhone, (e, v) => e.FilingContactPhone = v));

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceAffiliatedEntity, string> Get, Action<WorkforceAffiliatedEntity, string> Set)> Affiliate = Map<WorkforceAffiliatedEntity>(
			("workforceaffiliatedentities.fein", e => e.Fein, (e, v) => e.Fein = v),
			("workforceaffiliatedentities.sein", e => e.Sein, (e, v) => e.Sein = v),
			("workforceaffiliatedentities.sosnumber", e => e.SosNumber, (e, v) => e.SosNumber = v),
			("workforceaffiliatedentities.headquartersaddress", e => e.HeadquartersAddress, (e, v) => e.HeadquartersAddress = v));

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceEstablishment, string> Get, Action<WorkforceEstablishment, string> Set)> Establishment = Map<WorkforceEstablishment>(
			("workforceestablishments.physicaladdress", e => e.PhysicalAddress, (e, v) => e.PhysicalAddress = v));

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceLaborContractor, string> Get, Action<WorkforceLaborContractor, string> Set)> Contractor = Map<WorkforceLaborContractor>(
			("workforcelaborcontractors.fein", e => e.Fein, (e, v) => e.Fein = v),
			("workforcelaborcontractors.contactdetails", e => e.ContactDetails, (e, v) => e.ContactDetails = v));

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceWorker, string> Get, Action<WorkforceWorker, string> Set)> Worker = Map<WorkforceWorker>(
			("workforceworkers.externalworkerkey", e => e.ExternalWorkerKey, (e, v) => e.ExternalWorkerKey = v),
			("workforceworkers.displaylabel", e => e.DisplayLabel, (e, v) => e.DisplayLabel = v));

		public static readonly IReadOnlyDictionary<string, (Func<EmployeeCompensationProfile, string> Get, Action<EmployeeCompensationProfile, string> Set)> Compensation = Map<EmployeeCompensationProfile>(
			("employeecompensationprofiles.baseamount", e => e.BaseAmount, (e, v) => e.BaseAmount = v),
			("employeecompensationprofiles.regularhourlyequivalent", e => e.RegularHourlyEquivalent, (e, v) => e.RegularHourlyEquivalent = v),
			("employeecompensationprofiles.ratemultipliersjson", e => e.RateMultipliersJson, (e, v) => e.RateMultipliersJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<EmployeePayComponent, string> Get, Action<EmployeePayComponent, string> Set)> PayComponent = Map<EmployeePayComponent>(
			("employeepaycomponents.amount", e => e.Amount, (e, v) => e.Amount = v));

		public static readonly IReadOnlyDictionary<string, (Func<EmployeeCostComponent, string> Get, Action<EmployeeCostComponent, string> Set)> CostComponent = Map<EmployeeCostComponent>(
			("employeecostcomponents.rateamount", e => e.RateAmount, (e, v) => e.RateAmount = v),
			("employeecostcomponents.cap", e => e.Cap, (e, v) => e.Cap = v));

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceWorkEntry, string> Get, Action<WorkforceWorkEntry, string> Set)> WorkEntry = Map<WorkforceWorkEntry>(
			("workforceworkentries.approvedpayrollcost", e => e.ApprovedPayrollCost, (e, v) => e.ApprovedPayrollCost = v));

		public static readonly IReadOnlyDictionary<string, (Func<WorkforceAnnualPayFact, string> Get, Action<WorkforceAnnualPayFact, string> Set)> AnnualFact = Map<WorkforceAnnualPayFact>(
			("workforceannualpayfacts.w2box5", e => e.W2Box5, (e, v) => e.W2Box5 = v),
			("workforceannualpayfacts.w2box1", e => e.W2Box1, (e, v) => e.W2Box1 = v),
			("workforceannualpayfacts.earningsused", e => e.EarningsUsed, (e, v) => e.EarningsUsed = v),
			("workforceannualpayfacts.clientallocatedearnings", e => e.ClientAllocatedEarnings, (e, v) => e.ClientAllocatedEarnings = v));

		public static readonly IReadOnlyDictionary<string, (Func<FieldCostLine, string> Get, Action<FieldCostLine, string> Set)> CostLine = Map<FieldCostLine>(
			("fieldcostlines.protecteddetailjson", e => e.ProtectedDetailJson, (e, v) => e.ProtectedDetailJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<PayDataReportingDemographic, string> Get, Action<PayDataReportingDemographic, string> Set)> Demographic = Map<PayDataReportingDemographic>(
			("paydatareportingdemographics.hispaniclatino", e => e.HispanicLatino, (e, v) => e.HispanicLatino = v),
			("paydatareportingdemographics.raceethnicitycodes", e => e.RaceEthnicityCodes, (e, v) => e.RaceEthnicityCodes = v),
			("paydatareportingdemographics.sexcode", e => e.SexCode, (e, v) => e.SexCode = v));

		public static readonly IReadOnlyDictionary<string, (Func<PayDataReportRun, string> Get, Action<PayDataReportRun, string> Set)> ReportRun = Map<PayDataReportRun>(
			("paydatareportruns.employersnapshotjson", e => e.EmployerSnapshotJson, (e, v) => e.EmployerSnapshotJson = v),
			("paydatareportruns.runremarks", e => e.RunRemarks, (e, v) => e.RunRemarks = v));

		public static readonly IReadOnlyDictionary<string, (Func<PayDataReportEmployeeSnapshot, string> Get, Action<PayDataReportEmployeeSnapshot, string> Set)> EmployeeSnapshot = Map<PayDataReportEmployeeSnapshot>(
			("paydatareportemployeesnapshots.demographiccode", e => e.DemographicCode, (e, v) => e.DemographicCode = v),
			("paydatareportemployeesnapshots.annualearnings", e => e.AnnualEarnings, (e, v) => e.AnnualEarnings = v),
			("paydatareportemployeesnapshots.hourlyrate", e => e.HourlyRate, (e, v) => e.HourlyRate = v));

		public static readonly IReadOnlyDictionary<string, (Func<PayDataReportRow, string> Get, Action<PayDataReportRow, string> Set)> ReportRow = Map<PayDataReportRow>(
			("paydatareportrows.demographiccode", e => e.DemographicCode, (e, v) => e.DemographicCode = v),
			("paydatareportrows.meanhourlyrate", e => e.MeanHourlyRate, (e, v) => e.MeanHourlyRate = v),
			("paydatareportrows.medianhourlyrate", e => e.MedianHourlyRate, (e, v) => e.MedianHourlyRate = v),
			("paydatareportrows.rowremarks", e => e.RowRemarks, (e, v) => e.RowRemarks = v));

		public const string ExportDataFieldId = "paydataexportartifacts.data";

		/// <summary>(table, column, binary) for the catalog registration and the table bindings.</summary>
		public static IEnumerable<(string Table, string Column, bool Binary)> All()
		{
			yield return ("WorkforceEmployerProfiles", "Fein", false);
			yield return ("WorkforceEmployerProfiles", "Sein", false);
			yield return ("WorkforceEmployerProfiles", "SosNumber", false);
			yield return ("WorkforceEmployerProfiles", "EddAddress", false);
			yield return ("WorkforceEmployerProfiles", "HeadquartersAddress", false);
			yield return ("WorkforceEmployerProfiles", "FilingContactName", false);
			yield return ("WorkforceEmployerProfiles", "FilingContactEmail", false);
			yield return ("WorkforceEmployerProfiles", "FilingContactPhone", false);
			yield return ("WorkforceAffiliatedEntities", "Fein", false);
			yield return ("WorkforceAffiliatedEntities", "Sein", false);
			yield return ("WorkforceAffiliatedEntities", "SosNumber", false);
			yield return ("WorkforceAffiliatedEntities", "HeadquartersAddress", false);
			yield return ("WorkforceEstablishments", "PhysicalAddress", false);
			yield return ("WorkforceLaborContractors", "Fein", false);
			yield return ("WorkforceLaborContractors", "ContactDetails", false);
			yield return ("WorkforceWorkers", "ExternalWorkerKey", false);
			yield return ("WorkforceWorkers", "DisplayLabel", false);
			yield return ("EmployeeCompensationProfiles", "BaseAmount", false);
			yield return ("EmployeeCompensationProfiles", "RegularHourlyEquivalent", false);
			yield return ("EmployeeCompensationProfiles", "RateMultipliersJson", false);
			yield return ("EmployeePayComponents", "Amount", false);
			yield return ("EmployeeCostComponents", "RateAmount", false);
			yield return ("EmployeeCostComponents", "Cap", false);
			yield return ("WorkforceWorkEntries", "ApprovedPayrollCost", false);
			yield return ("WorkforceAnnualPayFacts", "W2Box5", false);
			yield return ("WorkforceAnnualPayFacts", "W2Box1", false);
			yield return ("WorkforceAnnualPayFacts", "EarningsUsed", false);
			yield return ("WorkforceAnnualPayFacts", "ClientAllocatedEarnings", false);
			yield return ("FieldCostLines", "ProtectedDetailJson", false);
			yield return ("PayDataReportingDemographics", "HispanicLatino", false);
			yield return ("PayDataReportingDemographics", "RaceEthnicityCodes", false);
			yield return ("PayDataReportingDemographics", "SexCode", false);
			yield return ("PayDataReportRuns", "EmployerSnapshotJson", false);
			yield return ("PayDataReportRuns", "RunRemarks", false);
			yield return ("PayDataReportEmployeeSnapshots", "DemographicCode", false);
			yield return ("PayDataReportEmployeeSnapshots", "AnnualEarnings", false);
			yield return ("PayDataReportEmployeeSnapshots", "HourlyRate", false);
			yield return ("PayDataReportRows", "DemographicCode", false);
			yield return ("PayDataReportRows", "MeanHourlyRate", false);
			yield return ("PayDataReportRows", "MedianHourlyRate", false);
			yield return ("PayDataReportRows", "RowRemarks", false);
			yield return ("PayDataExportArtifacts", "Data", true);
		}

		/// <summary>Tables bound directly on DepartmentId with the IsProtected marker (every Phase E table carries one).</summary>
		public static readonly IReadOnlyList<(string Table, string PkColumn)> Tables = new[]
		{
			("WorkforceEmployerProfiles", "WorkforceEmployerProfileId"), ("WorkforceAffiliatedEntities", "WorkforceAffiliatedEntityId"), ("WorkforceEstablishments", "WorkforceEstablishmentId"),
			("WorkforceLaborContractors", "WorkforceLaborContractorId"), ("WorkforceWorkers", "WorkforceWorkerId"),
			("EmployeeCompensationProfiles", "EmployeeCompensationProfileId"), ("EmployeePayComponents", "EmployeePayComponentId"), ("EmployeeCostComponents", "EmployeeCostComponentId"),
			("WorkforceWorkEntries", "WorkforceWorkEntryId"), ("WorkforceAnnualPayFacts", "WorkforceAnnualPayFactId"), ("FieldCostLines", "FieldCostLineId"),
			("PayDataReportingDemographics", "PayDataReportingDemographicId"), ("PayDataReportRuns", "PayDataReportRunId"), ("PayDataReportEmployeeSnapshots", "PayDataReportEmployeeSnapshotId"),
			("PayDataReportRows", "PayDataReportRowId"), ("PayDataExportArtifacts", "PayDataExportArtifactId")
		};
	}
}
