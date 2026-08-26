import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  guideSidebar: [
    'intro',
    'installation',
    'getting-started',
    {type: 'category', label: 'Core concepts', items: ['concepts/graph-model', 'concepts/context-and-sets', 'concepts/analytics-boundary']},
    {type: 'category', label: 'Querying', items: ['querying/query-engine', 'querying/traversals-and-paths', 'querying/graph-analytics', 'querying/compiled-and-native-queries']},
    {type: 'category', label: 'Data changes', items: ['data-changes/unit-of-work', 'data-changes/change-tracking']},
    {type: 'category', label: 'Providers', items: ['providers/overview', 'providers/compatibility', 'providers/neo4j', 'providers/tigergraph']},
    'migrations',
    'architecture',
    'roadmap',
  ],
};

export default sidebars;
