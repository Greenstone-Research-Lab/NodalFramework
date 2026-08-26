using System.Text.Json;

namespace Nodal.Migrations.Tests;

public sealed class NodalMigrationBundleTests
{
    [Fact]
    public void BundleIsNormalizedChecksummedAndRoundTrips()
    {
        var manifest = Manifest() with
        {
            Requirements = ["SchemaWrite", "AdministrativeTransport", "SchemaWrite"],
        };

        var bundle = NodalMigrationBundleSerializer.Create(manifest);
        var json = NodalMigrationBundleSerializer.Serialize(bundle);
        var restored = NodalMigrationBundleSerializer.Deserialize(json);

        Assert.Equal(NodalMigrationBundle.CurrentFormatVersion, restored.FormatVersion);
        Assert.Equal(["AdministrativeTransport", "SchemaWrite"], restored.Requirements);
        Assert.Equal(64, restored.Checksum.Length);
        Assert.Equal(json, NodalMigrationBundleSerializer.Serialize(restored));
        Assert.Equal(bundle.Checksum, NodalMigrationBundleSerializer.Create(manifest).Checksum);
    }

    [Fact]
    public void ManifestJsonCreatesSameCanonicalBundle()
    {
        const string json = """
            {
              "migrationId": "20260825_001_people",
              "providerName": "Neo4j",
              "providerVersion": "5.26",
              "frameworkVersion": "0.1.0-alpha.1",
              "requirements": ["SchemaWrite"],
              "commands": [
                {
                  "name": "create-index",
                  "text": "CREATE INDEX nodal_people_name IF NOT EXISTS FOR (n:people) ON (n.name)",
                  "transactional": true,
                  "destructive": false
                }
              ]
            }
            """;

        var manifest = NodalMigrationBundleSerializer.DeserializeManifest(json);
        var bundle = NodalMigrationBundleSerializer.Create(manifest);

        Assert.Equal("20260825_001_people", bundle.MigrationId);
        Assert.Equal("Neo4j", bundle.ProviderName);
        Assert.Single(bundle.Commands);
    }

    [Fact]
    public void ChecksumAndFormatDriftAreRejected()
    {
        var bundle = NodalMigrationBundleSerializer.Create(Manifest());

        Assert.Throws<NodalMigrationBundleChecksumException>(() =>
            NodalMigrationBundleSerializer.Serialize(bundle with { ProviderVersion = "changed" }));
        Assert.Throws<NotSupportedException>(() =>
            NodalMigrationBundleSerializer.Serialize(bundle with { FormatVersion = 2 }));
    }

    [Theory]
    [InlineData("CALL setup(password=secret)")]
    [InlineData("access_token:abc123")]
    [InlineData("Authorization = BasicValue")]
    [InlineData("SET header = 'Bearer abc123'")]
    public void CredentialLikeCommandMaterialIsRejected(string command)
    {
        var manifest = Manifest() with
        {
            Commands = [new NodalMigrationBundleCommand("unsafe", command, false, false)],
        };

        Assert.Throws<NodalMigrationBundleSecretException>(() =>
            NodalMigrationBundleSerializer.Create(manifest));
    }

    [Fact]
    public void OrdinaryPasswordPropertyNameIsNotMistakenForASecret()
    {
        var manifest = Manifest() with
        {
            Commands = [new NodalMigrationBundleCommand("property", "CREATE INDEX FOR (n) ON (n.password)", true, false)],
        };

        var bundle = NodalMigrationBundleSerializer.Create(manifest);

        Assert.Single(bundle.Commands);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyManifestJsonIsRejected(string json)
    {
        Assert.Throws<ArgumentException>(() =>
            NodalMigrationBundleSerializer.DeserializeManifest(json));
        Assert.Throws<ArgumentException>(() =>
            NodalMigrationBundleSerializer.Deserialize(json));
    }

    [Fact]
    public void NullAndStructurallyInvalidInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => NodalMigrationBundleSerializer.Create(null!));
        Assert.Throws<ArgumentNullException>(() => NodalMigrationBundleSerializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => NodalMigrationBundleSerializer.DeserializeManifest("{}"));
        Assert.Throws<ArgumentException>(() => NodalMigrationBundleSerializer.Create(Manifest() with { MigrationId = "" }));
        Assert.Throws<ArgumentException>(() => NodalMigrationBundleSerializer.Create(Manifest() with { Requirements = [""] }));
        Assert.Throws<ArgumentException>(() => NodalMigrationBundleSerializer.Create(Manifest() with
        {
            Commands = [new NodalMigrationBundleCommand("", "command", true, false)],
        }));
        Assert.Throws<ArgumentException>(() => NodalMigrationBundleSerializer.Create(Manifest() with
        {
            Commands = [new NodalMigrationBundleCommand("command", "", true, false)],
        }));
    }

    private static NodalMigrationBundleManifest Manifest() => new(
        "20260825_001_people",
        "Neo4j",
        "5.26",
        "0.1.0-alpha.1",
        ["SchemaWrite"],
        [
            new NodalMigrationBundleCommand(
                "create-index",
                "CREATE INDEX nodal_people_name IF NOT EXISTS FOR (n:people) ON (n.name)",
                true,
                false),
        ]);
}
