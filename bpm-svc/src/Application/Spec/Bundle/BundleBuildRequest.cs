using System.Text.Json;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Input payload for <see cref="IBundleBuilder.BuildAsync"/>. All fields
/// are required except the optional asset / chat-snapshot toggles and the
/// parent-spec hand-off (used to drive <c>CHANGELOG.md</c> generation).
///
/// <para>
/// <c>ParentSpecJson</c> intentionally takes the raw spec, NOT the parent
/// bundle zip — PR-I3 (BundleParser) doesn't exist yet, and pulling it in
/// here would bloat this PR. The Flow Library REST layer (PR-I5) will
/// extract the parent's <c>spec.json</c> from its stored zip blob and
/// hand it down via this property once both PRs land.
/// </para>
/// </summary>
public sealed record BundleBuildRequest(
    JsonElement DraftSpecJson,
    string BpmnXml,
    SampleOrgSnapshot SampleOrg,
    IReadOnlyList<TestCaseSnapshot> TestCases,
    bool IncludeAssets = false,
    bool IncludeChatSnapshots = false,
    JsonElement? ParentSpecJson = null,
    string SourceInstanceId = "default");
