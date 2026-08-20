# Journal authoring

Journal posts live in `website/blog`. Store reusable cover assets in
`website/static/img/journal` and reference them from the post frontmatter:

```yaml
---
slug: graph-native-migrations
title: Designing graph-native migrations
authors: [ilker]
tags: [architecture, migrations]
description: The problem the article explains in one search-friendly sentence.
image: /img/journal/graph-native-migrations.webp
image_alt: A concise description of the cover image for screen readers.
image_caption: Optional visible attribution or context for the image.
---
```

The theme renders the image in both the Journal listing and the full article.
It also uses Docusaurus' standard `image` field for social previews. When a
post omits `image`, `/img/journal/default-cover.svg` is rendered automatically.

Prefer a 2:1 source image at 1600 by 800 pixels. Use WebP for photographs and
SVG for illustrations. Keep important content away from the outer 8% because
cards and social platforms can crop cover edges.

Place `<!-- truncate -->` after the opening argument so the Journal listing
shows a deliberate excerpt rather than the entire article.
