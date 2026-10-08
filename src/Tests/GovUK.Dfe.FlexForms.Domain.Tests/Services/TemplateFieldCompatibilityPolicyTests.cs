using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class TemplateFieldCompatibilityPolicyTests
{
    private readonly TemplateFieldCompatibilityPolicy _policy = new();

    private static object Field(string id, string type = "text", string? lookup = null, string? key = null) =>
        new { fieldId = id, type, complexField = lookup is null ? null : new { id = lookup }, semanticKey = key };

    private static object Flow(string collectionId, params object[] fields) => KeyedFlow(collectionId, null, fields);

    private static object KeyedFlow(string collectionId, string? key, params object[] fields) =>
        new { flowId = $"{collectionId}-flow", fieldId = collectionId, semanticKey = key, pages = new[] { new { pageId = "item", fields } } };

    private static string Template(object[] fields, object[]? flows = null, object[]? retiredFields = null) =>
        JsonSerializer.Serialize(new
        {
            templateId = "t",
            taskGroups = new[]
            {
                new
                {
                    groupId = "g",
                    tasks = new[]
                    {
                        new
                        {
                            taskId = "task",
                            pages = new[] { new { pageId = "page", fields } },
                            summary = new { mode = "multiCollectionFlow", flows = flows ?? [] },
                        },
                    },
                },
            },
            retiredFields = retiredFields ?? [],
        });

    private static object Retired(string id, string? parent = null, params string[] replacedBy) =>
        new { fieldId = id, parentFieldId = parent, replacedBy };

    private TemplateFieldCompatibilityResult Evaluate(string next, params string[] earlier) =>
        _policy.Evaluate([.. earlier.Select((schema, i) => new TemplateVersionSchema($"1.0.{i}", schema))], next);

    [Fact]
    public void Relabelled_and_added_fields_are_compatible()
    {
        var v1 = Template([Field("name")]);
        var v2 = Template([new { fieldId = "name", type = "textarea", label = new { value = "Full name" } }, Field("email", "email")]);

        Assert.True(Evaluate(v2, v1).IsCompatible);
    }

    [Fact]
    public void Removing_a_field_without_declaring_it_is_rejected()
    {
        var result = Evaluate(Template([Field("firstName"), Field("lastName")]), Template([Field("name")]));

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Violations, v => v.Contains("\"name\"") && v.Contains("retiredFields"));
    }

    [Fact]
    public void Removal_message_gives_the_exact_retiredFields_entry_to_add()
    {
        var v1 = Template([Field("title")], [Flow("attendees", Field("attendeeName"))]);
        var v2 = Template([Field("title")], [Flow("attendees")]);

        var violation = Assert.Single(Evaluate(v2, v1).Violations);

        Assert.Contains("{ \"fieldId\": \"attendeeName\", \"parentFieldId\": \"attendees\" }", violation);
        Assert.Contains("version 1.0.0", violation);
    }

    [Fact]
    public void A_declared_replacement_is_compatible()
    {
        var v2 = Template([Field("firstName"), Field("lastName")], retiredFields: [Retired("name", null, "firstName", "lastName")]);

        Assert.True(Evaluate(v2, Template([Field("name")])).IsCompatible);
    }

    [Fact]
    public void A_retirement_declaration_carried_into_later_versions_stays_valid()
    {
        var v1 = Template([Field("name")]);
        var v2 = Template([Field("firstName")], retiredFields: [Retired("name", null, "firstName")]);

        Assert.True(Evaluate(v2, v1, v2).IsCompatible);
    }

    [Fact]
    public void Reusing_a_retired_field_id_is_rejected()
    {
        var v1 = Template([Field("name")]);
        var v2 = Template([Field("other")], retiredFields: [Retired("name")]);
        var v3 = Template([Field("other"), Field("name")]);

        var result = Evaluate(v3, v1, v2);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Violations, v => v.Contains("\"name\"") && v.Contains("1.0.0") && v.Contains("reused"));
    }

    [Theory]
    [InlineData("text", "number")]
    [InlineData("radios", "checkboxes")]
    [InlineData("text", "date")]
    public void Changing_what_a_field_stores_is_rejected(string before, string after)
    {
        var result = Evaluate(Template([Field("answer", after)]), Template([Field("answer", before)]));

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Violations, v => v.Contains("\"answer\"") && v.Contains("can't change the kind of answer it stores"));
    }

    [Theory]
    [InlineData("text", "textarea")]
    [InlineData("text-area", "character-count")]
    [InlineData("radios", "select")]
    [InlineData("date", "datetime")]
    public void Changing_between_types_that_store_the_same_answer_is_compatible(string before, string after)
    {
        Assert.True(Evaluate(Template([Field("answer", after)]), Template([Field("answer", before)])).IsCompatible);
    }

    [Fact]
    public void Pointing_a_lookup_at_different_data_is_rejected()
    {
        var result = Evaluate(Template([Field("school", "autocomplete", "trusts")]), Template([Field("school", "autocomplete", "academies")]));

        Assert.False(result.IsCompatible);
    }

    [Fact]
    public void Fields_in_collections_are_checked_within_their_collection()
    {
        var v1 = Template([Field("title")], [Flow("attendees", Field("attendeeName"))]);
        var v2 = Template([Field("title")], [Flow("attendees", Field("fullName"))]);

        var result = Evaluate(v2, v1);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Violations, v => v.Contains("\"attendeeName\" (in collection \"attendees\")"));
    }

    [Fact]
    public void Retiring_a_collection_retires_its_fields()
    {
        var v1 = Template([Field("title")], [Flow("attendees", Field("attendeeName"), Field("role"))]);
        var v2 = Template([Field("title")], retiredFields: [Retired("attendees")]);

        Assert.True(Evaluate(v2, v1).IsCompatible);
    }

    [Fact]
    public void Turning_a_field_into_a_collection_is_rejected()
    {
        var v1 = Template([Field("attendees")]);
        var v2 = Template([], [Flow("attendees", Field("name"))]);

        Assert.False(Evaluate(v2, v1).IsCompatible);
    }

    [Fact]
    public void Retiring_a_field_that_is_still_present_or_never_existed_is_rejected()
    {
        var v1 = Template([Field("name")]);
        var v2 = Template([Field("name")], retiredFields: [Retired("name"), Retired("typo")]);

        var result = Evaluate(v2, v1);

        Assert.Contains(result.Violations, v => v.Contains("\"name\"") && v.Contains("still in the template"));
        Assert.Contains(result.Violations, v => v.Contains("\"typo\"") && v.Contains("no earlier version"));
    }

    [Fact]
    public void A_replacement_that_is_not_in_the_template_is_rejected()
    {
        var v2 = Template([Field("firstName")], retiredFields: [Retired("name", null, "firstName", "surname")]);

        var result = Evaluate(v2, Template([Field("name")]));

        Assert.Single(result.Violations);
        Assert.Contains("\"surname\"", result.Violations[0]);
    }

    [Fact]
    public void A_declared_rename_can_carry_on_the_old_reporting_key()
    {
        var v1 = Template([Field("name")]);
        var v2 = Template([Field("fullName", key: "name")], retiredFields: [Retired("name", null, "fullName")]);

        Assert.True(Evaluate(v2, v1).IsCompatible);
    }

    [Fact]
    public void A_rename_message_tells_the_author_which_semanticKey_keeps_reports_continuous()
    {
        var violation = Assert.Single(Evaluate(Template([Field("fullName")]), Template([Field("name", key: "applicantName")])).Violations);

        Assert.Contains("\"semanticKey\": \"applicantName\"", violation);
    }

    [Fact]
    public void Taking_over_a_reporting_key_without_naming_the_field_in_replacedBy_is_rejected()
    {
        var v2 = Template([Field("fullName", key: "name")], retiredFields: [Retired("name")]);

        var violation = Assert.Single(Evaluate(v2, Template([Field("name")])).Violations);

        Assert.Contains("doesn't name it as a replacement", violation);
        Assert.Contains("{ \"fieldId\": \"name\", \"replacedBy\": [\"fullName\"] }", violation);
    }

    [Fact]
    public void Taking_over_a_reporting_key_with_a_different_kind_of_answer_is_rejected()
    {
        var v2 = Template([Field("ageInYears", "number", key: "age")], retiredFields: [Retired("age", null, "ageInYears")]);

        var violation = Assert.Single(Evaluate(v2, Template([Field("age")])).Violations);

        Assert.Contains("same kind of answer", violation);
    }

    [Fact]
    public void Changing_the_semanticKey_of_a_kept_field_is_rejected()
    {
        var violation = Assert.Single(Evaluate(Template([Field("name", key: "fullName")]), Template([Field("name")])).Violations);

        Assert.Contains("\"semanticKey\": \"name\"", violation);
    }

    [Fact]
    public void Two_fields_sharing_a_reporting_key_are_rejected()
    {
        var v2 = Template([Field("name"), Field("fullName", key: "name")]);

        var violation = Assert.Single(Evaluate(v2, Template([Field("name")])).Violations);

        Assert.Contains("share the reporting key \"name\"", violation);
    }

    [Fact]
    public void Reusing_a_dropped_reporting_key_is_rejected()
    {
        var v1 = Template([Field("name")]);
        var v2 = Template([Field("other")], retiredFields: [Retired("name")]);
        var v3 = Template([Field("other"), Field("fullName", key: "name")]);

        var violation = Assert.Single(Evaluate(v3, v1, v2).Violations);

        Assert.Contains("can't be reused", violation);
    }

    [Fact]
    public void A_renamed_collection_carries_on_its_fields_reporting_keys()
    {
        var v1 = Template([Field("title")], [Flow("attendees", Field("attendeeName"), Field("role"))]);
        var v2 = Template([Field("title")], [KeyedFlow("visitors", "attendees", Field("attendeeName"), Field("role"))],
            retiredFields: [Retired("attendees", null, "visitors")]);

        Assert.True(Evaluate(v2, v1).IsCompatible);
    }

    [Fact]
    public void A_semanticKey_cannot_contain_a_slash()
    {
        var violation = Assert.Single(Evaluate(Template([Field("name"), Field("extra", key: "a/b")]), Template([Field("name")])).Violations);

        Assert.Contains("can't contain \"/\"", violation);
    }

    [Fact]
    public void Field_ids_are_compared_case_insensitively()
    {
        Assert.True(Evaluate(Template([Field("FirstName")]), Template([Field("firstName")])).IsCompatible);
    }

    [Fact]
    public void Unreadable_schemas_are_not_checked()
    {
        Assert.True(Evaluate("not json", Template([Field("name")])).IsCompatible);
        Assert.True(Evaluate(Template([]), "not json").IsCompatible);
    }

    [Fact]
    public void Error_message_lists_every_violation()
    {
        var result = Evaluate(Template([Field("b", "number")]), Template([Field("a"), Field("b")]));

        Assert.Equal(2, result.Violations.Count);
        Assert.Equal(string.Join("\n", [TemplateFieldCompatibilityResult.Summary, .. result.Violations]), result.ToErrorMessage());
    }
}
