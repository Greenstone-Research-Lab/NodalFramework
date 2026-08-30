import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  guideSidebar: [
    'intro',
    'installation',
    'getting-started',
    {type: 'category', label: 'Core concepts', items: ['concepts/graph-model', 'concepts/context-and-sets', 'concepts/analytics-boundary']},
    {type: 'category', label: 'Querying', items: ['querying/query-engine', 'querying/traversals-and-paths', 'querying/graph-analytics', 'querying/compiled-and-native-queries']},
    {type: 'category', label: 'Data changes', items: ['data-changes/unit-of-work', 'data-changes/change-tracking']},
    'imports',
    'modeling',
    {type: 'category', label: 'Providers', items: ['providers/overview', 'providers/query-parity', 'providers/compatibility', 'providers/neo4j', 'providers/tigergraph']},
    'migrations',
    'architecture',
    {type: 'category', label: 'Contributing', items: ['contributing/spec-driven-development']},
    {type: 'category', label: 'Releases and security', items: ['releases/0.1-beta', 'releases/versioning-policy', 'releases/release-evidence', 'security/dependency-risk']},
    'roadmap',
  ],
};

export default sidebars;
