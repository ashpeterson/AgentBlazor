using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public class ActionScorerTests
{
    private readonly ActionScorer _scorer = new();

    #region Domain Verb Detection

    [Theory]
    [InlineData("GetUser", ActionClassification.Query, false)]
    [InlineData("GetUserAsync", ActionClassification.Query, false)]
    [InlineData("FindById", ActionClassification.Query, false)]
    [InlineData("SearchProducts", ActionClassification.Query, false)]
    [InlineData("ListOrders", ActionClassification.Query, false)]
    [InlineData("FetchData", ActionClassification.Query, false)]
    [InlineData("ShowOpenTickets", ActionClassification.Query, false)]
    public void ScoreMethod_QueryVerbs_ClassifiesAsQuery(string methodName, ActionClassification expected, bool isMutation)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.Equal(expected, score.Classification);
        Assert.Equal(isMutation, score.IsMutation);
        Assert.True(score.Score >= 0.5, $"Score {score.Score} should be >= 0.5 for verb match");
    }

    [Theory]
    [InlineData("CreateUser", ActionClassification.Command, true)]
    [InlineData("AddItem", ActionClassification.Command, true)]
    [InlineData("UpdateRecord", ActionClassification.Command, true)]
    [InlineData("DeleteOrder", ActionClassification.Command, true)]
    [InlineData("RemoveItem", ActionClassification.Command, true)]
    [InlineData("SaveOrder", ActionClassification.Command, true)]
    public void ScoreMethod_CommandVerbs_ClassifiesAsCommand(string methodName, ActionClassification expected, bool isMutation)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.Equal(expected, score.Classification);
        Assert.Equal(isMutation, score.IsMutation);
    }

    [Theory]
    [InlineData("SubmitOrder", ActionClassification.Workflow, true)]
    [InlineData("ApproveRequest", ActionClassification.Workflow, true)]
    [InlineData("RejectApplication", ActionClassification.Workflow, true)]
    [InlineData("ProcessPayment", ActionClassification.Workflow, true)]
    [InlineData("ExecuteTask", ActionClassification.Workflow, true)]
    [InlineData("CancelOrder", ActionClassification.Workflow, true)]
    [InlineData("CompleteWorkflow", ActionClassification.Workflow, true)]
    public void ScoreMethod_WorkflowVerbs_ClassifiesAsWorkflow(string methodName, ActionClassification expected, bool isMutation)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.Equal(expected, score.Classification);
        Assert.Equal(isMutation, score.IsMutation);
    }

    [Theory]
    [InlineData("ExportToCsv", ActionClassification.Export, false)]
    [InlineData("GenerateReport", ActionClassification.Export, false)]
    [InlineData("DownloadFile", ActionClassification.Export, false)]
    [InlineData("RenderPdf", ActionClassification.Export, false)]
    public void ScoreMethod_ExportVerbs_ClassifiesAsExport(string methodName, ActionClassification expected, bool isMutation)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.Equal(expected, score.Classification);
        Assert.Equal(isMutation, score.IsMutation);
    }

    [Theory]
    [InlineData("ValidateInput", ActionClassification.Validation, false)]
    [InlineData("CheckPermissions", ActionClassification.Validation, false)]
    [InlineData("VerifySignature", ActionClassification.Validation, false)]
    public void ScoreMethod_ValidationVerbs_ClassifiesAsValidation(string methodName, ActionClassification expected, bool isMutation)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.Equal(expected, score.Classification);
        Assert.Equal(isMutation, score.IsMutation);
    }

    #endregion

    #region Infrastructure Method Exclusion

    [Theory]
    [InlineData("Dispose")]
    [InlineData("DisposeAsync")]
    [InlineData("ToString")]
    [InlineData("GetHashCode")]
    [InlineData("OnInitialized")]
    [InlineData("OnInitializedAsync")]
    [InlineData("OnParametersSet")]
    [InlineData("StateHasChanged")]
    [InlineData("SaveChangesAsync")]
    public void ScoreMethod_InfrastructureMethods_ReturnsZeroScore(string methodName)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.Equal(0, score.Score);
        Assert.Equal(ActionClassification.Infrastructure, score.Classification);
    }

    [Theory]
    [InlineData("HandleClick")]
    [InlineData("HandleSubmit")]
    [InlineData("OnClickCallback")]
    [InlineData("IsValid")]
    [InlineData("HasPermission")]
    [InlineData("CanEdit")]
    [InlineData("TryParse")]
    public void ScoreMethod_InfrastructurePatterns_ReturnsLowScore(string methodName)
    {
        var method = CreateMethod(methodName);
        var score = _scorer.ScoreMethod(method, "TestService");

        Assert.True(score.Score <= 0.2, $"Score {score.Score} should be low for infrastructure pattern");
        Assert.Equal(ActionClassification.Infrastructure, score.Classification);
    }

    #endregion

    #region Score Calculation

    [Fact]
    public void ScoreMethod_AsyncMethod_GetsScoreBoost()
    {
        var syncMethod = CreateMethod("GetUser", isAsync: false);
        var asyncMethod = CreateMethod("GetUserAsync", isAsync: true);

        var syncScore = _scorer.ScoreMethod(syncMethod, "TestService");
        var asyncScore = _scorer.ScoreMethod(asyncMethod, "TestService");

        Assert.True(asyncScore.Score > syncScore.Score, "Async methods should score higher");
    }

    [Fact]
    public void ScoreMethod_WithXmlDoc_GetsScoreBoost()
    {
        var withoutDoc = CreateMethod("GetUser");
        var withDoc = CreateMethod("GetUser", xmlDocSummary: "Gets a user by ID");

        var withoutDocScore = _scorer.ScoreMethod(withoutDoc, "TestService");
        var withDocScore = _scorer.ScoreMethod(withDoc, "TestService");

        Assert.True(withDocScore.Score > withoutDocScore.Score, "Methods with XML docs should score higher");
    }

    [Fact]
    public void ScoreMethod_WithDtoParameter_GetsScoreBoost()
    {
        var withoutDto = CreateMethod("CreateUser", parameters: [CreateParam("name", "string")]);
        var withDto = CreateMethod("CreateUser", parameters: [CreateParam("request", "CreateUserRequest")]);

        var withoutDtoScore = _scorer.ScoreMethod(withoutDto, "TestService");
        var withDtoScore = _scorer.ScoreMethod(withDto, "TestService");

        Assert.True(withDtoScore.Score > withoutDtoScore.Score, "Methods with DTO params should score higher");
    }

    #endregion

    #region Action Model Generation

    [Fact]
    public void ToActionModel_GeneratesCorrectActionName()
    {
        var method = CreateMethod("GetUserByIdAsync");
        var score = _scorer.ScoreMethod(method, "UserService");
        var action = _scorer.ToActionModel(method, CreateService("UserService"), score);

        Assert.Equal("Get User By Id", action.Name);
    }

    [Fact]
    public void ToActionModel_GeneratesCorrectActionId()
    {
        var method = CreateMethod("CreateOrderAsync");
        var score = _scorer.ScoreMethod(method, "OrderService");
        var action = _scorer.ToActionModel(method, CreateService("OrderService"), score);

        Assert.Equal("order.create_order", action.Id);
    }

    [Fact]
    public void ToActionModel_SetsCorrectExposureMode()
    {
        var goodMethod = CreateMethod("CreateUser"); // High score verb
        var badMethod = CreateMethod("DoSomething"); // No verb match

        var goodScore = _scorer.ScoreMethod(goodMethod, "TestService");
        var badScore = _scorer.ScoreMethod(badMethod, "TestService");

        var goodAction = _scorer.ToActionModel(goodMethod, CreateService("TestService"), goodScore);
        var badAction = _scorer.ToActionModel(badMethod, CreateService("TestService"), badScore);

        Assert.Equal(ActionExposureMode.Suggested, goodAction.ExposureMode);
        Assert.Equal(ActionExposureMode.Ignored, badAction.ExposureMode);
    }

    [Fact]
    public void ToActionModel_RequiresApproval_ForMutatingCommands()
    {
        var method = CreateMethod("SendEmailAsync");
        var score = _scorer.ScoreMethod(method, "EmailService");

        var action = _scorer.ToActionModel(method, CreateService("EmailService"), score);

        Assert.True(action.IsMutationLikely);
        Assert.True(action.RequiresApproval);
    }

    #endregion

    #region Helpers

    private static ServiceMethodModel CreateMethod(
        string name,
        bool isAsync = false,
        string? xmlDocSummary = null,
        List<ParameterModel>? parameters = null)
    {
        return new ServiceMethodModel
        {
            Name = name,
            ReturnType = isAsync ? "Task<string>" : "string",
            IsAsync = isAsync || name.EndsWith("Async"),
            IsPublic = true,
            Parameters = parameters ?? [],
            XmlDocSummary = xmlDocSummary
        };
    }

    private static ParameterModel CreateParam(string name, string typeName)
    {
        return new ParameterModel
        {
            Name = name,
            TypeName = typeName,
            IsOptional = false
        };
    }

    private static ServiceModel CreateService(string name)
    {
        return new ServiceModel
        {
            Id = name.ToLowerInvariant(),
            TypeName = name,
            FilePath = $"Services/{name}.cs",
            Methods = []
        };
    }

    #endregion
}
