using AgentBlazor.Core.Components;

namespace AgentBlazor.Core.Tests;

public class AgentUiToolCatalogTests
{
    [Fact]
    public void BuildDocument_EmptyToolCalls_ReturnsNull()
    {
        var catalog = new DefaultAgentUiToolCatalog();

        var document = catalog.BuildDocument([], out var errors);

        Assert.Null(document);
        Assert.Empty(errors);
    }

    [Fact]
    public void BuildDocument_SummaryCardTool_RendersCardBlock()
    {
        var catalog = new DefaultAgentUiToolCatalog();

        var document = catalog.BuildDocument(
        [
            new AgentUiToolCall
            {
                ToolId = AgentUiToolIds.SummaryCard,
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blockId"] = "summary",
                    ["title"] = "Status Summary",
                    ["description"] = "All actions completed successfully."
                }
            }
        ], out var errors);

        Assert.NotNull(document);
        Assert.Empty(errors);
        var block = Assert.Single(document!.Blocks);
        Assert.Equal("summary", block.Id);
        Assert.Equal(AgentUiBlockKind.Card, block.Kind);
        Assert.Equal("Status Summary", block.Title);
    }

    [Fact]
    public void BuildDocument_FormDraftWithoutFields_ReturnsValidationError()
    {
        var catalog = new DefaultAgentUiToolCatalog();

        var document = catalog.BuildDocument(
        [
            new AgentUiToolCall
            {
                ToolId = AgentUiToolIds.FormDraft,
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = "Draft Form"
                }
            }
        ], out var errors);

        Assert.Null(document);
        Assert.NotEmpty(errors);
        Assert.Contains("fields", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDocument_FormDraft_RendersFieldsAndActions()
    {
        var catalog = new DefaultAgentUiToolCatalog();

        var document = catalog.BuildDocument(
        [
            new AgentUiToolCall
            {
                ToolId = AgentUiToolIds.FormDraft,
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blockId"] = "record-draft",
                    ["title"] = "Record Draft",
                    ["fields"] = new object?[]
                    {
                        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = "Name",
                            ["label"] = "Display Name",
                            ["type"] = "text",
                            ["required"] = true,
                            ["value"] = "Example"
                        }
                    },
                    ["actions"] = new object?[]
                    {
                        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["id"] = "applyDraft",
                            ["label"] = "Apply",
                            ["prompt"] = "Apply draft values to the form."
                        }
                    }
                }
            }
        ], out var errors);

        Assert.NotNull(document);
        Assert.Empty(errors);
        var block = Assert.Single(document!.Blocks);
        Assert.Equal(AgentUiBlockKind.Form, block.Kind);
        var field = Assert.Single(block.Fields);
        Assert.Equal("Name", field.Name);
        Assert.True(field.Required);
        Assert.Single(block.Actions);
    }

    [Fact]
    public void BuildDocument_TableView_RendersTableBlock()
    {
        var catalog = new DefaultAgentUiToolCatalog();

        var document = catalog.BuildDocument(
        [
            new AgentUiToolCall
            {
                ToolId = AgentUiToolIds.TableView,
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blockId"] = "metrics-table",
                    ["title"] = "Metrics",
                    ["columns"] = new object?[]
                    {
                        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["key"] = "Name",
                            ["header"] = "Name"
                        },
                        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["key"] = "Value",
                            ["header"] = "Value"
                        }
                    },
                    ["rows"] = new object?[]
                    {
                        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Name"] = "OpenTasks",
                            ["Value"] = 42
                        }
                    }
                }
            }
        ], out var errors);

        Assert.NotNull(document);
        Assert.Empty(errors);
        var block = Assert.Single(document!.Blocks);
        Assert.Equal(AgentUiBlockKind.Table, block.Kind);
        Assert.Equal("metrics-table", block.Id);
        Assert.Equal(2, block.Columns.Count);
        Assert.Single(block.Rows);
    }

    [Fact]
    public void BuildDocument_DuplicateBlockIds_AreMadeUnique()
    {
        var catalog = new DefaultAgentUiToolCatalog();

        var document = catalog.BuildDocument(
        [
            new AgentUiToolCall
            {
                ToolId = AgentUiToolIds.SummaryCard,
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blockId"] = "summary",
                    ["title"] = "Summary A"
                }
            },
            new AgentUiToolCall
            {
                ToolId = AgentUiToolIds.SummaryCard,
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blockId"] = "summary",
                    ["title"] = "Summary B"
                }
            }
        ], out var errors);

        Assert.NotNull(document);
        Assert.Empty(errors);
        Assert.Equal(2, document!.Blocks.Count);
        Assert.Equal("summary", document.Blocks[0].Id);
        Assert.Equal("summary-2", document.Blocks[1].Id);
    }
}
