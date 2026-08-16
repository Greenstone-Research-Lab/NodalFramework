using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Model;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class NodalContextTests
{
    [Fact]
    public async Task ToListAsyncRunsTheCompleteProviderPipeline()
    {
        var provider = new RecordingProvider();
        var context = new SocialGraphContext(provider);

        var people = await context.Persons
            .Match(person => person.Age >= 18)
            .Take(2)
            .ToListAsync();

        Assert.Equal("people", provider.Compiler.Query?.NodeType);
        Assert.Equal("compiled command", provider.Executor.Command?.Text);
        Assert.Equal([new Person("person-42", 24)], people);
        Assert.False(context.Database.SupportsMigrations);
        Assert.Throws<NotSupportedException>(() => context.Database.GetMigrationProvider());
    }

    [Fact]
    public void ModelRequiresAKeyForEveryNode()
    {
        var context = new InvalidGraphContext(new RecordingProvider());

        Assert.Throws<InvalidOperationException>(() => context.Model);
    }

    [Fact]
    public void DatabaseFacadeReturnsConfiguredMigrationProvider()
    {
        var provider = new MigrationProvider();
        var context = new EmptyContext(provider);

        Assert.True(context.Database.SupportsMigrations);
        Assert.Same(provider, context.Database.GetMigrationProvider());
    }

    private sealed class SocialGraphContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> Persons => Set<Person>();

        protected override void OnModelCreating(NodalModelBuilder modelBuilder)
        {
            modelBuilder.Node<Person>().HasName("people").HasKey(person => person.Id);
        }
    }

    private sealed class InvalidGraphContext(IGraphProvider provider) : NodalContext(provider)
    {
        protected override void OnModelCreating(NodalModelBuilder modelBuilder)
        {
            modelBuilder.Node<NodeWithoutKey>();
        }
    }

    private sealed class EmptyContext(IGraphProvider provider) : NodalContext(provider);

    private sealed class MigrationProvider : IGraphProvider, IGraphMigrationProvider
    {
        public bool SupportsMigrationExecution => true;

        public IGraphMigrationDialect MigrationDialect => throw new NotSupportedException();

        public IGraphMigrationExecutor MigrationExecutor => throw new NotSupportedException();

        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }

    private sealed class RecordingProvider : IGraphProvider
    {
        public RecordingProvider()
        {
            Compiler = new RecordingCompiler();
            Executor = new RecordingExecutor();
            Materializer = new RecordingMaterializer();
        }

        public RecordingCompiler Compiler { get; }

        public RecordingExecutor Executor { get; }

        public RecordingMaterializer Materializer { get; }

        IGraphQueryCompiler IGraphProvider.QueryCompiler => Compiler;

        IGraphCommandExecutor IGraphProvider.CommandExecutor => Executor;

        IGraphResultMaterializer IGraphProvider.ResultMaterializer => Materializer;
    }

    private sealed class RecordingCompiler : IGraphQueryCompiler
    {
        public GraphQueryModel? Query { get; private set; }

        public GraphCommand Compile(GraphQueryModel query)
        {
            Query = query;
            return new GraphCommand("compiled command", new Dictionary<string, object?>());
        }
    }

    private sealed class RecordingExecutor : IGraphCommandExecutor
    {
        public GraphCommand? Command { get; private set; }

        public ValueTask<GraphQueryResult> ExecuteAsync(
            GraphCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return ValueTask.FromResult(new GraphQueryResult([]));
        }
    }

    private sealed class RecordingMaterializer : IGraphResultMaterializer
    {
        public IReadOnlyList<TNode> Materialize<TNode>(GraphQueryResult result)
        {
            return (IReadOnlyList<TNode>)(object)new[] { new Person("person-42", 24) };
        }

        public IReadOnlyList<GraphPath<TSource, TRelation, TTarget>> MaterializePaths<TSource, TRelation, TTarget>(
            GraphQueryResult result)
            where TRelation : notnull => throw new NotSupportedException();
    }

    private sealed record Person(string Id, int Age);

    private sealed record NodeWithoutKey(string Name);
}
