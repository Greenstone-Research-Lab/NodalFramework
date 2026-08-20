import {useBlogPost} from '@docusaurus/plugin-content-blog/client';
import useBaseUrl from '@docusaurus/useBaseUrl';
import {blogPostContainerID} from '@docusaurus/utils-common';
import MDXContent from '@theme/MDXContent';
import type {Props} from '@theme/BlogPostItem/Content';
import clsx from 'clsx';
import React, {type ReactNode} from 'react';
import styles from './styles.module.css';

export default function BlogPostItemContent({children, className}: Props): ReactNode {
  const {assets, frontMatter, isBlogPostPage} = useBlogPost();
  const journalFrontMatter = frontMatter as typeof frontMatter & {
    image_alt?: unknown;
    image_caption?: unknown;
  };
  const configuredImage = typeof frontMatter.image === 'string' ? frontMatter.image : '/img/journal/default-cover.svg';
  const imageUrl = assets.image ?? useBaseUrl(configuredImage);
  const imageAlt = typeof journalFrontMatter.image_alt === 'string'
    ? journalFrontMatter.image_alt
    : 'Nodal Framework Journal cover';
  const imageCaption = typeof journalFrontMatter.image_caption === 'string' ? journalFrontMatter.image_caption : undefined;

  return (
    <div
      id={isBlogPostPage ? blogPostContainerID : undefined}
      className={clsx('markdown', className)}>
      <figure className={clsx(styles.cover, isBlogPostPage && styles.coverPost)}>
        <img src={imageUrl} alt={imageAlt} loading={isBlogPostPage ? 'eager' : 'lazy'} />
        {imageCaption && <figcaption>{imageCaption}</figcaption>}
      </figure>
      <MDXContent>{children}</MDXContent>
    </div>
  );
}
