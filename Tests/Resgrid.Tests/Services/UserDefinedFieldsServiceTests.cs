using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace UserDefinedFieldsServiceTests
	{
		public class with_the_udf_service : TestBase
		{
			protected IUserDefinedFieldsService _udfService;

			protected override void Before_all_tests()
			{
				_udfService = Resolve<IUserDefinedFieldsService>();
			}
		}

		// ── Definition versioning ────────────────────────────────────────────────

		[TestFixture]
		public class when_saving_a_new_definition : with_the_udf_service
		{
			[Test]
			public async Task should_create_version_1_and_be_active()
			{
				var fields = new List<UdfField>
				{
					new UdfField { Name = "incidentType", Label = "Incident Type", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var definition = await _udfService.SaveDefinitionAsync(100, (int)UdfEntityType.Call, fields, "user1");

				definition.Should().NotBeNull();
				definition.Version.Should().Be(1);
				definition.IsActive.Should().BeTrue();
				definition.DepartmentId.Should().Be(100);
				definition.EntityType.Should().Be((int)UdfEntityType.Call);
			}

			[Test]
			public async Task should_increment_version_on_second_save()
			{
				var fields1 = new List<UdfField>
				{
					new UdfField { Name = "field1", Label = "Field 1", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				await _udfService.SaveDefinitionAsync(200, (int)UdfEntityType.Personnel, fields1, "user1");

				var fields2 = new List<UdfField>
				{
					new UdfField { Name = "field1", Label = "Field 1", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true },
					new UdfField { Name = "field2", Label = "Field 2", FieldDataType = (int)UdfFieldDataType.Number, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var v2 = await _udfService.SaveDefinitionAsync(200, (int)UdfEntityType.Personnel, fields2, "user1");

				v2.Version.Should().Be(2);
				v2.IsActive.Should().BeTrue();

				var active = await _udfService.GetActiveDefinitionAsync(200, (int)UdfEntityType.Personnel);
				active.UdfDefinitionId.Should().Be(v2.UdfDefinitionId);
			}

			[Test]
			public async Task should_isolate_definitions_by_department()
			{
				var fields = new List<UdfField>
				{
					new UdfField { Name = "customField", Label = "Custom Field", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				await _udfService.SaveDefinitionAsync(300, (int)UdfEntityType.Call, fields, "user1");
				await _udfService.SaveDefinitionAsync(301, (int)UdfEntityType.Call, fields, "user2");

				var dept300 = await _udfService.GetActiveDefinitionAsync(300, (int)UdfEntityType.Call);
				var dept301 = await _udfService.GetActiveDefinitionAsync(301, (int)UdfEntityType.Call);

				dept300.Should().NotBeNull();
				dept301.Should().NotBeNull();
				dept300.UdfDefinitionId.Should().NotBe(dept301.UdfDefinitionId);
			}
		}

		// ── Field values ─────────────────────────────────────────────────────────

		[TestFixture]
		public class when_saving_field_values : with_the_udf_service
		{
			[Test]
			public async Task should_save_and_retrieve_values_for_entity()
			{
				var fieldId = Guid.NewGuid().ToString();
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = fieldId, Name = "hazmat", Label = "Hazmat?", FieldDataType = (int)UdfFieldDataType.Boolean, IsEnabled = true, IsRequired = false, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				await _udfService.SaveDefinitionAsync(400, (int)UdfEntityType.Call, fields, "user1");

				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = fieldId, Value = "true" }
				};

				var errors = await _udfService.SaveFieldValuesForEntityAsync(400, (int)UdfEntityType.Call, "call-abc", values, "user1");
				errors.Should().BeEmpty();

				var retrieved = await _udfService.GetFieldValuesForEntityAsync(400, (int)UdfEntityType.Call, "call-abc");
				retrieved.Should().NotBeNull();
				retrieved.Should().HaveCountGreaterThanOrEqualTo(1);
			}

			[Test]
			public void should_return_errors_for_invalid_required_field()
			{
				var fieldId = Guid.NewGuid().ToString();
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = fieldId, Name = "requiredField", Label = "Required Field", FieldDataType = (int)UdfFieldDataType.Text, IsRequired = true, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var errors = _udfService.ValidateFieldValues(fields, new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = fieldId, Value = "" }
				});

				errors.Should().ContainKey(fieldId);
				errors[fieldId].Should().NotBeEmpty();
			}
		}

		// ── Delete field creates new version ─────────────────────────────────────

		[TestFixture]
		public class when_deleting_a_field : with_the_udf_service
		{
			[Test]
			public async Task should_create_new_version_without_the_field()
			{
				var fieldToRemoveId = Guid.NewGuid().ToString();
				var fieldToKeepId = Guid.NewGuid().ToString();
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = fieldToRemoveId, Name = "fieldToRemove", Label = "Remove Me", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true },
					new UdfField { UdfFieldId = fieldToKeepId, Name = "keepField", Label = "Keep Me", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var v1 = await _udfService.SaveDefinitionAsync(500, (int)UdfEntityType.Unit, fields, "user1");

				var v2 = await _udfService.DeleteFieldFromDefinitionAsync(fieldToRemoveId, 500, "user1");

				v2.Should().NotBeNull();
				v2.Version.Should().BeGreaterThan(v1.Version);

				var remainingFields = await _udfService.GetFieldsForActiveDefinitionAsync(500, (int)UdfEntityType.Unit);
				remainingFields.Should().NotContain(f => f.Name == "fieldToRemove");
				remainingFields.Should().Contain(f => f.Name == "keepField");
			}
		}
	}
}


