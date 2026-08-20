import Head from '@docusaurus/Head';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Layout from '@theme/Layout';
import React, {useEffect, useMemo, useState} from 'react';
import styles from './capabilities.module.css';

type RelationshipKey = 'requires' | 'provides' | 'uses' | 'supports';

type CapabilityNode = {
  '@id': string;
  '@type': string;
  name: string;
  description?: string;
  requires?: string | string[];
  provides?: string | string[];
  uses?: string | string[];
  supports?: string | string[];
  algorithms?: string[];
};

type CapabilityDocument = {
  '@graph': CapabilityNode[];
  'schema:version'?: string;
  'schema:dateModified'?: string;
};

type PositionedNode = CapabilityNode & {x: number; y: number; layer: number};
type GraphEdge = {source: PositionedNode; target: PositionedNode; relationship: RelationshipKey};

const relationshipKeys: RelationshipKey[] = ['provides', 'requires', 'supports', 'uses'];
const layerX = [135, 405, 675, 945];
const nodeWidth = 218;
const nodeHeight = 70;

function toValues(value?: string | string[]): string[] {
  if (!value) {
    return [];
  }

  return Array.isArray(value) ? value : [value];
}

function getLayer(node: CapabilityNode): number {
  if (node['@type'] === 'nodal:CoreType' || node['@type'] === 'nodal:GenericType') {
    return 0;
  }

  if (node['@type'] === 'nodal:ModelConstraint' || node['@type'] === 'nodal:Attribute') {
    return 1;
  }

  if (node['@type'] === 'nodal:Provider' || node['@type'] === 'nodal:Package') {
    return 3;
  }

  return 2;
}

