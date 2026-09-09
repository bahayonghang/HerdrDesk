const FORBIDDEN_SNIPPETS = [
  "innerHTML",
  "outerHTML",
  "document.write",
  "eval(",
  "new Function",
  "cdn.jsdelivr.net",
  "unpkg.com",
  "cdnjs.cloudflare.com",
  "jsdelivr.net",
];

export function sourceContainsForbidden(source: string): string | null {
  for (const snippet of FORBIDDEN_SNIPPETS) {
    if (source.includes(snippet)) {
      return snippet;
    }
  }
  return null;
}

export function isLocalAssetUrl(url: string): boolean {
  return url.startsWith("./") || url.startsWith("../") || url.startsWith("/");
}
