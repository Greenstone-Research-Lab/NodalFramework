using System.Globalization;
using Nodal.Core.Modeling;

namespace Nodal.Core.Tests;

public sealed class GraphModelDescriptorTests
{
    [Fact]
    public void CanonicalJsonAndFingerprintIgnoreInputOrderingAndCulture()
    {
        var first = Descriptor(reverse: false);
        var second = Descriptor(reverse: true);
        using var culture = new CultureScope("tr-TR");

        Assert.Equal(GraphModelDescriptorJson.Serialize(first), GraphModelDescriptorJson.Serialize(second));
        Assert.Equal(GraphModelDescriptorJson.ComputeFingerprint(first), GraphModelDescriptorJson.ComputeFingerprint(second));
    }

    [Fact]
    public void JsonRoundTripRetainsCanonicalDescriptor()
    {
        var descriptor = Descriptor(reverse: true);
        var json = GraphModelDescriptorJson.Serialize(descriptor);

        var restored = GraphModelDescriptorJson.Deserialize(json);

        Assert.Equal(json, GraphModelDescriptorJson.Serialize(restored));
        Assert.Equal(
            GraphModelDescriptorJson.ComputeFingerprint(descriptor),
            GraphModelDescriptorJson.ComputeFingerprint(restored));
        Assert.Contains("\"valueKind\": \"Text\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PortableValues))]
    public void GraphValuePreservesPortableClrKinds(object? value, GraphValueKind expected)
    {
        Assert.Equal(expected, GraphValue.From(value).Kind);
    }

    [Fact]
    public void GraphValueDefensivelyCopiesVectorsCollectionsAndObjects()
    {
        var vector = new List<double> { 1, 2 };
        var items = new List<GraphValue> { GraphValue.From("a") };
        var properties = new Dictionary<string, GraphValue> { ["name"] = GraphValue.From("n") };

        var vectorValue = GraphValue.Vector(vector);
        var collectionValue = GraphValue.From(items);
        var objectValue = GraphValue.From(properties);
        vector.Add(3);
        items.Add(GraphValue.From("b"));
        properties.Add("other", GraphValue.From("x"));

        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<double>>(vectorValue.Value).Count);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GraphValue>>(collectionValue.Value));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyDictionary<string, GraphValue>>(objectValue.Value));
        Assert.Throws<ArgumentNullException>(() => GraphValue.Vector(null!));
        Assert.Throws<NotSupportedException>(() => GraphValue.From(new Version(1, 0)));
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void GeoPointRejectsOutOfRangeCoordinates(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphGeoPoint(latitude, longitude));
    }

    [Fact]
    public void ValidatorRejectsBrokenDescriptorInvariants()
    {
        var descriptor = Descriptor(false);
        Assert.Throws<ArgumentNullException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(null!));
        Assert.Throws<NotSupportedException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { FormatVersion = "2" }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Nodes = [descriptor.Nodes[0], descriptor.Nodes[0]] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Relations = [descriptor.Relations[0] with { TargetNodeId = "missing" }] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Nodes = [descriptor.Nodes[0] with { Key = new GraphKeyDescriptor(["missing"]) }, descriptor.Nodes[1]] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Nodes = [descriptor.Nodes[0] with { Properties = [new GraphPropertyDescriptor("tags", "Tags", GraphValueKind.Collection, false, true)] }, descriptor.Nodes[1]] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Nodes = [descriptor.Nodes[0] with { Properties = [new GraphPropertyDescriptor("tags", "Tags", GraphValueKind.Text, false, true, GraphValueKind.Text)] }, descriptor.Nodes[1]] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Nodes = [descriptor.Nodes[0] with { Properties = [new GraphPropertyDescriptor("tags", "Tags", GraphValueKind.Collection, false, true, GraphValueKind.Collection)] }, descriptor.Nodes[1]] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorValidator.ThrowIfInvalid(descriptor with { Nodes = [descriptor.Nodes[0] with { Id = " " }, descriptor.Nodes[1]] }));
        Assert.Throws<ArgumentException>(() => GraphModelDescriptorJson.Deserialize(" "));
    }

    public static TheoryData<object?, GraphValueKind> PortableValues => new()
    {
        { null, GraphValueKind.Null },
        { "text", GraphValueKind.Text },
        { 'x', GraphValueKind.Character },
        { -1, GraphValueKind.SignedInteger },
        { 1UL, GraphValueKind.UnsignedInteger },
        { 1.25m, GraphValueKind.DecimalNumber },
        { 1.5f, GraphValueKind.FloatingPoint },
        { true, GraphValueKind.Boolean },
        { Guid.Empty, GraphValueKind.Identifier },
        { new DateOnly(2026, 8, 30), GraphValueKind.Date },
        { new TimeOnly(12, 30), GraphValueKind.Time },
        { new DateTime(2026, 8, 30), GraphValueKind.DateTime },
        { DateTimeOffset.UnixEpoch, GraphValueKind.DateTimeOffset },
        { DayOfWeek.Monday, GraphValueKind.Categorical },
        { new GraphGeoPoint(59.437, 24.7536), GraphValueKind.GeoPoint },
    };

    private static GraphModelDescriptor Descriptor(bool reverse)
    {
        var id = new GraphPropertyDescriptor("id", "Id", GraphValueKind.Identifier, false);
        var name = new GraphPropertyDescriptor("name", "Name", GraphValueKind.Text, false,
            ProviderAnnotations: new Dictionary<string, string> { ["z"] = "2", ["a"] = "1" });
        var customer = new NodeTypeDescriptor("customer", "Customer", "Customer", new GraphKeyDescriptor(["id"]),
            reverse ? [name, id] : [id, name]);
        var order = new NodeTypeDescriptor("order", "Order", "Order", new GraphKeyDescriptor(["id"]), [id]);
        var relation = new RelationTypeDescriptor("placed", "PLACED", "Placed", "customer", "order", true, []);
        return new GraphModelDescriptor(GraphModelFormat.CurrentVersion,
            reverse ? [order, customer] : [customer, order], [relation]);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo original = CultureInfo.CurrentCulture;

        public CultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        public void Dispose() => CultureInfo.CurrentCulture = original;
    }
}
