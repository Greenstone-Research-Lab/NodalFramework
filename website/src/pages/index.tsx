import Head from '@docusaurus/Head';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import styles from './index.module.css';

const queryExample = `var people = await context.People
    .Match(person => person.Name.StartsWith("Ada"))
    .OrderBy(person => person.Name)
    .Take(10)
    .ToListAsync();`;

const structuredData = {
  '@context': 'https://schema.org',
  '@graph': [
    {
      '@type': 'SoftwareSourceCode',
      name: 'Nodal Framework',
      codeRepository: 'https://github.com/Greenstone-Research-Lab/NodalFramework',
      programmingLanguage: 'C#',
      runtimePlatform: '.NET 10',
      license: 'https://opensource.org/license/mit',
      description: 'Provider-neutral graph data access framework for .NET with Neo4j and TigerGraph providers.',
    },
    {
      '@type': 'WebSite',
      name: 'Nodal Framework Documentation',
      url: 'https://nodalframework.pages.dev/',
      inLanguage: 'en',
    },
  ],
};

export default function Home() {
  return (
    <Layout title="Graph data access for .NET" description="Build provider-neutral graph applications for Neo4j and TigerGraph with strongly typed .NET APIs.">
      <Head>
        <script type="application/ld+json">{JSON.stringify(structuredData)}</script>
      </Head>
      <main>
        <section className={styles.hero}>
          <div className={styles.heroCopy}>
            <span className={styles.eyebrow}>Provider-neutral · Strongly typed · Open source</span>
            <h1>One graph model.<br /><span>Multiple engines.</span></h1>
            <p>Query, traverse, track, mutate, and migrate graph data with a fluent .NET API. Choose Neo4j or TigerGraph without leaking provider response shapes into your domain.</p>
            <div className={styles.actions}>
              <Link className="button button--primary button--lg" to="/docs/getting-started">Start building</Link>
              <Link className="button button--secondary button--lg" to="/docs/architecture">Explore the architecture</Link>
            </div>
          </div>
          <div className={styles.codePanel}>
            <div className={styles.codeHeader}><span /> <span /> <span /><strong>Provider-neutral query</strong></div>
            <CodeBlock language="csharp">{queryExample}</CodeBlock>
          </div>
        </section>
        <section className={styles.features}>
          <article><span>01</span><h2>Model naturally</h2><p>Map node and relationship POCOs with portable attributes, conventions, or fluent configuration.</p></article>
          <article><span>02</span><h2>Query fluently</h2><p>Compose parameterized filters, ordering, paging, aggregates, and graph-native traversals.</p></article>
          <article><span>03</span><h2>Change safely</h2><p>Use identity resolution, change tracking, ordered mutation plans, and provider-aware transactions.</p></article>
        </section>
        <section className={styles.providerStrip}>
          <div><small>FIRST-CLASS PROVIDERS</small><h2>Cypher and GSQL behind the same domain model.</h2></div>
          <div className={styles.providerNames}><strong>Neo4j</strong><strong>TigerGraph</strong></div>
        </section>
        <section className={styles.analyticsShell}>
          <div className={styles.analyticsVisual}>
            <img src="/img/journal/pattern-recognition-analytics-shell.png" alt="Provider graph streams entering a shared pattern recognition analytics shell" />
          </div>
          <div className={styles.analyticsCopy}>
            <small>EXPERIMENTAL · P3</small>
            <h2>Pattern intelligence above every provider.</h2>
            <p><code>Nodal.PatternRecognition</code> is an optional analytics shell over canonical graph paths. It combines exact bitset similarity, sparse candidate search, communities, and temporal transitions while preserving provider-native acceleration.</p>
            <Link to="/docs/concepts/pattern-recognition-shell">Explore the analytics shell →</Link>
          </div>
        </section>
      </main>
    </Layout>
  );
}
