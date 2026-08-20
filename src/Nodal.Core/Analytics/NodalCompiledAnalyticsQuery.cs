using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace Nodal.Core.Analytics;

/// <summary>Compiles reusable analytics factories and creates deterministic cache keys.</summary>
public static class NodalCompiledAnalyticsQuery
{
    /// <summary>Compiles a parameterless analytics factory for a context type.</summary>
    public static Func<TContext, GraphAnalyticsQuery<TNode, TRelation>> Compile<TContext, TNode, TRelation>(
        Expression<Func<TContext, GraphAnalyticsQuery<TNode, TRelation>>> query)
        where TContext : NodalContext
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Compile();
    }

    /// <summary>Compiles an analytics factory with one runtime parameter.</summary>
    /// <example><code>
    /// var pageRank = NodalCompiledAnalyticsQuery.Compile((SocialContext db, int count) =&gt;
    ///     db.People.Query().Analyze(db.Friendships).PageRank().Top(count));
    /// var rows = await pageRank(context, 25).ToListAsync();
    /// </code></example>
    public static Func<TContext, TParameter, GraphAnalyticsQuery<TNode, TRelation>> Compile<
        TContext, TParameter, TNode, TRelation>(
        Expression<Func<TContext, TParameter, GraphAnalyticsQuery<TNode, TRelation>>> query)
        where TContext : NodalContext
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Compile();
    }

    /// <summary>Returns a deterministic SHA-256 key for an analytics expression shape.</summary>
    public static string CreateCacheKey(LambdaExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var signature = string.Join('|', query.Parameters.Select(parameter => parameter.Type.AssemblyQualifiedName)) +
            '|' + query.ReturnType.AssemblyQualifiedName + '|' + query;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
    }
}
