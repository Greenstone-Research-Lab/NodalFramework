using Nodal.Core.Modeling;

namespace Nodal.Core.Tests;

public sealed class GraphModelToolingTests
{
    [Fact]
    public void ValidationReturnsStableSuccessWarningAndFailureEvidence()
    {
        var valid = Descriptor();
        var warning = valid with
        {
            Nodes = valid.Nodes.Select((node, index) => index == 0 ? node with
            {
                ProviderAnnotations = new Dictionary<string, string>
                {
                    ["nodal:review"] = "Confirm the synthetic source identity.",
                },
            } : node).ToArray(),
        };

        Assert.True(GraphModelValidation.Validate(valid).IsValid);
        var warningResult = GraphModelValidation.Validate(warning);
        Assert.True(warningResult.IsValid);
        Assert.Equal(GraphModelIssueSeverity.Warning, Assert.Single(warningResult.Issues).Severity);
        var nullResult = GraphModelValidation.Validate(null);
        Assert.False(nullResult.IsValid);
        Assert.Equal("NODAL-MODEL-NULL", Assert.Single(nullResult.Issues).Code);
        Assert.Throws<GraphModelValidationException>(() => nullResult.ThrowIfInvalid());
    }

    [Fact]
    public void ValidationClassifiesVersionNullMemberAndStructuralFailures()
    {
        var descriptor = Descriptor();
        var version = GraphModelValidation.Validate(descriptor with { FormatVersion = "99" });
        var nullMember = GraphModelValidation.Validate(descriptor with { Nodes = null! });
        var structural = GraphModelValidation.Validate(descriptor with { Nodes = [descriptor.Nodes[0], descriptor.Nodes[0]] });

        Assert.Equal("NODAL-MODEL-VERSION", Assert.Single(version.Issues).Code);
        Assert.Equal("NODAL-MODEL-NULL-MEMBER", Assert.Single(nullMember.Issues).Code);
        Assert.Equal("NODAL-MODEL-STRUCTURE", Assert.Single(structural.Issues).Code);
        Assert.Contains("NODAL-MODEL-STRUCTURE", Assert.Throws<GraphModelValidationException>(() => structural.ThrowIfInvalid()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsUnsafeAndCollidingClrNamesAndFindsRelationReviewMarkers()
    {
        var descriptor = Descriptor();
        var invalid = descriptor with
        {
            Nodes =
            [
                descriptor.Nodes[0] with
                {
                    ClrName = "class",
                    Properties =
                    [
                        descriptor.Nodes[0].Properties[0] with { ClrName = "bad-name" },
                        descriptor.Nodes[0].Properties[1] with { ClrName = "bad-name" },
                    ],
                },
                descriptor.Nodes[1] with { ClrName = "class" },
                descriptor.Nodes[2],
            ],
            Relations = [descriptor.Relations[0] with
            {
                ProviderAnnotations = new Dictionary<string, string> { ["review.required"] = "true" },
            }],
        };

        var result = GraphModelValidation.Validate(invalid);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "NODAL-MODEL-CLR-NAME");
        Assert.Contains(result.Issues, issue => issue.Code == "NODAL-MODEL-CLR-COLLISION");
        Assert.Contains(result.Issues, issue => issue.Severity == GraphModelIssueSeverity.Warning && issue.Path.Contains("relations", StringComparison.Ordinal));
    }

    [Fact]
    public void InspectorReportsCanonicalEvidence()
    {
        var descriptor = Descriptor();

        var inspection = GraphModelInspector.Inspect(descriptor);

        Assert.Equal(GraphModelFormat.CurrentVersion, inspection.FormatVersion);
        Assert.Equal(GraphModelDescriptorJson.ComputeFingerprint(descriptor), inspection.Fingerprint);
        Assert.Equal(3, inspection.NodeCount);
        Assert.Equal(1, inspection.RelationCount);
        Assert.Equal(5, inspection.PropertyCount);
        Assert.Equal(0, inspection.CompositeKeyCount);
        Assert.Equal(0, inspection.ReviewCount);
    }

    [Fact]
    public void DifferClassifiesAdditionsRemovalsAndContractChanges()
    {
        var before = Descriptor();
        var person = before.Nodes[0];
        var after = before with
        {
            Nodes =
            [
                person with
                {
                    ClrName = "Customer",
                    Key = new GraphKeyDescriptor(["id", "name"]),
                    Properties =
                    [
                        person.Properties[0] with { IsNullable = true },
                        person.Properties[1] with { ValueKind = GraphValueKind.Categorical },
                        new GraphPropertyDescriptor("nickname", "Nickname", GraphValueKind.Text, true),
                    ],
                },
                before.Nodes[1],
                new NodeTypeDescriptor(
                    "country", "Country", "Country", new GraphKeyDescriptor(["id"]),
                    [new GraphPropertyDescriptor("id", "Id", GraphValueKind.Text, false)]),
            ],
            Relations = [before.Relations[0] with { Directed = false }],
        };

        var diff = GraphModelDiffer.Compare(before, after);

        Assert.True(diff.HasBreakingChanges);
        Assert.Contains(diff.Changes, change => change.Kind == GraphModelChangeKind.NodeAdded && change.Impact == GraphModelChangeImpact.NonBreaking);
        Assert.Contains(diff.Changes, change => change.Kind == GraphModelChangeKind.NodeRemoved);
        Assert.Contains(diff.Changes, change => change.Kind == GraphModelChangeKind.KeyChanged);
        Assert.Contains(diff.Changes, change => change.Kind == GraphModelChangeKind.RelationShapeChanged);
        Assert.Contains(diff.Changes, change => change.Kind == GraphModelChangeKind.PropertyAdded && change.Impact == GraphModelChangeImpact.NonBreaking);
        Assert.Contains(diff.Changes, change => change.Kind == GraphModelChangeKind.PropertyChanged && change.Impact == GraphModelChangeImpact.NonBreaking);
        Assert.Equal(diff.Changes.OrderBy(change => change.Path, StringComparer.Ordinal).ThenBy(change => change.Kind), diff.Changes);
    }

    [Fact]
    public void DifferReportsNoChangesAndRequiredPropertyAdditionAsBreaking()
    {
        var descriptor = Descriptor();
        var unchanged = GraphModelDiffer.Compare(descriptor, descriptor);
        var node = descriptor.Nodes[0];
        var changed = descriptor with
        {
            Nodes = [node with
            {
                Properties = node.Properties.Append(new GraphPropertyDescriptor(
                    "required", "Required", GraphValueKind.Boolean, false)).ToArray(),
            }, descriptor.Nodes[1]],
        };

        Assert.True(unchanged.IsEmpty);
        Assert.False(unchanged.HasBreakingChanges);
        Assert.Contains(GraphModelDiffer.Compare(descriptor, changed).Changes, change =>
            change.Kind == GraphModelChangeKind.PropertyAdded && change.Impact == GraphModelChangeImpact.Breaking);
    }

    private static GraphModelDescriptor Descriptor() => new(
        GraphModelFormat.CurrentVersion,
        [
            new NodeTypeDescriptor(
                "person", "Person", "Person", new GraphKeyDescriptor(["id"]),
                [
                    new GraphPropertyDescriptor("id", "Id", GraphValueKind.Text, false),
                    new GraphPropertyDescriptor("name", "Name", GraphValueKind.Text, false),
                ]),
            new NodeTypeDescriptor(
                "company", "Company", "Company", new GraphKeyDescriptor(["id"]),
                [new GraphPropertyDescriptor("id", "Id", GraphValueKind.Text, false)]),
            new NodeTypeDescriptor(
                "obsolete", "Obsolete", "Obsolete", new GraphKeyDescriptor(["id"]),
                [new GraphPropertyDescriptor("id", "Id", GraphValueKind.Text, false)]),
        ],
        [new RelationTypeDescriptor(
            "works-at", "WORKS_AT", "WorksAt", "person", "company", true,
            [new GraphPropertyDescriptor("since", "Since", GraphValueKind.Date, true)])]);
}
