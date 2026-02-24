using System.Text.Json;
using AgentBlazor.Core.Components;

namespace AgentBlazor.Core.Tests;

public class GenerativeUiSpecTests
{
    [Fact]
    public void DocumentValidation_FailsForUnsupportedVersion()
    {
        var document = new AgentUiDocument
        {
            SpecVersion = "agentblazor.ui.v999",
            Blocks =
            [
                new AgentUiBlock
                {
                    Id = "summary",
                    Kind = AgentUiBlockKind.Card
                }
            ]
        };

        var valid = document.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("Unsupported spec version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentValidation_FailsForFormWithoutFields()
    {
        var document = new AgentUiDocument
        {
            Blocks =
            [
                new AgentUiBlock
                {
                    Id = "form1",
                    Kind = AgentUiBlockKind.Form
                }
            ]
        };

        var valid = document.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("must define at least one field", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentValidation_PassesForValidCardFormAndTable()
    {
        var document = new AgentUiDocument
        {
            Blocks =
            [
                new AgentUiBlock
                {
                    Id = "card1",
                    Kind = AgentUiBlockKind.Card,
                    Title = "Supplier summary",
                    Description = "Top risk suppliers"
                },
                new AgentUiBlock
                {
                    Id = "form1",
                    Kind = AgentUiBlockKind.Form,
                    Fields =
                    [
                        new AgentUiField { Name = "SupplierName", Label = "Supplier name", Required = true }
                    ],
                    Actions =
                    [
                        new AgentUiAction { Id = "save", Label = "Save", Prompt = "save supplier form" }
                    ]
                },
                new AgentUiBlock
                {
                    Id = "table1",
                    Kind = AgentUiBlockKind.Table,
                    Columns =
                    [
                        new AgentUiTableColumn { Key = "SupplierName", Header = "Supplier" },
                        new AgentUiTableColumn { Key = "RiskScore", Header = "Risk Score" }
                    ],
                    Rows =
                    [
                        new Dictionary<string, object?>
                        {
                            ["SupplierName"] = "Alpine Components",
                            ["RiskScore"] = 82
                        }
                    ]
                }
            ]
        };

        var valid = document.TryValidate(out var error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void JsonSerialization_UsesStringEnumValues_ForBlockKind()
    {
        var document = new AgentUiDocument
        {
            Blocks =
            [
                new AgentUiBlock
                {
                    Id = "card1",
                    Kind = AgentUiBlockKind.Card
                }
            ]
        };

        var json = JsonSerializer.Serialize(document, AgentGenerativeUiSpec.JsonOptions);

        Assert.Contains("\"kind\":\"Card\"", json, StringComparison.Ordinal);

        var roundTrip = JsonSerializer.Deserialize<AgentUiDocument>(json, AgentGenerativeUiSpec.JsonOptions);
        Assert.NotNull(roundTrip);
        Assert.Single(roundTrip.Blocks);
        Assert.Equal(AgentUiBlockKind.Card, roundTrip.Blocks[0].Kind);
    }
}
