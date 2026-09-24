using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
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
				fieldId = (await _udfService.GetFieldsForActiveDefinitionAsync(400, (int)UdfEntityType.Call)).Single().UdfFieldId;

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

			[Test]
			public async Task should_keep_a_protected_dropdown_value_posted_back_as_the_sentinel()
			{
				var fields = new List<UdfField>
				{
					new UdfField { Name = "disposition", Label = "Disposition", FieldDataType = (int)UdfFieldDataType.Dropdown, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true,
						ValidationRules = JsonConvert.SerializeObject(new UdfValidationRules { Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "transported", Label = "Transported" } } }) }
				};

				var definition = await _udfService.SaveDefinitionAsync(401, (int)UdfEntityType.Call, fields, "user1");
				var fieldId = (await _udfService.GetFieldsForActiveDefinitionAsync(401, (int)UdfEntityType.Call)).Single().UdfFieldId;

				// What ADP leaves in the row: an envelope, which the edit form and the mobile schema show as REDACTED.
				var sealedValue = ProtectedDataEnvelope.Prefix + "sealed-transported";
				await Resolve<IUdfFieldValueRepository>().SaveOrUpdateAsync(new UdfFieldValue
				{
					UdfFieldValueId = Guid.NewGuid().ToString(), UdfFieldId = fieldId, UdfDefinitionId = definition.UdfDefinitionId,
					EntityId = "call-sealed", EntityType = (int)UdfEntityType.Call, Value = sealedValue
				}, CancellationToken.None);

				var errors = await _udfService.SaveFieldValuesForEntityAsync(401, (int)UdfEntityType.Call, "call-sealed",
					new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = fieldId, Value = ProtectedDataEnvelope.RedactionValue } }, "user2");

				errors.Should().BeEmpty();
				(await _udfService.GetFieldValuesForEntityAsync(401, (int)UdfEntityType.Call, "call-sealed"))
					.Should().ContainSingle().Which.Value.Should().Be(sealedValue);
			}
		}

		// ── Combo box ────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_saving_combo_box_values : with_the_udf_service
		{
			[TestCase("Transported", "tx")]
			[TestCase("tx", "tx")]
			[TestCase("  Referred to crisis line ", "Referred to crisis line")]
			public async Task should_store_an_option_as_its_key_and_free_text_as_typed(string entered, string stored)
			{
				var fields = new List<UdfField>
				{
					new UdfField { Name = "outcome", Label = "Outcome", FieldDataType = (int)UdfFieldDataType.ComboBox, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true,
						ValidationRules = JsonConvert.SerializeObject(new UdfValidationRules { Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "tx", Label = "Transported" } } }) }
				};

				await _udfService.SaveDefinitionAsync(403, (int)UdfEntityType.Call, fields, "user1");
				var fieldId = (await _udfService.GetFieldsForActiveDefinitionAsync(403, (int)UdfEntityType.Call)).Single().UdfFieldId;
				var callId = $"call-combo-{Guid.NewGuid()}";

				var errors = await _udfService.SaveFieldValuesForEntityAsync(403, (int)UdfEntityType.Call, callId,
					new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = fieldId, Value = entered } }, "user1");

				errors.Should().BeEmpty();
				(await _udfService.GetFieldValuesForEntityAsync(403, (int)UdfEntityType.Call, callId))
					.Should().ContainSingle().Which.Value.Should().Be(stored);
			}
		}

		// ── Option lists ─────────────────────────────────────────────────────────

		[TestFixture]
		public class when_saving_a_definition_with_option_fields : with_the_udf_service
		{
			[Test]
			public async Task should_reject_a_dropdown_without_options()
			{
				var fields = new List<UdfField>
				{
					new UdfField { Name = "disposition", Label = "Disposition", FieldDataType = (int)UdfFieldDataType.Dropdown, IsEnabled = true }
				};

				Func<Task> save = () => _udfService.SaveDefinitionAsync(402, (int)UdfEntityType.Call, fields, "user1");

				await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one option*");
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
				fieldToRemoveId = (await _udfService.GetFieldsForActiveDefinitionAsync(500, (int)UdfEntityType.Unit)).Single(f => f.Name == "fieldToRemove").UdfFieldId;

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


