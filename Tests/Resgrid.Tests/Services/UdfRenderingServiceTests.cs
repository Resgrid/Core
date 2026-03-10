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
		}
	}
}