function getKind(node: CapabilityNode): string {
  return node['@type'].replace('nodal:', '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

function getShortName(id: string): string {
  return id.replace('nodal:', '');
}

function getDisplayName(name: string): string {
  if (name.startsWith('RelationSet<')) {
    return 'RelationSet<TSource, …>';
  }

  return name.length > 27 ? `${name.slice(0, 26)}…` : name;
}

function buildGraph(nodes: CapabilityNode[]): {nodes: PositionedNode[]; edges: GraphEdge[]; height: number} {
  const grouped = [0, 1, 2, 3].map((layer) =>
    nodes
      .filter((node) => getLayer(node) === layer)
      .sort((left, right) => left.name.localeCompare(right.name)),
  );
  const largestLayer = Math.max(...grouped.map((group) => group.length), 1);
  const height = Math.max(590, largestLayer * 118 + 120);
  const positioned = grouped.flatMap((group, layer) => {
    const spacing = (height - 140) / Math.max(group.length, 1);
    return group.map((node, index) => ({
      ...node,
      layer,
      x: layerX[layer],
      y: 92 + spacing * index + spacing / 2,
    }));
  });
  const byId = new Map(positioned.map((node) => [node['@id'], node]));
  const edges = positioned.flatMap((source) =>
    relationshipKeys.flatMap((relationship) =>
      toValues(source[relationship]).flatMap((targetId) => {
        const target = byId.get(targetId);
        return target ? [{source, target, relationship}] : [];
      }),
    ),
  );

  return {nodes: positioned, edges, height};
}

function edgePath(edge: GraphEdge): string {
  const forward = edge.target.x >= edge.source.x;
  const sourceX = edge.source.x + (forward ? nodeWidth / 2 : -nodeWidth / 2);
  const targetX = edge.target.x + (forward ? -nodeWidth / 2 : nodeWidth / 2);
  const curve = Math.max(60, Math.abs(targetX - sourceX) * 0.45);
  const sourceControl = sourceX + (forward ? curve : -curve);
  const targetControl = targetX + (forward ? -curve : curve);
  return `M ${sourceX} ${edge.source.y} C ${sourceControl} ${edge.source.y}, ${targetControl} ${edge.target.y}, ${targetX} ${edge.target.y}`;
}

export default function CapabilitiesPage() {
  const graphUrl = useBaseUrl('/knowledge/nodal-capabilities.jsonld');
  const [document, setDocument] = useState<CapabilityDocument | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState('nodal:NodalContext');

  useEffect(() => {
    const controller = new AbortController();
    fetch(graphUrl, {headers: {Accept: 'application/ld+json'}, signal: controller.signal})
      .then((response) => {
        if (!response.ok) {
          throw new Error(`Capability metadata returned HTTP ${response.status}.`);
        }
        return response.json() as Promise<CapabilityDocument>;
      })
      .then(setDocument)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : 'Capability metadata could not be loaded.');
        }
      });

    return () => controller.abort();
  }, [graphUrl]);

  const graph = useMemo(() => buildGraph(document?.['@graph'] ?? []), [document]);
  const selected = graph.nodes.find((node) => node['@id'] === selectedId) ?? graph.nodes[0];

  return (
    <Layout title="Capability graph" description="Explore the Nodal Framework types, providers, and graph capabilities as a connected model.">
      <Head>
        <meta property="og:title" content="Nodal Framework capability graph" />
      </Head>
      <main className={styles.page}>
        <header className={styles.hero}>
          <div>
            <span className={styles.eyebrow}>Living architecture</span>
            <h1>See how Nodal fits together.</h1>
            <p>
              This view is generated from the same JSON-LD capability graph used by coding agents.
              Select a node to inspect its contracts and relationships.
            </p>
          </div>
          <div className={styles.heroLinks}>
            <Link className="button button--primary" to="/docs/providers/compatibility">Provider matrix</Link>
            <a className="button button--secondary" href={graphUrl}>Raw JSON-LD</a>
          </div>
        </header>

        <section className={styles.workspace} aria-label="Nodal Framework capability explorer">
          <div className={styles.graphPanel}>
            <div className={styles.legend} aria-label="Graph legend">
              <span><i className={styles.coreDot} />Core API</span>
              <span><i className={styles.modelDot} />Model</span>
              <span><i className={styles.capabilityDot} />Capability</span>
              <span><i className={styles.providerDot} />Provider</span>
            </div>

            {!document && !error && <div className={styles.state}>Loading the capability graph…</div>}
            {error && <div className={styles.error}><strong>Graph unavailable.</strong><span>{error}</span></div>}
            {document && (
              <svg
                className={styles.graph}
                viewBox={`0 0 1080 ${graph.height}`}
                role="img"
                aria-labelledby="capability-graph-title capability-graph-description">
                <title id="capability-graph-title">Nodal Framework capability graph</title>
                <desc id="capability-graph-description">Connected core types, model constraints, capabilities, packages, and providers.</desc>
                <defs>
                  <marker id="capability-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
                    <path d="M 0 0 L 10 5 L 0 10 z" />
                  </marker>
                </defs>
                <g className={styles.edges}>
                  {graph.edges.map((edge) => (
                    <path
                      key={`${edge.source['@id']}-${edge.relationship}-${edge.target['@id']}`}
                      d={edgePath(edge)}
                      data-relationship={edge.relationship}
                    />
                  ))}
                </g>
                <g>
                  {graph.nodes.map((node) => (
                    <g
                      key={node['@id']}
                      className={`${styles.node} ${styles[`layer${node.layer}`]} ${selected?.['@id'] === node['@id'] ? styles.selected : ''}`}
                      transform={`translate(${node.x - nodeWidth / 2} ${node.y - nodeHeight / 2})`}
                      role="button"
                      tabIndex={0}
                      aria-label={`${node.name}, ${getKind(node)}`}
                      onClick={() => setSelectedId(node['@id'])}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          setSelectedId(node['@id']);
                        }
                      }}>
                      <rect width={nodeWidth} height={nodeHeight} rx="14" />
                      <text className={styles.nodeKind} x="16" y="23">{getKind(node)}</text>
                      <text className={styles.nodeName} x="16" y="48">{getDisplayName(node.name)}</text>
                    </g>
                  ))}
                </g>
              </svg>
            )}
          </div>

          <aside className={styles.inspector} aria-live="polite">
            {selected ? (
              <>
                <span className={styles.inspectorKind}>{getKind(selected)}</span>
                <h2>{selected.name}</h2>
                <code>{getShortName(selected['@id'])}</code>
                <p>{selected.description ?? 'A typed part of the Nodal Framework capability model.'}</p>
                {relationshipKeys.map((relationship) => {
                  const values = toValues(selected[relationship]);
                  return values.length > 0 ? (
                    <section key={relationship} className={styles.relationships}>
                      <h3>{relationship}</h3>
                      <div>{values.map((value) => <span key={value}>{getShortName(value)}</span>)}</div>
                    </section>
                  ) : null;
                })}
                {selected.algorithms && (
                  <section className={styles.algorithms}>
                    <h3>{selected.algorithms.length} algorithms</h3>
                    <p>{selected.algorithms.join(' · ')}</p>
                  </section>
                )}
              </>
            ) : <p>Select a graph node to inspect it.</p>}
          </aside>
        </section>

        <footer className={styles.sourceNote}>
          <span>Metadata version {document?.['schema:version'] ?? 'loading'}</span>
          <span>Updated {document?.['schema:dateModified'] ?? '—'}</span>
          <span>{graph.nodes.length} modeled concepts · {graph.edges.length} visible relationships</span>
        </footer>
      </main>
    </Layout>
  );
}
