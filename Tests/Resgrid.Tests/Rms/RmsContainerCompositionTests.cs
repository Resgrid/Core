using FluentAssertions;
using NUnit.Framework;
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
		}

		[Test]
		public void Legacy_log_services_still_resolve_with_the_cutover_guard_injected()
		{
			Resolve<IWorkLogsService>().Should().NotBeNull();
			Resolve<IUnitsService>().Should().NotBeNull();
		}
	}
}
