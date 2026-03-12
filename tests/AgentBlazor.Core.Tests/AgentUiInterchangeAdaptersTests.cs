using AgentBlazor.Core.Components;

namespace AgentBlazor.Core.Tests;

public class AgentUiInterchangeAdaptersTests
{
    [Fact]
    public void FromA2UiJsonLines_MapsCardAndFormIntoAgentUiDocument()
    {
        const string jsonLines = """
            {"surfaceUpdate":{"surfaceId":"recipe-surface","components":[{"id":"recipe-summary","type":"card","title":"Recipe Summary","description":"Draft is ready.","actions":[{"id":"apply","label":"Apply","prompt":"Apply the draft."}]},{"id":"recipe-form","type":"form","title":"Recipe Draft","fields":[{"name":"Title","label":"Recipe Title","type":"text","required":true},{"name":"Minutes","label":"Minutes","type":"number"}]}]}}
            {"dataModelUpdate":{"surfaceId":"recipe-surface","path":"/","contents":{"Title":"Omelette","Minutes":12}}}
            {"beginRendering":{"surfaceId":"recipe-surface","root":"recipe-form"}}
            """;

        var document = AgentUiInterchangeAdapters.FromA2UiJsonLines(jsonLines, out var diagnostics);

        Assert.NotNull(document);
        Assert.Empty(diagnostics);
        Assert.Equal(2, document!.Blocks.Count);

        var form = Assert.Single(document.Blocks, static block => block.Kind == AgentUiBlockKind.Form);
        Assert.Equal("recipe-form", form.Id);
        Assert.Equal("Recipe Draft", form.Title);
        Assert.Equal("Omelette", form.Fields.Single(static field => field.Name == "Title").Value);
        Assert.Equal("12", form.Fields.Single(static field => field.Name == "Minutes").Value);

        var card = Assert.Single(document.Blocks, static block => block.Kind == AgentUiBlockKind.Card);
        Assert.Single(card.Actions);
        Assert.Equal("Apply", card.Actions[0].Label);
    }

    [Fact]
    public void FromOpenJsonUi_MapsCardTableAndChartIntoAgentUiDocument()
    {
        const string json = """
            {
              "type": "open-json-ui",
              "spec": {
                "components": [
                  {
                    "id": "metrics-card",
                    "type": "card",
                    "properties": {
                      "title": "Metrics",
                      "description": "Topline metrics"
                    }
                  },
                  {
                    "id": "tasks-table",
                    "type": "table",
                    "properties": {
                      "title": "Tasks",
                      "columns": [
                        { "key": "name", "header": "Name" },
                        { "key": "owner", "header": "Owner" }
                      ],
                      "rows": [
                        { "name": "Ship adapters", "owner": "Platform" }
                      ]
                    }
                  },
                  {
                    "id": "trend-chart",
                    "type": "chart",
                    "properties": {
                      "title": "Trend",
                      "chartType": "line",
                      "labels": ["Mon", "Tue", "Wed"],
                      "series": [
                        { "name": "Score", "data": [1, 3, 5] }
                      ]
                    }
                  }
                ]
              }
            }
            """;

        var document = AgentUiInterchangeAdapters.FromOpenJsonUi(json, out var diagnostics);

        Assert.NotNull(document);
        Assert.Empty(diagnostics);
        Assert.Equal(3, document!.Blocks.Count);
        Assert.Contains(document.Blocks, static block => block.Kind == AgentUiBlockKind.Card);
        Assert.Contains(document.Blocks, static block => block.Kind == AgentUiBlockKind.Table);

        var chart = Assert.Single(document.Blocks, static block => block.Kind == AgentUiBlockKind.Chart);
        Assert.Equal(AgentUiChartType.Line, chart.ChartType);
        Assert.Equal(3, chart.ChartLabels.Count);
        Assert.Single(chart.ChartSeries);
    }

    [Fact]
    public void FromOpenJsonUi_UnsupportedPayload_ReturnsDiagnostics()
    {
        const string json = """
            {
              "type": "open-json-ui",
              "spec": {
                "components": [
                  { "id": "layout-root", "type": "stack" }
                ]
              }
            }
            """;

        var document = AgentUiInterchangeAdapters.FromOpenJsonUi(json, out var diagnostics);

        Assert.Null(document);
        Assert.NotEmpty(diagnostics);
        Assert.Contains("not supported", diagnostics[0], StringComparison.OrdinalIgnoreCase);
    }
}
