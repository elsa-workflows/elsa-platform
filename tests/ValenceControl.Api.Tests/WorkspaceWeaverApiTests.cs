using System.Net;
using ValenceControl.Api.Workspace;
using ValenceControl.Weaver.Core.Configuration;
using ValenceControl.Weaver.Core.Sessions;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

public sealed class WorkspaceWeaverApiTests
{
    [Fact]
    public async Task Configuration_reports_disabled_by_default()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient();
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/weaver/configuration");
        var configuration = await response.Content.ReadControlJsonAsync<WorkspaceWeaverConfigurationResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        configuration!.Enabled.Should().BeFalse();
        configuration.ProviderMode.Should().Be(WeaverProviderMode.Disabled);
        configuration.DisabledReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Workspace_member_can_create_session_and_send_prompt_with_fake_provider()
    {
        await using var app = new ControlApiTestApplication(FakeWeaverConfiguration());
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("weaver-member");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();

        var createResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/sessions",
            new WorkspaceWeaverCreateSessionRequest(
                "/admin/deployments",
                WeaverMode.Inspect,
                new Dictionary<string, string> { ["applicationId"] = Guid.NewGuid().ToString("D") }));
        var session = await createResponse.Content.ReadControlJsonAsync<WorkspaceWeaverSessionResponse>();
        var messageResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/sessions/{session!.Id}/messages",
            new WorkspaceWeaverSendMessageRequest("What is wrong here? token: ghp_secret", WeaverMode.Inspect, "Immediate"));
        var message = await messageResponse.Content.ReadControlJsonAsync<WorkspaceWeaverSendMessageResponse>();
        var detail = await client.GetControlJsonAsync<WorkspaceWeaverSessionDetailResponse>(
            $"/api/workspaces/{workspaceId}/weaver/sessions/{session.Id}");

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        messageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        message!.AssistantMessageId.Should().NotBeNull();
        detail!.Messages.Should().HaveCount(2);
        detail.Messages.Single(x => x.Role == WeaverMessageRole.User).Content.Should().Contain("[REDACTED]");
        detail.Messages.Single(x => x.Role == WeaverMessageRole.User).Content.Should().NotContain("ghp_secret");
        detail.Messages.Single(x => x.Role == WeaverMessageRole.Assistant).Content.Should().Contain("Mode: Inspect");
        detail.ToolCalls.Should().ContainSingle(x => x.ToolName == "get_current_context" && x.Status == WeaverToolCallStatus.Succeeded);
    }

    [Fact]
    public async Task Weaver_routes_reject_non_members()
    {
        await using var app = new ControlApiTestApplication(FakeWeaverConfiguration());
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = app.CreateTrustedWorkspaceClient("weaver-owner");
        var workspaceId = await member.GetDefaultWorkspaceIdAsync();

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/weaver/configuration");
        var nonMember = await app.CreateTrustedWorkspaceClient("weaver-other").GetAsync($"/api/workspaces/{workspaceId}/weaver/configuration");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Plan_mode_prompt_creates_readable_draft_plan()
    {
        await using var app = new ControlApiTestApplication(FakeWeaverConfiguration());
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("weaver-planner");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        var createResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/sessions",
            new WorkspaceWeaverCreateSessionRequest("/admin/deployments", WeaverMode.Plan, new Dictionary<string, string>()));
        var session = await createResponse.Content.ReadControlJsonAsync<WorkspaceWeaverSessionResponse>();

        var messageResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/sessions/{session!.Id}/messages",
            new WorkspaceWeaverSendMessageRequest("Prepare a promotion plan for Production.", WeaverMode.Plan, "Immediate"));
        var detail = await client.GetControlJsonAsync<WorkspaceWeaverSessionDetailResponse>(
            $"/api/workspaces/{workspaceId}/weaver/sessions/{session.Id}");
        var plan = detail!.Plans.Single();
        var planDetail = await client.GetControlJsonAsync<WorkspaceWeaverPlanResponse>(
            $"/api/workspaces/{workspaceId}/weaver/plans/{plan.Id}");

        messageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detail.Plans.Should().ContainSingle(x =>
            x.PlanType == WeaverPlanType.Promotion &&
            x.Status == WeaverPlanStatus.ReadyForApproval &&
            x.Title == "Draft promotion plan");
        planDetail!.Id.Should().Be(plan.Id);
        planDetail.Title.Should().Be("Draft promotion plan");
    }

    [Fact]
    public async Task Owner_can_approve_and_execute_plan_idempotently()
    {
        await using var app = new ControlApiTestApplication(FakeWeaverConfiguration());
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("weaver-executor");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        var createResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/sessions",
            new WorkspaceWeaverCreateSessionRequest("/admin/deployments", WeaverMode.Plan, new Dictionary<string, string>()));
        var session = await createResponse.Content.ReadControlJsonAsync<WorkspaceWeaverSessionResponse>();
        await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/sessions/{session!.Id}/messages",
            new WorkspaceWeaverSendMessageRequest("Prepare a promotion plan for Production.", WeaverMode.Plan, "Immediate"));
        var detail = await client.GetControlJsonAsync<WorkspaceWeaverSessionDetailResponse>(
            $"/api/workspaces/{workspaceId}/weaver/sessions/{session.Id}");
        var plan = detail!.Plans.Single();

        var approvalResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/plans/{plan.Id}/approvals",
            new WorkspaceWeaverPlanApprovalRequest(plan.Version, WeaverPlanApprovalDecision.Approved, null, null));
        var approval = await approvalResponse.Content.ReadControlJsonAsync<WorkspaceWeaverPlanApprovalResponse>();
        var firstExecuteResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/plans/{plan.Id}/execute",
            new WorkspaceWeaverPlanExecuteRequest(plan.Version));
        var firstExecution = await firstExecuteResponse.Content.ReadControlJsonAsync<WorkspaceWeaverPlanExecuteResponse>();
        var secondExecuteResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/weaver/plans/{plan.Id}/execute",
            new WorkspaceWeaverPlanExecuteRequest(plan.Version));
        var secondExecution = await secondExecuteResponse.Content.ReadControlJsonAsync<WorkspaceWeaverPlanExecuteResponse>();

        approvalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        approval!.Status.Should().Be(WeaverPlanStatus.Approved);
        firstExecuteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondExecuteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstExecution!.Status.Should().Be(WeaverPlanExecutionStatus.Succeeded);
        secondExecution!.ExecutionId.Should().Be(firstExecution.ExecutionId);
    }

    private static IReadOnlyDictionary<string, string?> FakeWeaverConfiguration() => new Dictionary<string, string?>
    {
        ["Weaver:Enabled"] = "true",
        ["Weaver:ProviderMode"] = "Fake",
        ["Weaver:Model"] = "gpt-5"
    };
}
