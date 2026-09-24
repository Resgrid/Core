using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace UdfRenderingServiceTests
	{
		[TestFixture]
		public class when_generating_html_form_fields
		{
			private readonly UdfRenderingService _service = new UdfRenderingService();

			private UdfDefinition MakeDefinition(int entityType = 0) => new UdfDefinition
			{
				UdfDefinitionId = "def-1",
				DepartmentId = 1,
				EntityType = entityType,
				Version = 1,
				IsActive = true
			};

			[Test]
			public void should_return_empty_when_no_fields()
			{
				var html = _service.GenerateHtmlFormFields(MakeDefinition(), new List<UdfField>(), new List<UdfFieldValue>());
				html.Should().BeNullOrEmpty();
			}

			[Test]
			public void should_include_required_attribute_for_required_field()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "name", Label = "Name", FieldDataType = (int)UdfFieldDataType.Text, IsRequired = true, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, new List<UdfFieldValue>());

				html.Should().Contain("required");
				html.Should().Contain("type=\"text\"");
				html.Should().Contain("Name");
			}

			[Test]
			public void should_render_correct_input_type_for_email()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "email", Label = "Email", FieldDataType = (int)UdfFieldDataType.Email, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, new List<UdfFieldValue>());
				html.Should().Contain("type=\"email\"");
			}

			[Test]
			public void should_render_select_for_dropdown_with_options()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "opt1", Label = "Option 1" },
						new UdfDropdownOption { Key = "opt2", Label = "Option 2" }
					}
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "dropdown", Label = "Dropdown", FieldDataType = (int)UdfFieldDataType.Dropdown, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, new List<UdfFieldValue>());
				html.Should().Contain("<select");
				html.Should().Contain("Option 1");
				html.Should().Contain("Option 2");
			}

			[TestCase(UdfFieldDataType.Dropdown)]
			[TestCase(UdfFieldDataType.MultiSelect)]
			public void should_keep_an_unrevealed_protected_selection_posting_the_sentinel(UdfFieldDataType type)
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "opt1", Label = "Option 1" } }
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "outcome", Label = "Outcome", FieldDataType = (int)type, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				// An ADP envelope renders as the REDACTED placeholder, which matches no option.
				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = "f1", Value = ProtectedDataEnvelope.Prefix + "ciphertext" }
				};

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, values);

				html.Should().Contain($"<option value=\"{ProtectedDataEnvelope.RedactionValue}\" selected>");
				html.Should().Contain("data-adp-field=\"udffieldvalues.value:f1\"");
				html.Should().NotContain("ciphertext");
			}

			private static UdfField ComboField() => new UdfField
			{
				UdfFieldId = "f1", Name = "outcome", Label = "Outcome", FieldDataType = (int)UdfFieldDataType.ComboBox, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true,
				ValidationRules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "tx", Label = "Transported" },
						new UdfDropdownOption { Key = "refused", Label = "Refused care" }
					}
				})
			};

			[Test]
			public void should_render_a_combo_box_as_a_text_input_with_a_suggestion_list()
			{
				var html = _service.GenerateHtmlFormFields(MakeDefinition(), new List<UdfField> { ComboField() }, new List<UdfFieldValue>());

				html.Should().Contain("type=\"text\"");
				html.Should().Contain("list=\"udf_f1_options\"");
				html.Should().Contain("<datalist id=\"udf_f1_options\">");
				html.Should().Contain("<option value=\"Transported\" data-key=\"tx\"></option>");
				html.Should().Contain("<option value=\"Refused care\" data-key=\"refused\"></option>");
				html.Should().NotContain("<select");
			}

			[Test]
			public void should_show_a_stored_combo_box_key_as_its_label()
			{
				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = "tx" } };

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), new List<UdfField> { ComboField() }, values);

				html.Should().Contain("value=\"Transported\" placeholder");
			}

			[Test]
			public void should_show_combo_box_free_text_as_typed()
			{
				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = "Referred to crisis line" } };

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), new List<UdfField> { ComboField() }, values);

				html.Should().Contain("value=\"Referred to crisis line\" placeholder");
			}

			[Test]
			public void should_post_the_sentinel_for_an_unrevealed_protected_combo_box()
			{
				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = ProtectedDataEnvelope.Prefix + "ciphertext" } };

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), new List<UdfField> { ComboField() }, values);

				html.Should().Contain($"value=\"{ProtectedDataEnvelope.RedactionValue}\" placeholder");
				html.Should().Contain("data-adp-field=\"udffieldvalues.value:f1\"");
			}

			[Test]
			public void should_not_add_the_sentinel_option_for_a_plain_selection()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "opt1", Label = "Option 1" } }
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "outcome", Label = "Outcome", FieldDataType = (int)UdfFieldDataType.Dropdown, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = "opt1" } };

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, values);

				html.Should().NotContain(ProtectedDataEnvelope.RedactionValue);
				html.Should().Contain("<option value=\"opt1\" selected>");
			}

			[Test]
			public void should_pre_populate_existing_value()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "notes", Label = "Notes", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = "f1", Value = "My existing note" }
				};

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, values);
				html.Should().Contain("My existing note");
			}

			[Test]
			public void should_group_fields_by_group_name()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "f1", Label = "Field 1", GroupName = "Section A", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true },
					new UdfField { UdfFieldId = "f2", Name = "f2", Label = "Field 2", GroupName = "Section B", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var html = _service.GenerateHtmlFormFields(MakeDefinition(), fields, new List<UdfFieldValue>());
				html.Should().Contain("Section A");
				html.Should().Contain("Section B");
				html.Should().Contain("<fieldset");
			}
		}

		[TestFixture]
		public class when_generating_react_native_schema
		{
			private readonly UdfRenderingService _service = new UdfRenderingService();

			private UdfDefinition MakeDefinition() => new UdfDefinition
			{
				UdfDefinitionId = "def-rn-1",
				DepartmentId = 1,
				EntityType = (int)UdfEntityType.Call,
				Version = 1,
				IsActive = true
			};

			[Test]
			public void should_return_valid_json()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "priority", Label = "Priority", FieldDataType = (int)UdfFieldDataType.Dropdown, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var json = _service.GenerateReactNativeSchema(MakeDefinition(), fields, new List<UdfFieldValue>());

				json.Should().NotBeNullOrEmpty();
				var parsed = JsonConvert.DeserializeObject<dynamic>(json);
				((string)parsed.definitionId).Should().Be("def-rn-1");
				((int)parsed.version).Should().Be(1);
			}

			[Test]
			public void should_include_validation_rules_in_schema()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules { MinLength = 3, MaxLength = 50 });
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "name", Label = "Name", FieldDataType = (int)UdfFieldDataType.Text, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var json = _service.GenerateReactNativeSchema(MakeDefinition(), fields, new List<UdfFieldValue>());
				json.Should().Contain("minLength");
				json.Should().Contain("maxLength");
			}

			[Test]
			public void should_describe_a_combo_box_with_its_suggestions()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "tx", Label = "Transported" } }
				});
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "outcome", Label = "Outcome", FieldDataType = (int)UdfFieldDataType.ComboBox, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var json = _service.GenerateReactNativeSchema(MakeDefinition(), fields, new List<UdfFieldValue>());
				var parsed = JsonConvert.DeserializeObject<dynamic>(json);

				((string)parsed.fields[0].type).Should().Be("combobox");
				((string)parsed.fields[0].validation.options[0].key).Should().Be("tx");
				((string)parsed.fields[0].validation.options[0].label).Should().Be("Transported");
			}

			[Test]
			public void should_include_current_value_when_pre_populated()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "status", Label = "Status", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = "f1", Value = "active" }
				};

				var json = _service.GenerateReactNativeSchema(MakeDefinition(), fields, values);
				json.Should().Contain("active");
			}

			[Test]
			public void should_exclude_non_mobile_visible_fields()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "webOnly", Label = "Web Only", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = false, IsVisibleOnReports = true },
					new UdfField { UdfFieldId = "f2", Name = "mobileField", Label = "Mobile Field", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var json = _service.GenerateReactNativeSchema(MakeDefinition(), fields, new List<UdfFieldValue>());
				json.Should().NotContain("webOnly");
				json.Should().Contain("mobileField");
			}
		}

		[TestFixture]
		public class when_generating_read_only_html
		{
			private readonly UdfRenderingService _service = new UdfRenderingService();

			private UdfDefinition MakeDefinition() => new UdfDefinition
			{
				UdfDefinitionId = "def-ro-1",
				DepartmentId = 1,
				EntityType = (int)UdfEntityType.Contact,
				Version = 1,
				IsActive = true
			};

			[Test]
			public void should_render_label_and_value()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "notes", Label = "Notes", FieldDataType = (int)UdfFieldDataType.Text, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = "f1", Value = "Some note content" }
				};

				var html = _service.GenerateReadOnlyHtml(MakeDefinition(), fields, values);
				html.Should().Contain("Notes");
				html.Should().Contain("Some note content");
			}

			[Test]
			public void should_show_yes_no_for_boolean()
			{
				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "active", Label = "Active", FieldDataType = (int)UdfFieldDataType.Boolean, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = "f1", Value = "true" }
				};

				var html = _service.GenerateReadOnlyHtml(MakeDefinition(), fields, values);
				html.Should().Contain("Yes");
			}

			[Test]
			public void should_resolve_dropdown_key_to_label()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "p1", Label = "Priority 1" },
						new UdfDropdownOption { Key = "p2", Label = "Priority 2" }
					}
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "priority", Label = "Priority", FieldDataType = (int)UdfFieldDataType.Dropdown, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue>
				{
					new UdfFieldValue { UdfFieldId = "f1", Value = "p1" }
				};

				var html = _service.GenerateReadOnlyHtml(MakeDefinition(), fields, values);
				html.Should().Contain("Priority 1");
				html.Should().NotContain(">p1<");
			}

			[Test]
			public void should_resolve_a_dropdown_key_containing_a_comma()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "transported, als", Label = "Transported - ALS" }
					}
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "disposition", Label = "Disposition", FieldDataType = (int)UdfFieldDataType.Dropdown, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = "transported, als" } };

				var html = _service.GenerateReadOnlyHtml(MakeDefinition(), fields, values);
				html.Should().Contain("Transported - ALS");
			}

			[TestCase("tx", "Transported")]
			[TestCase("Referred to crisis line", "Referred to crisis line")]
			public void should_show_a_combo_box_key_as_its_label_and_free_text_as_typed(string stored, string shown)
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "tx", Label = "Transported" } }
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "outcome", Label = "Outcome", FieldDataType = (int)UdfFieldDataType.ComboBox, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = stored } };

				var html = _service.GenerateReadOnlyHtml(MakeDefinition(), fields, values);
				html.Should().Contain($"<dd>{shown}</dd>");
			}

			[Test]
			public void should_show_the_placeholder_for_a_protected_dropdown_value()
			{
				var rules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "p1", Label = "Priority 1" } }
				});

				var fields = new List<UdfField>
				{
					new UdfField { UdfFieldId = "f1", Name = "priority", Label = "Priority", FieldDataType = (int)UdfFieldDataType.Dropdown, ValidationRules = rules, IsEnabled = true, IsVisibleOnMobile = true, IsVisibleOnReports = true }
				};

				var values = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "f1", Value = ProtectedDataEnvelope.Prefix + "ciphertext" } };

				var html = _service.GenerateReadOnlyHtml(MakeDefinition(), fields, values);
				html.Should().Contain($"<dd>{ProtectedDataEnvelope.RedactionValue}</dd>");
			}
		}

		[TestFixture]
		public class when_formatting_display_values
		{
			private readonly UdfRenderingService _service = new UdfRenderingService();

			private static UdfField Field(UdfFieldDataType type) => new UdfField
			{
				UdfFieldId = "f1", Name = "outcome", Label = "Outcome", FieldDataType = (int)type, IsEnabled = true,
				ValidationRules = JsonConvert.SerializeObject(new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "tx", Label = "Transported" },
						new UdfDropdownOption { Key = "refused", Label = "Refused care" }
					}
				})
			};

			[TestCase(UdfFieldDataType.ComboBox, "tx", "Transported")]
			[TestCase(UdfFieldDataType.ComboBox, "Referred to crisis line", "Referred to crisis line")]
			[TestCase(UdfFieldDataType.Dropdown, "refused", "Refused care")]
			[TestCase(UdfFieldDataType.MultiSelect, "tx,refused", "Transported, Refused care")]
			[TestCase(UdfFieldDataType.Boolean, "true", "Yes")]
			[TestCase(UdfFieldDataType.Text, "tx", "tx")]
			public void should_show_what_a_person_reads(UdfFieldDataType type, string stored, string shown)
			{
				_service.FormatDisplayValue(Field(type), stored).Should().Be(shown);
			}

			[Test]
			public void should_never_show_a_sealed_value()
			{
				_service.FormatDisplayValue(Field(UdfFieldDataType.ComboBox), ProtectedDataEnvelope.Prefix + "ciphertext")
					.Should().Be(ProtectedDataEnvelope.RedactionValue);
				_service.FormatDisplayValue(null, ProtectedDataEnvelope.Prefix + "ciphertext")
					.Should().Be(ProtectedDataEnvelope.RedactionValue);
			}
		}
	}
}

