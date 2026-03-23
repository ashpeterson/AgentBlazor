using AgentBlazor.Core.Runtime.ExecutionPlans;

namespace AgentBlazor.Core.Tests;

public class InputSchemaParameterParserTests
{
    [Fact]
    public void Parse_HandlesDescriptionsWithCommas_WithoutPhantomParameters()
    {
        var schema = "(string notes [optional] — Include city, state, and postal code, string email [required] — Primary contact email)";

        var parameters = InputSchemaParameterParser.Parse(schema);

        Assert.Equal(2, parameters.Count);

        Assert.Equal("notes", parameters[0].Name);
        Assert.Equal("string", parameters[0].Type);
        Assert.False(parameters[0].Required);
        Assert.Equal("Include city, state, and postal code", parameters[0].Description);

        Assert.Equal("email", parameters[1].Name);
        Assert.Equal("string", parameters[1].Type);
        Assert.True(parameters[1].Required);
        Assert.Equal("Primary contact email", parameters[1].Description);
    }

    [Fact]
    public void Parse_HandlesParenthesizedExamplesWithCommas()
    {
        var schema = "(string companyName [required] — Legal company name (for example, ACME, Inc.), string taxId [optional] — Tax identifier)";

        var parameters = InputSchemaParameterParser.Parse(schema);

        Assert.Equal(2, parameters.Count);
        Assert.Equal("companyName", parameters[0].Name);
        Assert.Equal("Legal company name (for example, ACME, Inc.)", parameters[0].Description);
        Assert.Equal("taxId", parameters[1].Name);
    }

    [Fact]
    public void Parse_HandlesTypeLabelsContainingSpaces()
    {
        var schema = "(array of string tags [optional] — Suggested tags)";

        var parameters = InputSchemaParameterParser.Parse(schema);

        var parameter = Assert.Single(parameters);
        Assert.Equal("array of string", parameter.Type);
        Assert.Equal("tags", parameter.Name);
        Assert.False(parameter.Required);
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForJsonSchemaInput()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "field": { "type": "string" }
              }
            }
            """;

        var parameters = InputSchemaParameterParser.Parse(schema);

        Assert.Empty(parameters);
    }
}
