using System.Linq.Expressions;

namespace Nodal.Core.Query;

/// <summary>
/// Compiles reusable graph-query factories once so repeated hot-path invocations do not
/// reinterpret the outer context and argument expression.
/// </summary>
public static class NodalCompiledQuery
{
    /// <summary>Compiles a parameterless query factory for a context type.</summary>
    public static Func<TContext, GraphQuery<TNode>> Compile<TContext, TNode>(
        Expression<Func<TContext, GraphQuery<TNode>>> query)
        where TContext : NodalContext
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Compile();
    }

    /// <summary>Compiles a reusable query factory with one runtime parameter.</summary>
    /// <example>
    /// <code>
    /// var byId = NodalCompiledQuery.Compile((SocialContext db, string id) =>
    ///     db.People.Match(person => person.Id == id));
    /// var ada = await byId(context, "person-1").SingleAsync();
    /// </code>
    /// </example>
    public static Func<TContext, TParameter, GraphQuery<TNode>> Compile<TContext, TParameter, TNode>(
        Expression<Func<TContext, TParameter, GraphQuery<TNode>>> query)
        where TContext : NodalContext
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Compile();
    }

    /// <summary>Compiles a reusable query factory with two runtime parameters.</summary>
    public static Func<TContext, TParameter1, TParameter2, GraphQuery<TNode>> Compile<
        TContext,
        TParameter1,
        TParameter2,
        TNode>(
        Expression<Func<TContext, TParameter1, TParameter2, GraphQuery<TNode>>> query)
        where TContext : NodalContext
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Compile();
    }
}
