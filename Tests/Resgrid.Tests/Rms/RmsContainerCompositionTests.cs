using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Autofac container-composition test for every new Records registration (RMS plan section 7). A
	/// missing registration fails here at test time rather than at application start.
	/// </summary>
	[TestFixture]
	public class RmsContainerCompositionTests : TestBase
	{
		[Test]
		public void Records_services_resolve_from_the_container()
		{
			Resolve<IRecordsCutoverService>().Should().NotBeNull();
			Resolve<IDomainEventOutboxService>().Should().NotBeNull();
			Resolve<IRecordsAuthorizationService>().Should().NotBeNull();
			Resolve<IRecordsService>().Should().NotBeNull();
			Resolve<IRmsRecordValueService>().Should().NotBeNull();
			Resolve<IRmsInventoryUsageAdapter>().Should().NotBeNull();
			Resolve<IRecordsAccountabilityService>().Should().NotBeNull();
			// RMS-2 NERIS boundary
			Resolve<Resgrid.Model.Providers.INerisProfileService>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.INerisMappingService>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.INerisValidationService>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.INerisApiClient>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.INerisSubmissionService>().Should().NotBeNull();
			Resolve<IIncidentReportsService>().Should().NotBeNull();
			Resolve<IRecordsSubmissionService>().Should().NotBeNull();
			Resolve<IRecordsApiStateStore>().Should().NotBeNull();
			Resolve<IRecordsApiIdempotencyService>().Should().NotBeNull();
			Resolve<IRecordAttachmentUploadService>().Should().NotBeNull();
			Resolve<IRecordsNotificationService>().Should().NotBeNull();
			Resolve<IRecordsSearchService>().Should().NotBeNull();
			Resolve<IRecordsSearchIndexer>().Should().NotBeNull();
			Resolve<IRecordsSearchIndexMaintenanceService>().Should().NotBeNull();
			Resolve<IRecordsReportingService>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.IRecordAttachmentScanner>().Should().BeOfType<Resgrid.Providers.Scanning.ClamAvAttachmentScanner>("the scanning module replaces the null scanner");
			Resolve<IDepartmentProfileMediaService>().Should().NotBeNull();
			Resolve<IRecordsPrintLayoutService>().Should().NotBeNull();
			// RMS-3: worker 42 and worker 43
			Resolve<IRecordsDueStateService>().Should().NotBeNull();
			Resolve<IRecordsRetentionService>().Should().NotBeNull();
			Resolve<IIncidentAnalysisService>().Should().NotBeNull();
			// RMS-3c/3d
			Resolve<IRecordsEvidenceService>().Should().NotBeNull();
			Resolve<IRecordEvidenceSelectionService>().Should().NotBeNull();
			Resolve<IRecordsDisclosureService>().Should().NotBeNull();
			Resolve<IRecordsDashboardService>().Should().NotBeNull();
			// RMS-3 feeds, the read-only NFIRS crosswalk and the RecordOperationalSummaryV1 contract
			Resolve<IIncidentSourceFeedService>().Should().NotBeNull();
			Resolve<IRecordsNfirsLegacyService>().Should().NotBeNull();
			Resolve<IRecordOperationalSummaryService>().Should().NotBeNull();
			// RMS-3e (2026-09-05): ADP seam, ambient grant context and department report exports
			Resolve<IProtectedGrantContext>().Should().NotBeNull();
			// RMS-5 prevention + investigations, RMS-4 quality review and release telemetry (2026-09-07)
			Resolve<Resgrid.Services.Records.RecordsPreventionGate>().Should().NotBeNull();
			Resolve<IRecordsOccupancyService>().Should().NotBeNull();
			Resolve<IContactPreplanOwnershipGate>().Should().BeOfType<Resgrid.Services.Records.RecordsOccupancyService>("Contacts asks the occupancy service who owns structure writes");
			Resolve<IRecordsInspectionsService>().Should().NotBeNull();
			Resolve<IRecordsHydrantsService>().Should().NotBeNull();
			Resolve<IRecordsPermitsService>().Should().NotBeNull();
			Resolve<IRecordsCrrService>().Should().NotBeNull();
			Resolve<IRecordsPreventionAttachmentsService>().Should().NotBeNull();
			Resolve<IRecordsInvestigationsService>().Should().NotBeNull();
			Resolve<IRecordsQualityReviewService>().Should().NotBeNull();
			Resolve<IRecordsReleaseTelemetryService>().Should().NotBeNull();
			Resolve<IRecordsPreventionSweepService>().Should().NotBeNull();
			Resolve<IRecordsProtectionService>().Should().NotBeNull();
			Resolve<IRecordsProtectedReadService>().Should().NotBeNull();
			Resolve<IRecordsExportService>().Should().NotBeNull();
			Resolve<IRmsExportTemplatesRepository>().Should().NotBeNull();
			Resolve<IRmsExportRunsRepository>().Should().NotBeNull();
			// RMS-1B/1C (2026-09-06): configurable definitions, typed values, saved reports, template packs, deployments, reveal
			Resolve<IRecordDefinitionsService>().Should().NotBeNull();
			Resolve<IRecordTypedValuesService>().Should().NotBeNull();
			Resolve<IRecordSavedReportsService>().Should().NotBeNull();
			Resolve<IRecordTemplatePacksService>().Should().NotBeNull();
			Resolve<IRecordDeploymentsService>().Should().NotBeNull();
			Resolve<IRecordsBulkPacketService>().Should().NotBeNull();
			// RMS-1D Field Records
			Resolve<IFieldRecordsService>().Should().NotBeNull();
			Resolve<IRecordWorkAssignmentsService>().Should().NotBeNull();
			Resolve<IRmsRecordWorkAssignmentsRepository>().Should().NotBeNull();
			Resolve<IRecordsFieldRolloutService>().Should().NotBeNull();
			Resolve<IRmsFieldRolloutEventsRepository>().Should().NotBeNull();
			// RMS-1C external ordering-system connectors (2026-09-06)
			Resolve<IRecordDeploymentConnectorsService>().Should().NotBeNull();
			Resolve<IRmsExternalOrderConnectorsRepository>().Should().NotBeNull();
			Resolve<IRmsExternalOrderConnectorRunsRepository>().Should().NotBeNull();
			Resolve<System.Collections.Generic.IEnumerable<IExternalOrderFeedProvider>>().Select(p => p.Key).Should().BeEquivalentTo(RmsExternalOrderConnectorProviders.All);
			Resolve<System.Collections.Generic.IEnumerable<IRecordEvidenceAdapter>>().Should().Contain(a => a.Kind == RmsEvidenceKind.ModuleProjection);
			Resolve<IRecordsRevealService>().Should().NotBeNull();
			Resolve<IRmsRecordDefinitionsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordDefinitionVersionsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordSectionDefinitionsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordFieldDefinitionsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordValueGroupsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordValuesRepository>().Should().NotBeNull();
			Resolve<IRmsSavedReportDefinitionsRepository>().Should().NotBeNull();
			Resolve<IRmsTemplatePackVersionsRepository>().Should().NotBeNull();
			Resolve<IRmsJurisdictionProfileVersionsRepository>().Should().NotBeNull();
			Resolve<IRmsExternalOrdersRepository>().Should().NotBeNull();
			Resolve<IRmsExternalOrderFillsRepository>().Should().NotBeNull();
		}

		[Test]
		public void Records_repositories_resolve_from_the_container()
		{
			Resolve<IRmsOperationalRecordsRepository>().Should().NotBeNull();
			Resolve<IRmsOperationalRecordDetailsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordParticipantsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordUnitResponsesRepository>().Should().NotBeNull();
			Resolve<IRmsRecordAttachmentsRepository>().Should().NotBeNull();
			Resolve<IRmsExternalReferencesRepository>().Should().NotBeNull();
			Resolve<IDomainEventOutboxRepository>().Should().NotBeNull();
			Resolve<IRmsDepartmentCutoversRepository>().Should().NotBeNull();
			Resolve<IRmsDepartmentCutoverEventsRepository>().Should().NotBeNull();
			Resolve<IRmsRevisionsRepository>().Should().NotBeNull();
			Resolve<IRmsAccessAuditsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordSearchProjectionsRepository>().Should().NotBeNull();
			Resolve<IRmsSearchIndexStatesRepository>().Should().NotBeNull();
			Resolve<IRmsSearchWriteFence>().Should().NotBeNull();
			Resolve<IRmsCommandReceiptsRepository>().Should().NotBeNull();
			Resolve<IRmsRecordGroupScopesRepository>().Should().NotBeNull();
			Resolve<IRmsRecordSharesRepository>().Should().NotBeNull();
			Resolve<IRmsLegacyStatsRepository>().Should().NotBeNull();
			Resolve<IDepartmentProfileRepository>().Should().NotBeNull();
			Resolve<IDepartmentProfileMediaRepository>().Should().NotBeNull();
			Resolve<IRmsRecordPrintLayoutsRepository>().Should().NotBeNull();
			// RMS-2 (M0164-M0166)
			Resolve<IRmsIncidentReportsRepository>().Should().NotBeNull();
			Resolve<IRmsSourceFactsRepository>().Should().NotBeNull();
			Resolve<IRmsUnitResponsesRepository>().Should().NotBeNull();
			Resolve<IRmsIncidentTypesRepository>().Should().NotBeNull();
			Resolve<IRmsActionTacticsRepository>().Should().NotBeNull();
			Resolve<IRmsAidsRepository>().Should().NotBeNull();
			Resolve<IRmsLocationsRepository>().Should().NotBeNull();
			Resolve<IRmsNarrativesRepository>().Should().NotBeNull();
			Resolve<IRmsValidationIssuesRepository>().Should().NotBeNull();
			Resolve<IRmsSubmissionsRepository>().Should().NotBeNull();
			Resolve<IRmsSignaturesRepository>().Should().NotBeNull();
			Resolve<IRmsNerisProfilesRepository>().Should().NotBeNull();
			Resolve<IRmsNerisValueSetsRepository>().Should().NotBeNull();
			Resolve<IRmsNerisCrosswalksRepository>().Should().NotBeNull();
			// RMS-3 (M0167-M0168, M0170)
			Resolve<IRmsIncidentModulesRepository>().Should().NotBeNull();
			Resolve<IRmsIncidentResourcesRepository>().Should().NotBeNull();
			Resolve<IRmsCasualtyRescuesRepository>().Should().NotBeNull();
			Resolve<IRmsExposuresRepository>().Should().NotBeNull();
			Resolve<IRmsIncidentAnalysesRepository>().Should().NotBeNull();
			Resolve<IRmsIncidentPropertiesRepository>().Should().NotBeNull();
			Resolve<IRmsIncidentVehiclesRepository>().Should().NotBeNull();
			Resolve<IRmsRecordDueStatesRepository>().Should().NotBeNull();
			Resolve<IRmsRecordLegalHoldsRepository>().Should().NotBeNull();
			// RMS-3c/3d (M0169, M0171)
			Resolve<IRmsEvidenceArtifactsRepository>().Should().NotBeNull();
			Resolve<IRmsDisclosureRequestsRepository>().Should().NotBeNull();
			Resolve<IRmsDisclosureProductionsRepository>().Should().NotBeNull();
		}

		[Test]
		public void All_six_evidence_adapters_are_registered()
		{
			// The plan ships all six, none optional. A source that is not present in this build still registers an
			// adapter, so the dashboard can say "unavailable" rather than leaving an author to infer "none".
			var adapters = ResolveAll<Resgrid.Model.Services.IRecordEvidenceAdapter>().ToList();

			adapters.Select(a => a.Kind).Should().BeEquivalentTo(
				System.Enum.GetValues(typeof(RmsEvidenceKind)).Cast<RmsEvidenceKind>());
		}

		[Test]
		public void Legacy_log_services_still_resolve_with_the_cutover_guard_injected()
		{
			Resolve<IWorkLogsService>().Should().NotBeNull();
			Resolve<IUnitsService>().Should().NotBeNull();
		}
	}
}
