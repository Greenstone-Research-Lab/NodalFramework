using Nodal.Analytics.Observations;

namespace Nodal.Analytics.Tests;

public sealed class GraphObservationKeyTests
{
    public static TheoryData<object, string, string> SupportedIdentities => new()
    {
        { "42", "string", "42" },
        { (sbyte)-1, "integer", "-1" },
        { (byte)2, "integer", "2" },
        { (short)-3, "integer", "-3" },
        { (ushort)4, "integer", "4" },
        { -5, "integer", "-5" },
        { 6U, "integer", "6" },
        { -7L, "integer", "-7" },
        { 8UL, "integer", "8" },
    };

    [Theory]
    [MemberData(nameof(SupportedIdentities))]
    public void SupportedIdentitiesAreCultureIndependent(object value, string expectedKind, string expectedValue)
    {
        var key = GraphObservationKey.From(value);

        Assert.Equal(expectedKind, key.Kind);
        Assert.Equal(expectedValue, key.Value);
        Assert.Equal($"{expectedKind}:{expectedValue}", key.ToString());
    }

    [Fact]
    public void GuidIdentityUsesStableDFormat()
    {
        var value = Guid.Parse("4A0123C2-8E11-49D0-9147-7217EFA19231");

        var key = GraphObservationKey.From(value);

        Assert.Equal("guid", key.Kind);
        Assert.Equal("4a0123c2-8e11-49d0-9147-7217efa19231", key.Value);
    }

    [Fact]
    public void NullIdentityIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => GraphObservationKey.From(null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyStringIdentityIsRejected(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => GraphObservationKey.From(value));

        Assert.Equal("identity", exception.ParamName);
    }

    [Fact]
    public void NodeIdentityRejectsInvalidPublicConstruction()
    {
        var key = GraphObservationKey.From("node-1");

        Assert.Throws<ArgumentException>(() => new GraphObservationNodeIdentity(" ", key));
        Assert.Throws<ArgumentNullException>(() => new GraphObservationNodeIdentity("Food", null!));
    }

    [Fact]
    public void UnsupportedIdentityTypeIsRejectedWithoutFormattingItsValue()
    {
        var exception = Assert.Throws<ArgumentException>(() => GraphObservationKey.From(1.5m));

        Assert.Contains(typeof(decimal).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1.5", exception.Message, StringComparison.Ordinal);
    }
}
