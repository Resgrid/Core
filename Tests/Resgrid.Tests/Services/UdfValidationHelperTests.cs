using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Services
{
	namespace UdfValidationHelperTests
	{
		// ── Shared helpers ────────────────────────────────────────────────────────
		internal static class UdfTestHelpers
		{
			internal static UdfField MakeField(UdfFieldDataType type, bool required = false) =>
				new UdfField
				{
					UdfFieldId = "test-field",
					Name = "testField",
					Label = "Test Field",
					FieldDataType = (int)type,
					IsRequired = required,
					IsEnabled = true
				};

			internal static UdfField MakeFieldWithRules(UdfFieldDataType type, UdfValidationRules rules) =>
				new UdfField
				{
					UdfFieldId = "test-field",
					Name = "testField",
					Label = "Test Field",
					FieldDataType = (int)type,
					IsRequired = false,
					IsEnabled = true,
					ValidationRules = JsonConvert.SerializeObject(rules)
				};
		}

		// ── Required ─────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_required_fields
		{
			[Test]
			public void should_fail_when_required_text_is_empty()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: true);
				UdfValidationHelper.ValidateFieldValue(field, "").Should().NotBeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, null).Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_when_required_text_has_value()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: true);
				UdfValidationHelper.ValidateFieldValue(field, "hello").Should().BeEmpty();
			}

			[Test]
			public void should_pass_when_not_required_and_empty()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: false);
				UdfValidationHelper.ValidateFieldValue(field, "").Should().BeEmpty();
			}
		}

		// ── Text length ──────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_text_length_rules
		{
			[Test]
			public void should_fail_below_min_length()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { MinLength = 5 });
				UdfValidationHelper.ValidateFieldValue(field, "ab").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_at_min_length()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { MinLength = 3 });
				UdfValidationHelper.ValidateFieldValue(field, "abc").Should().BeEmpty();
			}

			[Test]
			public void should_fail_above_max_length()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { MaxLength = 5 });
				UdfValidationHelper.ValidateFieldValue(field, "toolongvalue").Should().NotBeEmpty();
			}
		}

		// ── Numeric range ────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_numeric_range_rules
		{
			[Test]
			public void should_fail_number_below_min()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Number, new UdfValidationRules { MinValue = 10 });
				UdfValidationHelper.ValidateFieldValue(field, "5").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_number_within_range()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Number, new UdfValidationRules { MinValue = 1, MaxValue = 100 });
				UdfValidationHelper.ValidateFieldValue(field, "50").Should().BeEmpty();
			}

			[Test]
			public void should_fail_decimal_above_max()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Decimal, new UdfValidationRules { MaxValue = 9.99m });
				UdfValidationHelper.ValidateFieldValue(field, "10.50").Should().NotBeEmpty();
			}

			[Test]
			public void should_fail_non_numeric_for_number_field()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Number);
				UdfValidationHelper.ValidateFieldValue(field, "abc").Should().NotBeEmpty();
			}
		}

		// ── Regex ────────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_regex_rules
		{
			[Test]
			public void should_fail_when_value_does_not_match_regex()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { Regex = "^[A-Z]{3}$" });
				UdfValidationHelper.ValidateFieldValue(field, "abc").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_when_value_matches_regex()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { Regex = "^[A-Z]{3}$" });
				UdfValidationHelper.ValidateFieldValue(field, "ABC").Should().BeEmpty();
			}
		}

		// ── Email / Phone / Url ──────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_format_fields
		{
			[Test]
			public void should_fail_invalid_email()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Email);
				UdfValidationHelper.ValidateFieldValue(field, "notanemail").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_valid_email()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Email);
				UdfValidationHelper.ValidateFieldValue(field, "test@example.com").Should().BeEmpty();
			}

			[Test]
			public void should_fail_invalid_url()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Url);
				UdfValidationHelper.ValidateFieldValue(field, "not-a-url").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_valid_https_url()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Url);
				UdfValidationHelper.ValidateFieldValue(field, "https://resgrid.com").Should().BeEmpty();
			}

			[Test]
			public void should_fail_invalid_phone()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Phone);
				UdfValidationHelper.ValidateFieldValue(field, "not-phone!!").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_valid_phone()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Phone);
				UdfValidationHelper.ValidateFieldValue(field, "+1 (555) 867-5309").Should().BeEmpty();
			}
		}

		// ── Dropdown / MultiSelect ───────────────────────────────────────────────

		[TestFixture]
		public class when_validating_dropdown_fields
		{
			[Test]
			public void should_fail_dropdown_with_invalid_key()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Dropdown, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "fire", Label = "Fire" },
						new UdfDropdownOption { Key = "ems", Label = "EMS" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "police").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_dropdown_with_valid_key()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Dropdown, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "fire", Label = "Fire" },
						new UdfDropdownOption { Key = "ems", Label = "EMS" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "fire").Should().BeEmpty();
			}

			[Test]
			public void should_fail_multiselect_with_invalid_key()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.MultiSelect, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "a", Label = "A" },
						new UdfDropdownOption { Key = "b", Label = "B" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "a,c").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_multiselect_with_all_valid_keys()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.MultiSelect, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "a", Label = "A" },
						new UdfDropdownOption { Key = "b", Label = "B" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "a,b").Should().BeEmpty();
			}
		}

		// ── Boolean ──────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_boolean_fields
		{
			[Test]
			public void should_pass_true_false_values()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Boolean);
				UdfValidationHelper.ValidateFieldValue(field, "true").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, "false").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, "1").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, "0").Should().BeEmpty();
			}

			[Test]
			public void should_fail_non_boolean_value()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Boolean);
				UdfValidationHelper.ValidateFieldValue(field, "maybe").Should().NotBeEmpty();
			}
		}

		// ── HTML attributes ──────────────────────────────────────────────────────

		[TestFixture]
		public class when_generating_html_attributes
		{
			[Test]
			public void should_include_required_attribute_for_required_field()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: true);
				var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
				attrs.Should().ContainKey("required");
			}

			[Test]
			public void should_include_min_max_for_number_field_with_rules()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Number, new UdfValidationRules { MinValue = 1, MaxValue = 100 });
				var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
				attrs.Should().ContainKey("min");
				attrs.Should().ContainKey("max");
				attrs["min"].Should().Be("1");
				attrs["max"].Should().Be("100");
			}

			[Test]
			public void should_include_pattern_for_regex_rule()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { Regex = "^[A-Z]+$" });
				var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
				attrs.Should().ContainKey("pattern");
			}
		}
	}
}

