import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Nodal Framework',
  tagline: 'Provider-neutral graph data access for .NET',
  url: 'https://nodalframework.pages.dev',
  baseUrl: '/',
  organizationName: 'Greenstone-Research-Lab',
  projectName: 'NodalFramework',
  trailingSlash: false,
  onBrokenLinks: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },
  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          routeBasePath: 'docs',
          editUrl: 'https://github.com/Greenstone-Research-Lab/NodalFramework/edit/developer/website/',
          showLastUpdateAuthor: false,
          showLastUpdateTime: false,
        },
        blog: {
          showReadingTime: true,
          blogTitle: 'Nodal Framework Journal',
          blogDescription: 'Design notes, releases, and graph engineering articles.',
          editUrl: 'https://github.com/Greenstone-Research-Lab/NodalFramework/edit/developer/website/',
        },
        sitemap: {
          changefreq: 'weekly',
          priority: 0.6,
          ignorePatterns: ['/tags/**'],
        },
        theme: {customCss: './src/css/custom.css'},
      } satisfies Preset.Options,
    ],
  ],
  themeConfig: {
    image: 'img/social-card.svg',
    metadata: [
      {name: 'keywords', content: 'Nodal Framework, .NET, graph database, Neo4j, TigerGraph, LINQ'},
      {name: 'theme-color', content: '#0b1220'},
    ],
    navbar: {
      title: 'Nodal Framework',
      items: [
        {type: 'docSidebar', sidebarId: 'guideSidebar', position: 'left', label: 'Guide'},
        {to: '/docs/providers/overview', label: 'Providers', position: 'left'},
        {to: '/capabilities', label: 'Capabilities', position: 'left'},
        {href: 'pathname:///api/index.html', label: 'API', position: 'left'},
        {to: '/blog', label: 'Journal', position: 'left'},
        {type: 'localeDropdown', position: 'right'},
        {href: 'https://github.com/Greenstone-Research-Lab/NodalFramework', label: 'GitHub', position: 'right'},
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {title: 'Learn', items: [{label: 'Get started', to: '/docs/getting-started'}, {label: 'Capability graph', to: '/capabilities'}, {label: 'API reference', href: 'pathname:///api/index.html'}]},
        {title: 'Providers', items: [{label: 'Neo4j', to: '/docs/providers/neo4j'}, {label: 'TigerGraph', to: '/docs/providers/tigergraph'}]},
        {title: 'Project', items: [{label: 'GitHub', href: 'https://github.com/Greenstone-Research-Lab/NodalFramework'}, {label: 'Roadmap', to: '/docs/roadmap'}]},
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Greenstone Research Lab. MIT licensed.`,
    },
    prism: {
      additionalLanguages: ['csharp', 'powershell'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
