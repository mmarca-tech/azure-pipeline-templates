using System.Text.RegularExpressions;

namespace PipelineTools.ReleaseTools.Tests.Yaml;

public class ReleaseProcessContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void CreateTicketCondition_default_includes_succeeded()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");
        var stages = ReadTemplate("release-process-stages.yaml");

        Assert.Contains("and(succeeded()", create);
        Assert.Contains("- name: createTicketCondition", create);
        Assert.Matches(
            @"- name: createTicketCondition\s*\n\s*type: string\s*\n\s*default: ''",
            create);
        Assert.Contains("eq( variables['isMainBranch'], 'true' )", create);
        Assert.Contains("ne( variables['isPullRequest'],  true )", create);
        Assert.Contains("createTicketCondition: ${{ parameters.createTicketCondition }}", stages);
    }

    [Fact]
    public void Release_branch_opt_in_preserves_override_precedence_and_pins_source_commit()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");
        var stages = ReadTemplate("release-process-stages.yaml");

        foreach (var template in new[] { create, stages })
        {
            Assert.Matches(@"- name: releaseBranchCondition\s*\n\s*type: string\s*\n\s*default: ''", template);
        }

        Assert.Contains("releaseBranchCondition: ${{ parameters.releaseBranchCondition }}", stages);
        Assert.Contains("${{ if ne(parameters.createTicketCondition, '') }}:", create);
        Assert.Contains("condition: ${{ parameters.createTicketCondition }}", create);
        Assert.Contains("${{ elseif ne(parameters.releaseBranchCondition, '') }}:", create);
        Assert.Contains("condition: and(succeeded(), ${{ parameters.releaseBranchCondition }}, ne(variables['Build.Reason'], 'PullRequest'))", create);
        Assert.Contains("-TargetCommit \"$(Build.SourceVersion)\"", create);
    }

    [Fact]
    public void Reporter_comes_from_creation_approval_unless_manual_approval_is_skipped()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");

        Assert.Contains("-ReporterSource \"${{ iif(parameters.skipManualApproval, 'Configured', 'ManualApproval') }}\"", create);
        Assert.Contains("-Reporter \"${{ parameters.reporter }}\"", create);
        Assert.Contains("SYSTEM_ACCESSTOKEN: $(System.AccessToken)", create);
        Assert.Contains("onTimeout: 'reject'", create);
    }

    [Fact]
    public void Wrapper_master_switch_uses_successful_bridges_between_deployments()
    {
        var stages = ReadTemplate("release-process-stages.yaml");
        var pipeline = File.ReadAllText(Path.Combine(RepoRoot, "azure-pipelines.release-process-test.yml"));

        Assert.Matches(
            @"- name: releaseProcessEnabled\s*\n\s*type: boolean\s*\n\s*default: true",
            stages);
        Assert.Contains("${{ if eq(parameters.releaseProcessEnabled, true) }}", stages);
        Assert.Contains("${{ else }}", stages);
        Assert.Contains("job: release_process_disabled_before_pt", stages);
        Assert.Contains("job: release_process_disabled_before_live", stages);
        Assert.Contains("condition: succeeded()", stages);
        Assert.DoesNotContain(
            "createTicketCondition: and(${{ parameters.releaseProcessEnabled }}, ${{ parameters.createTicketCondition }})",
            stages);
        Assert.Contains("releaseProcessEnabled: ${{ parameters.releaseProcessEnabled }}", pipeline);
    }

    [Fact]
    public void Secret_outputs_mark_token_and_webhook_as_issecret()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");
        Assert.Contains("variable=jiraBotToken;isOutput=true;issecret=true", create);
        Assert.Contains("variable=jiraBotEmail;isOutput=true;issecret=true", create);
        Assert.Contains("variable=releaseProcessWebhookUrl;isOutput=true;issecret=true", create);
        Assert.DoesNotContain("variable=jiraCloudId;isOutput=true;issecret=true", create);
    }

    [Fact]
    public void Jira_secrets_are_fetched_inside_create_ticket_stage()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");

        Assert.DoesNotContain("stage: Get_Jira_Secrets", create);
        Assert.Contains("stage: release_process_create_ticket", create);
        Assert.Contains("job: GetSecrets", create);
        Assert.Contains("dependencies.GetSecrets.outputs", create);
        Assert.Contains("- GetSecrets", create);
        Assert.DoesNotContain("stageDependencies.Get_Jira_Secrets", create);
    }

    [Fact]
    public void Cancel_stage_depends_on_ticket_creation_and_requires_approval()
    {
        var cancel = ReadTemplate("release-process-cancel-release.yaml");
        Assert.Contains("dependsOn: release_process_create_ticket", cancel);
        Assert.Contains("dependencies.release_process_create_ticket.result", cancel);
        Assert.Contains("job: approve_cancel_release", cancel);
        Assert.Contains("ManualValidation@1", cancel);
        Assert.Contains("dependsOn: approve_cancel_release", cancel);
    }

    [Fact]
    public void SkipManualApproval_gates_create_and_update_stakeholders_only()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");
        var update = ReadTemplate("release-process-update-stakeholders.yaml");
        var implementing = ReadTemplate("release-process-implementing.yaml");
        var deployed = ReadTemplate("release-process-release-deployed.yaml");
        var cancel = ReadTemplate("release-process-cancel-release.yaml");

        Assert.Contains("parameters.skipManualApproval", create);
        Assert.Contains("parameters.skipManualApproval", update);
        Assert.DoesNotContain("skipManualApproval", implementing);
        Assert.DoesNotContain("skipManualApproval", deployed);
        Assert.DoesNotContain("skipManualApproval", cancel);
        Assert.Contains("ManualValidation@1", implementing);
        Assert.Contains("ManualValidation@1", deployed);
        Assert.Contains("ManualValidation@1", cancel);
    }

    [Fact]
    public void Review_acknowledgment_advances_to_go_live_and_implementing_enforces_jira_approval()
    {
        var awaiting = ReadTemplate("release-process-awaiting-implementation.yaml");
        var implementing = ReadTemplate("release-process-implementing.yaml");
        Assert.Contains("displayName: \"Acknowledge Review and Wait for Go Live Approval\"", awaiting);
        Assert.Contains("check-jira-review-acknowledgement", awaiting);
        Assert.Contains("ManualValidation@1", awaiting);
        Assert.Contains("advance-jira-to-go-live-decision", awaiting);
        Assert.Contains("check-jira-awaiting-implementation-status", awaiting);
        Assert.Contains("-FailIfBlocked", awaiting);
        Assert.Contains("recheck_awaiting_status", awaiting);
        Assert.Contains("Awaiting implementation", awaiting);
        Assert.DoesNotContain("transition-jira-ticket", awaiting);
        Assert.DoesNotContain("check-jira-awaiting-implementation-approval", awaiting);
        Assert.Contains("check-jira-awaiting-implementation-status", implementing);
        Assert.Contains("-FailIfBlocked", implementing);
        Assert.Matches(
            @"Require Jira manual release approval[\s\S]*?Transition JIRA Ticket",
            implementing);
    }

    [Fact]
    public void Create_ticket_defaults_to_Normal_change_type_and_forwards_serviceImpact()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");
        var stages = ReadTemplate("release-process-stages.yaml");
        Assert.Contains("default: 'Normal'", create);
        Assert.Contains("default: 'Normal'", stages);
        Assert.Contains("- name: serviceImpact", create);
        Assert.Contains("-ServiceImpact \"${{ parameters.serviceImpact }}\"", create);
        Assert.Contains("serviceImpact: ${{ parameters.serviceImpact }}", stages);
    }

    [Fact]
    public void Create_ticket_requires_and_forwards_approver()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");
        var stages = ReadTemplate("release-process-stages.yaml");

        Assert.Matches(@"- name: approver\s*\n\s*type: string", create);
        Assert.Matches(@"- name: approver\s*\n\s*type: string", stages);
        Assert.Contains("-Approver \"${{ parameters.approver }}\"", create);
        Assert.Contains("approver: ${{ parameters.approver }}", stages);
    }

    [Fact]
    public void Release_process_test_pipeline_uses_wrapper_and_placeholder_deployments()
    {
        var pipeline = File.ReadAllText(Path.Combine(RepoRoot, "azure-pipelines.release-process-test.yml"));

        Assert.Contains("templates/release-process-stages.yaml@pipeline-templates", pipeline);
        Assert.Contains("ref: ${{ variables['Build.SourceBranch'] }}", pipeline);
        Assert.DoesNotContain("refs/heads/feature/itsm-ticket-approver", pipeline);
        Assert.Matches(
            @"- name: skipManualApproval\s*\n\s*displayName: Skip supported manual approval steps\s*\n\s*type: boolean\s*\n\s*default: false",
            pipeline);
        Assert.Contains("skipManualApproval: ${{ parameters.skipManualApproval }}", pipeline);
        Assert.Contains("approver: '5eeaff58b5ccf20ab39e3b34'", pipeline);
        Assert.Contains("stage: DeployTest", pipeline);
        Assert.Contains("stage: DeployPT", pipeline);
        Assert.Contains("stage: DeployLive", pipeline);
    }

    [Fact]
    public void Create_ticket_manual_approval_precedes_ticket_creation()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");

        Assert.Contains("job: approve_release_ticket", create);
        Assert.Contains("task: ManualValidation@1", create);
        Assert.Matches(
            @"job: approve_release_ticket[\s\S]*?dependsOn: GetSecrets[\s\S]*?condition: succeeded\(\)",
            create);
        Assert.Matches(
            @"job: create_release_ticket[\s\S]*?dependsOn:[\s\S]*?- GetSecrets[\s\S]*?- approve_release_ticket",
            create);
    }

    [Fact]
    public void Create_ticket_template_checkout_has_authenticated_full_tag_history()
    {
        var create = ReadTemplate("release-process-create-ticket.yaml");

        Assert.Matches(
            @"checkout: \$\{\{ parameters\.templateRepo \}\}[\s\S]*?persistCredentials: true[\s\S]*?fetchDepth: 0[\s\S]*?fetchTags: true",
            create);
    }

    [Fact]
    public void Implementing_and_failed_set_actual_dates()
    {
        var implementing = ReadTemplate("release-process-implementing.yaml");
        var failed = ReadTemplate("release-process-release-failed.yaml");
        Assert.Contains("-SetActualStartDate", implementing);
        Assert.Contains("-SetActualEndDate", failed);
        Assert.Contains("-PostImplementationStatus", failed);
    }

    [Fact]
    public void Wrapper_forwards_dependsOn_and_failedTransitionId()
    {
        var stages = ReadTemplate("release-process-stages.yaml");
        Assert.Contains("failedTransitionId: ${{ parameters.failedTransitionId }}", stages);
        Assert.Contains("deployedStageDependsOn: ${{ parameters.deployedStageDependsOn }}", stages);
        Assert.Contains("failedStageDependsOn: ${{ parameters.failedStageDependsOn }}", stages);
        Assert.Contains("dependsOn: ${{ parameters.awaitingImplementationDependsOn }}", stages);
        Assert.Contains("createTicketDependsOn: ${{ parameters.createTicketDependsOn }}", stages);
        Assert.Contains("${{ parameters.createTicketCondition }}", stages);
    }

    [Fact]
    public void Release_process_templates_reference_existing_ReleaseTools_launcher()
    {
        var templatesDir = Path.Combine(RepoRoot, "templates");
        var releaseTemplates = Directory.GetFiles(templatesDir, "release-process-*.yaml");
        Assert.NotEmpty(releaseTemplates);

        var launcherPath = Path.Combine(RepoRoot, "scripts", "ReleaseProcess.cs");
        Assert.True(File.Exists(launcherPath));
        Assert.Contains(
            releaseTemplates,
            template => File.ReadAllText(template).Contains("scripts/ReleaseProcess.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Deployed_update_runs_after_update_released_tickets_on_succeeded()
    {
        var deployed = ReadTemplate("release-process-release-deployed.yaml");
        Assert.Contains("update-released-tickets", deployed);
        Assert.Contains("update_stakeholders_deployed", deployed);
        Assert.Contains("condition: succeeded()", deployed);
    }

    private static string ReadTemplate(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "templates", fileName));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "templates")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests", "PipelineTemplates.Tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
