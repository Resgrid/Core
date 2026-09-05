using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Neris;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class NerisSubmissionValidationTests
	{
		[TestCase("rank", "NONFF", "FF")]
		[TestCase("years_of_service", "NONFF", "FF")]
		[TestCase("rescue.mayday", "NONFF", "FF")]
		[TestCase("casualty.injury_or_noninjury.ff_injury_details", "NONFF", "FF")]
		[TestCase("rescue.presence_known", "FF", "NONFF")]
		public void Casualty_conditions_have_exact_paths_and_do_not_drop_zero_or_empty_objects(string field, string wrongType, string allowedType)
		{
			var person = new JObject { ["type"] = wrongType };
			var keys = field.Split('.'); var parent = person;
			foreach (var key in keys.Take(keys.Length - 1)) { var next = new JObject(); parent[key] = next; parent = next; }
			parent[keys.Last()] = field == "years_of_service" ? new JValue(0) : field == "rank" ? new JValue("Captain") : new JObject();
			var payload = new JObject { ["casualty_rescues"] = new JArray(person) };
			var paths = new List<string>();
			NerisPayloadRules.Validate(payload, false, (path, message) => paths.Add(path));
			paths.Should().ContainSingle().Which.Should().Be("/casualty_rescues/0/" + field.Replace('.', '/'));
			person["type"] = allowedType; paths.Clear();
			NerisPayloadRules.Validate(payload, false, (path, message) => paths.Add(path));
			paths.Should().BeEmpty();
		}

		[Test]
		public void Mapped_civilian_with_firefighter_fields_is_rejected_by_the_officer_validator()
		{
			var snapshot = NerisMappingTests.Snapshot();
			snapshot.Casualties[0].PersonType = RmsCasualtyPersonTypes.Civilian;
			snapshot.Casualties[0].Rank = "Captain";
			snapshot.Casualties[0].YearsOfService = 0;
			var issues = new NerisValidationService(Mock.Of<INerisApiClient>(), Mock.Of<INerisProfileService>()).ValidateLocal(snapshot, NerisMappingTests.Profile());
			issues.Where(i => i.RuleKey == "neris.contract.condition").Select(i => i.FieldPath).Should().Contain(new[]
			{
				"/casualty_rescues/0/rank", "/casualty_rescues/0/years_of_service", "/casualty_rescues/0/casualty/injury_or_noninjury/ff_injury_details"
			});
		}

		[TestCase(false, "schema")]
		[TestCase(false, "condition")]
		[TestCase(false, "malformed")]
		[TestCase(false, "foreign-profile")]
		[TestCase(true, "schema")]
		[TestCase(true, "condition")]
		[TestCase(true, "malformed")]
		[TestCase(true, "foreign-profile")]
		public async Task Invalid_queued_payload_is_refused_before_credentials_or_any_HTTP(bool analysis, string attack)
		{
			var profile = NerisMappingTests.Profile();
			var payload = ValidPayload(analysis);
			if (attack == "schema") payload["base"] = new JObject();
			if (attack == "condition")
			{
				if (analysis) payload.Remove("properties");
				else { payload["casualty_rescues"][0]["type"] = "NONFF"; payload["casualty_rescues"][0]["rank"] = "Captain"; }
			}
			var submission = new RmsSubmission { DepartmentId = profile.DepartmentId, RecordId = "r", PayloadJson = attack == "malformed" ? "{broken" : payload.ToString(Formatting.None) };
			if (attack == "foreign-profile") profile.DepartmentId++;
			var original = submission.PayloadJson;
			var client = new Mock<INerisApiClient>(MockBehavior.Strict);
			var profiles = new Mock<INerisProfileService>(MockBehavior.Strict);
			var service = new NerisSubmissionService(client.Object, profiles.Object);
			var outcome = analysis ? await service.DeliverAnalysisAsync(profile, submission, "FD24027000|INC-123|1788264000", null)
				: await service.DeliverAsync(profile, submission, null);
			outcome.Kind.Should().Be(NerisOutcomeKind.Rejected);
			outcome.LocalValidationFailure.Should().BeTrue();
			outcome.DeliveryUncertain.Should().BeFalse();
			outcome.ResponseJson.Should().BeNull("there was no external response");
			outcome.StatusCode.Should().BeNull();
			outcome.Errors.Should().NotBeEmpty();
			NerisValidationService.ToIssues(outcome, submission.DepartmentId, submission.RecordId).Should().OnlyContain(issue => issue.Source == (int)RmsValidationSource.Local);
			submission.PayloadJson.Should().Be(original);
			client.VerifyNoOtherCalls(); profiles.VerifyNoOtherCalls();
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public async Task Complete_queued_payload_is_sent_byte_for_byte_once_to_the_selected_operation(bool analysis, bool update)
		{
			var profile = NerisMappingTests.Profile(); var credential = new NerisCredential();
			var submission = new RmsSubmission { DepartmentId = profile.DepartmentId, RecordId = "r", PayloadJson = ValidPayload(analysis).ToString(Formatting.None) };
			var client = new Mock<INerisApiClient>(MockBehavior.Strict);
			var profiles = new Mock<INerisProfileService>(MockBehavior.Strict);
			profiles.Setup(p => p.GetCredentialAsync(profile)).ReturnsAsync(credential);
			var accepted = new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Accepted, ExternalId = "receipt" };
			const string parent = "FD24027000|INC-123|1788264000";
			if (analysis && update) client.Setup(c => c.UpdateIncidentAnalysisAsync(profile, credential, "receipt", submission.PayloadJson, It.IsAny<CancellationToken>())).ReturnsAsync(accepted);
			if (analysis && !update) client.Setup(c => c.CreateIncidentAnalysisAsync(profile, credential, parent, submission.PayloadJson, It.IsAny<CancellationToken>())).ReturnsAsync(accepted);
			if (!analysis && update) client.Setup(c => c.UpdateIncidentAsync(profile, credential, "receipt", submission.PayloadJson, It.IsAny<CancellationToken>())).ReturnsAsync(accepted);
			if (!analysis && !update) client.Setup(c => c.CreateIncidentAsync(profile, credential, submission.PayloadJson, It.IsAny<CancellationToken>())).ReturnsAsync(accepted);
			var service = new NerisSubmissionService(client.Object, profiles.Object);
			var outcome = analysis ? await service.DeliverAnalysisAsync(profile, submission, parent, update ? "receipt" : null)
				: await service.DeliverAsync(profile, submission, update ? "receipt" : null);
			outcome.Should().BeSameAs(accepted);
			client.Invocations.Should().ContainSingle(); profiles.Invocations.Should().ContainSingle();
		}

		private static JObject ValidPayload(bool analysis) => analysis
			? JObject.Parse("{\"base\":{\"neris_id_incident\":\"FD24027000|INC-123|1788264000\",\"incident_number\":\"INC-123\"},\"properties\":[{\"parcel_id\":\"P-12\"}]}")
			: JObject.Parse(new NerisMappingService().BuildIncidentPayloadJson(NerisMappingTests.Snapshot(), NerisMappingTests.Profile()));
	}
}
