import React from 'react';

export function ArticlePage({ title }: { title: string }) {
  const slug = title.toSlug();
  return <div data-slug={slug}>{title}</div>;
}
