import fs from "node:fs/promises";
import path from "node:path";

function toTimestamp(value) {
  const date = value instanceof Date ? value : new Date(value);
  const pad = (part) => String(part).padStart(2, "0");
  return [
    date.getFullYear(),
    pad(date.getMonth() + 1),
    pad(date.getDate()),
    "-",
    pad(date.getHours()),
    pad(date.getMinutes()),
  ].join("");
}

const consoleScopes = [
  {
    key: "home",
    label: "CABINET",
    kind: "home",
    status: "live",
    views: [
      { key: "overview", label: "Overview" },
    ],
  },
  {
    key: "system",
    label: "SYSTEM",
    kind: "system",
    status: "live",
    views: [
      { key: "runtime", label: "Runtime" },
      { key: "connectivity", label: "Connectivity" },
      { key: "storage", label: "Storage" },
      { key: "coverage", label: "Coverage" },
      { key: "events", label: "Events" },
    ],
  },
  {
    key: "info",
    label: "INFO",
    kind: "info",
    status: "reference",
    views: [
      { key: "overview", label: "Overview" },
      { key: "glossary", label: "Glossary" },
      { key: "contracts", label: "Contracts" },
      { key: "data_sources", label: "Data Sources" },
      { key: "algorithms", label: "Algorithms" },
    ],
  },
  {
    key: "analytics",
    label: "ANALYTICS",
    kind: "analytics",
    status: "live",
    views: [
      { key: "session", label: "Session" },
      { key: "colony", label: "Colony" },
      { key: "advice", label: "Advice" },
      { key: "sse", label: "SSE" },
      { key: "candidates", label: "Candidates" },
    ],
  },
  {
    key: "dev_blog",
    label: "DEV BLOG",
    kind: "dev_blog",
    status: "live",
    views: [
      { key: "features", label: "Features" },
      { key: "churn", label: "Churn" },
      { key: "commits", label: "Commits" },
      { key: "topics", label: "Topics" },
      { key: "suggestions", label: "Suggestions" },
    ],
  },
];

const fullMinisterViews = [
  { key: "prompt", label: "LLM" },
  { key: "briefing", label: "Briefing" },
  { key: "rules", label: "Rules" },
  { key: "infographics", label: "Infographics" },
  { key: "advice", label: "Advice" },
];

const rulesOnlyMinisterViews = [
  { key: "briefing", label: "Briefing" },
  { key: "build_queue", label: "Build Queue" },
  { key: "solver", label: "Solver" },
  { key: "requests", label: "Requests" },
  { key: "rules", label: "Rules" },
  { key: "advice", label: "Advice" },
];

const ministerScopes = [
  { key: "mayor", label: "Mayor", kind: "minister", status: "live", views: fullMinisterViews },
  { key: "food", label: "Chef", kind: "minister", status: "live", views: fullMinisterViews },
  { key: "willie", label: "Willie", kind: "minister", status: "live", views: rulesOnlyMinisterViews },
  { key: "defense", label: "Defense", kind: "minister", status: "planned", views: fullMinisterViews },
  { key: "welfare", label: "Welfare", kind: "minister", status: "live", views: fullMinisterViews },
  { key: "medical", label: "Medical", kind: "minister", status: "planned", views: fullMinisterViews },
  { key: "research", label: "Research", kind: "minister", status: "planned", views: fullMinisterViews },
  { key: "industry", label: "Industry", kind: "minister", status: "planned", views: fullMinisterViews },
  { key: "economy", label: "Economy", kind: "minister", status: "planned", views: fullMinisterViews },
  { key: "chief_of_staff", label: "Chief of Staff", kind: "minister", status: "planned", views: fullMinisterViews },
];

const embeddedImageCache = new Map();

export function dashboardSnapshotPages() {
  return [...consoleScopes, ...ministerScopes].flatMap((scope) =>
    scope.views.map((view) => ({
      scope: scope.key,
      scopeLabel: scope.label,
      kind: scope.kind,
      status: scope.status,
      view: view.key,
      viewLabel: view.label,
      relativePath: path.posix.join("pages", scope.key, `${view.key}.html`),
    })),
  );
}

export async function dashboardSnapshotPagesFromDashboardSource(repoRoot) {
  if (!repoRoot) {
    throw new Error("dashboardSnapshotPagesFromDashboardSource requires repoRoot.");
  }

  const scopesPath = path.join(repoRoot, "Dashboard", "src", "dashboard", "scopes.ts");
  const source = await fs.readFile(scopesPath, "utf8");
  const viewArrays = {
    homeViews: parseDashboardViewArray(source, "homeViews"),
    systemViews: parseDashboardViewArray(source, "systemViews"),
    infoViews: parseDashboardViewArray(source, "infoViews"),
    analyticsViews: parseDashboardViewArray(source, "analyticsViews"),
    devBlogViews: parseDashboardViewArray(source, "devBlogViews"),
    ministerViews: parseDashboardViewArray(source, "ministerViews"),
  };
  const ministerViewByKey = new Map(viewArrays.ministerViews.map((view) => [view.key, view]));
  const allMinisterViews = viewArrays.ministerViews
    .filter((view) => !parseAllMinisterViewExclusions(source).includes(view.key));
  const rulesOnlyMinisterViews = parseStringArrayConstant(source, "rulesOnlyMinisterViews")
    .map((key) => viewForKey(ministerViewByKey, key, "rulesOnlyMinisterViews"));
  const namedViewSets = {
    homeViews: viewArrays.homeViews,
    systemViews: viewArrays.systemViews,
    infoViews: viewArrays.infoViews,
    analyticsViews: viewArrays.analyticsViews,
    devBlogViews: viewArrays.devBlogViews,
    allMinisterViews,
    rulesOnlyMinisterViews,
  };
  const scopes = parseScopeConfigs(source, namedViewSets);

  return scopes.flatMap((scope) =>
    scope.views.map((view) => ({
      scope: scope.key,
      scopeLabel: scope.label,
      kind: scope.kind,
      status: scope.status,
      view: view.key,
      viewLabel: view.label,
      relativePath: path.posix.join("pages", scope.key, `${view.key}.html`),
    })),
  );
}

export async function assertSnapshotPageRegistryMatchesDashboard(repoRoot) {
  const sourcePages = await dashboardSnapshotPagesFromDashboardSource(repoRoot);
  const fallbackPages = dashboardSnapshotPages();
  const sourcePaths = sourcePages.map((page) => page.relativePath);
  const fallbackPaths = fallbackPages.map((page) => page.relativePath);
  const missing = sourcePaths.filter((relativePath) => !fallbackPaths.includes(relativePath));
  const extra = fallbackPaths.filter((relativePath) => !sourcePaths.includes(relativePath));
  if (missing.length > 0 || extra.length > 0 || sourcePaths.length !== fallbackPaths.length) {
    throw new Error(`Snapshot page registry drifted from Dashboard/src/dashboard/scopes.ts. Missing=${missing.join(", ")} Extra=${extra.join(", ")}`);
  }

  return {
    pageCount: sourcePages.length,
    pages: sourcePages,
  };
}

async function resolveDashboardSnapshotPages(sourceRepoRoot) {
  if (!sourceRepoRoot) {
    return dashboardSnapshotPages();
  }

  const scopesPath = path.join(sourceRepoRoot, "Dashboard", "src", "dashboard", "scopes.ts");
  if (!(await pathExists(scopesPath))) {
    return dashboardSnapshotPages();
  }

  return dashboardSnapshotPagesFromDashboardSource(sourceRepoRoot);
}

function parseDashboardViewArray(source, name) {
  const match = source.match(new RegExp(`export const ${name}:[^=]*= \\[([\\s\\S]*?)\\];`));
  if (!match) {
    throw new Error(`Could not find dashboard view array '${name}' in Dashboard/src/dashboard/scopes.ts.`);
  }

  const views = Array.from(match[1].matchAll(/\{\s*key:\s*'([^']+)'\s*,\s*label:\s*'([^']+)'\s*\}/g))
    .map((viewMatch) => ({ key: viewMatch[1], label: viewMatch[2] }));
  if (views.length === 0) {
    throw new Error(`Dashboard view array '${name}' did not contain any parseable views.`);
  }

  return views;
}

function parseAllMinisterViewExclusions(source) {
  const match = source.match(/const allMinisterViews[\s\S]*?;/);
  if (!match) {
    throw new Error("Could not find allMinisterViews in Dashboard/src/dashboard/scopes.ts.");
  }

  return Array.from(match[0].matchAll(/view !== '([^']+)'/g)).map((excludeMatch) => excludeMatch[1]);
}

function parseStringArrayConstant(source, name) {
  const match = source.match(new RegExp(`const ${name}:[^=]*= \\[([\\s\\S]*?)\\];`));
  if (!match) {
    throw new Error(`Could not find string array '${name}' in Dashboard/src/dashboard/scopes.ts.`);
  }

  return Array.from(match[1].matchAll(/'([^']+)'/g)).map((valueMatch) => valueMatch[1]);
}

function viewForKey(viewByKey, key, sourceName) {
  const view = viewByKey.get(key);
  if (!view) {
    throw new Error(`Could not resolve view '${key}' from ${sourceName}.`);
  }

  return view;
}

function parseScopeConfigs(source, namedViewSets) {
  const match = source.match(/export const scopeConfigs:[^=]*= \[([\s\S]*?)\];/);
  if (!match) {
    throw new Error("Could not find scopeConfigs in Dashboard/src/dashboard/scopes.ts.");
  }

  const scopes = [];
  for (const line of match[1].split("\n")) {
    const scopeMatch = line.match(/\{\s*key:\s*'([^']+)'\s*,\s*label:\s*'([^']+)'\s*,[\s\S]*?kind:\s*'([^']+)'\s*,\s*status:\s*'([^']+)'\s*,\s*enabledViews:\s*([^,}]+)/);
    if (!scopeMatch) {
      continue;
    }

    scopes.push({
      key: scopeMatch[1],
      label: scopeMatch[2],
      kind: scopeMatch[3],
      status: scopeMatch[4],
      views: resolveEnabledViews(scopeMatch[5].trim(), namedViewSets),
    });
  }

  if (scopes.length === 0) {
    throw new Error("scopeConfigs did not contain any parseable scope entries.");
  }

  return scopes;
}

function resolveEnabledViews(expression, namedViewSets) {
  const mappedArrayMatch = expression.match(/^([A-Za-z0-9_]+)\.map\(view => view\.key\)$/);
  const viewSetName = mappedArrayMatch ? mappedArrayMatch[1] : expression;
  const views = namedViewSets[viewSetName];
  if (!views) {
    throw new Error(`Unsupported dashboard enabledViews expression '${expression}'.`);
  }

  return views;
}

export async function writeStaticDashboardSnapshot({
  tab,
  repoRoot,
  dashboardUrl,
  health,
  timestamp = new Date(),
  settleMilliseconds = 2500,
  reload = true,
}) {
  if (!tab) {
    throw new Error("writeStaticDashboardSnapshot requires a Browser tab.");
  }

  if (!repoRoot) {
    throw new Error("writeStaticDashboardSnapshot requires repoRoot.");
  }

  if (!dashboardUrl) {
    throw new Error("writeStaticDashboardSnapshot requires dashboardUrl.");
  }

  const currentUrl = await tab.url();
  if (currentUrl !== dashboardUrl) {
    await tab.goto(dashboardUrl);
  } else if (reload) {
    await tab.reload();
  }

  await tab.playwright.waitForLoadState({ state: "load", timeoutMs: 15000 });
  await tab.playwright.waitForTimeout(settleMilliseconds);
  await waitForDashboardMarkers(tab, dashboardUrl);

  const capturedAt = timestamp instanceof Date
    ? timestamp.toISOString()
    : new Date(timestamp).toISOString();

  const page = await tab.playwright.evaluate(async (input) => {
    function attributeEscape(value) {
      return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("\"", "&quot;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
    }

    function stylesheetText() {
      const chunks = [];
      for (const sheet of Array.from(document.styleSheets)) {
        try {
          const rules = Array.from(sheet.cssRules ?? []);
          if (rules.length > 0) {
            chunks.push(rules.map((rule) => rule.cssText).join("\n"));
          }
        } catch {
          if (sheet.href) {
            chunks.push(`@import url("${new URL(sheet.href, location.href).href}");`);
          }
        }
      }

      return chunks.join("\n\n");
    }

    function collectImageSources() {
      const images = [];
      for (const image of Array.from(document.images ?? [])) {
        const src = image.getAttribute("src");
        if (!src || src.startsWith("data:")) {
          continue;
        }

        const absoluteUrl = new URL(src, location.href).href;
        images.push({
          src,
          escapedSrc: attributeEscape(src),
          absoluteUrl,
        });
      }

      return images;
    }

    const text = document.body?.innerText ?? "";

    return {
      html: document.documentElement.outerHTML,
      cssText: stylesheetText(),
      title: document.title,
      url: location.href,
      hasDashboardTitle: text.includes("RimBob Dashboard v2"),
      hasSystemScope: text.includes("SYSTEM"),
      images: collectImageSources(),
    };
  }, { capturedAt, dashboardUrl });

  if (!page.hasDashboardTitle || !page.hasSystemScope) {
    throw new Error("Rendered page did not contain expected RimBob dashboard markers.");
  }

  const snapshotDir = path.join(repoRoot, "web", "Snapshot");
  await fs.mkdir(snapshotDir, { recursive: true });

  const stamp = toTimestamp(timestamp);
  const indexPath = path.join(snapshotDir, "index.html");
  const timestampedPath = path.join(snapshotDir, `dashboard-${stamp}.html`);
  const metadataPath = path.join(snapshotDir, "metadata.json");

  const imageResult = await embedImages(page.images);
  const html = toStaticHtml(page.html, page.cssText, imageResult.images, capturedAt, dashboardUrl);
  await writeSnapshotText(indexPath, html);
  await writeSnapshotText(timestampedPath, html);
  await writeSnapshotText(
    metadataPath,
    `${JSON.stringify({
      captured_at: capturedAt,
      dashboard_url: dashboardUrl,
      title: page.title,
      page_url: page.url,
      index_path: indexPath,
      timestamped_path: timestampedPath,
      health,
      image_counts: imageResult.counts,
    }, null, 2)}\n`,
  );

  return {
    indexPath,
    timestampedPath,
    metadataPath,
    title: page.title,
    url: page.url,
    imageCounts: imageResult.counts,
  };
}

export async function writeStaticDashboardSiteSnapshot({
  tab,
  repoRoot,
  sourceRepoRoot = repoRoot,
  dashboardUrl,
  health,
  timestamp = new Date(),
  settleMilliseconds = 900,
}) {
  if (!tab) {
    throw new Error("writeStaticDashboardSiteSnapshot requires a Browser tab.");
  }

  if (!repoRoot) {
    throw new Error("writeStaticDashboardSiteSnapshot requires repoRoot.");
  }

  if (!dashboardUrl) {
    throw new Error("writeStaticDashboardSiteSnapshot requires dashboardUrl.");
  }

  const capturedAt = timestamp instanceof Date
    ? timestamp.toISOString()
    : new Date(timestamp).toISOString();
  const snapshotDir = path.join(repoRoot, "web", "Snapshot");
  const pagesDir = path.join(snapshotDir, "pages");
  await fs.mkdir(snapshotDir, { recursive: true });
  await removeOldSinglePageArtifacts(snapshotDir);
  await fs.rm(pagesDir, { recursive: true, force: true });
  await fs.mkdir(pagesDir, { recursive: true });

  const pages = await resolveDashboardSnapshotPages(sourceRepoRoot);
  const capturedPages = [];
  let entryHtml = null;
  let entryPage = null;
  let embedded = 0;
  let failed = 0;

  for (const page of pages) {
    const pageUrl = urlForPage(dashboardUrl, page);
    const rendered = await captureRenderedDashboardPage(tab, pageUrl, capturedAt, settleMilliseconds);
    const imageResult = await embedImages(rendered.images);
    const html = toStaticHtml(rendered.html, rendered.cssText, imageResult.images, capturedAt, pageUrl, {
      currentPage: page,
      pages,
    });
    const outputPath = path.join(snapshotDir, ...page.relativePath.split("/"));
    await fs.mkdir(path.dirname(outputPath), { recursive: true });
    await writeSnapshotText(outputPath, html);

    if (page.scope === "mayor" && page.view === "advice") {
      entryPage = page;
      entryHtml = toStaticHtml(rendered.html, rendered.cssText, imageResult.images, capturedAt, pageUrl, {
        currentPage: { ...page, relativePath: "index.html" },
        pages,
      });
    }

    embedded += imageResult.counts.embedded;
    failed += imageResult.counts.failed;
    capturedPages.push({
      ...page,
      url: pageUrl,
      title: rendered.title,
      path: outputPath,
      image_counts: imageResult.counts,
    });
  }

  const indexPath = path.join(snapshotDir, "index.html");
  const metadataPath = path.join(snapshotDir, "metadata.json");
  const stamp = toTimestamp(timestamp);
  if (!entryHtml || !entryPage) {
    throw new Error("Could not find Mayor Advice page for snapshot entrypoint.");
  }

  await writeSnapshotText(indexPath, entryHtml);
  await writeSnapshotText(
    metadataPath,
    `${JSON.stringify({
      captured_at: capturedAt,
      snapshot_id: stamp,
      dashboard_url: dashboardUrl,
      index_path: indexPath,
      entry_page: {
        scope: entryPage.scope,
        view: entryPage.view,
        label: `${entryPage.scopeLabel} / ${entryPage.viewLabel}`,
        source_relative_path: entryPage.relativePath,
      },
      health,
      page_count: capturedPages.length,
      static_navigation: {
        scope_rail_links: true,
        view_tab_links: true,
      },
      image_counts: { embedded, failed },
      pages: capturedPages.map((page) => ({
        scope: page.scope,
        view: page.view,
        label: `${page.scopeLabel} / ${page.viewLabel}`,
        status: page.status,
        url: page.url,
        relative_path: page.relativePath,
        image_counts: page.image_counts,
      })),
    }, null, 2)}\n`,
  );

  return {
    indexPath,
    metadataPath,
    pageCount: capturedPages.length,
    imageCounts: { embedded, failed },
    pages: capturedPages,
  };
}

export async function writeStaticDashboardSiteSnapshotBatch({
  tab,
  repoRoot,
  sourceRepoRoot = repoRoot,
  dashboardUrl,
  health,
  timestamp = new Date(),
  settleMilliseconds = 900,
  startIndex = 0,
  pageLimit = 20,
  reset = false,
  finalize = false,
}) {
  if (!tab) {
    throw new Error("writeStaticDashboardSiteSnapshotBatch requires a Browser tab.");
  }

  if (!repoRoot) {
    throw new Error("writeStaticDashboardSiteSnapshotBatch requires repoRoot.");
  }

  if (!dashboardUrl) {
    throw new Error("writeStaticDashboardSiteSnapshotBatch requires dashboardUrl.");
  }

  const requestedCapturedAt = timestamp instanceof Date
    ? timestamp.toISOString()
    : new Date(timestamp).toISOString();
  const snapshotDir = path.join(repoRoot, "web", "Snapshot");
  const pagesDir = path.join(snapshotDir, "pages");
  const partialPath = path.join(snapshotDir, "metadata.partial.json");
  await fs.mkdir(snapshotDir, { recursive: true });

  if (reset) {
    await removeOldSinglePageArtifacts(snapshotDir);
    await fs.rm(pagesDir, { recursive: true, force: true });
    await fs.rm(partialPath, { force: true });
  }

  await fs.mkdir(pagesDir, { recursive: true });

  const pages = await resolveDashboardSnapshotPages(sourceRepoRoot);
  const partial = reset
    ? { captured_at: requestedCapturedAt, entry_page: null, captured_pages: [] }
    : await readSnapshotPartial(partialPath, requestedCapturedAt);
  const capturedAt = partial.captured_at;
  const selectedPages = pages.slice(startIndex, Math.min(startIndex + pageLimit, pages.length));

  for (const page of selectedPages) {
    const pageUrl = urlForPage(dashboardUrl, page);
    const rendered = await captureRenderedDashboardPage(tab, pageUrl, capturedAt, settleMilliseconds);
    const imageResult = await embedImages(rendered.images);
    const html = toStaticHtml(rendered.html, rendered.cssText, imageResult.images, capturedAt, pageUrl, {
      currentPage: page,
      pages,
    });
    const outputPath = path.join(snapshotDir, ...page.relativePath.split("/"));
    await fs.mkdir(path.dirname(outputPath), { recursive: true });
    await writeSnapshotText(outputPath, html);

    if (page.scope === "mayor" && page.view === "advice") {
      partial.entry_page = {
        scope: page.scope,
        view: page.view,
        label: `${page.scopeLabel} / ${page.viewLabel}`,
        source_relative_path: page.relativePath,
      };
      const entryHtml = toStaticHtml(rendered.html, rendered.cssText, imageResult.images, capturedAt, pageUrl, {
        currentPage: { ...page, relativePath: "index.html" },
        pages,
      });
      await writeSnapshotText(path.join(snapshotDir, "index.html"), entryHtml);
    }

    partial.captured_pages = partial.captured_pages.filter((capturedPage) =>
      capturedPage.relative_path !== page.relativePath);
    partial.captured_pages.push({
      scope: page.scope,
      view: page.view,
      label: `${page.scopeLabel} / ${page.viewLabel}`,
      status: page.status,
      url: pageUrl,
      title: rendered.title,
      relative_path: page.relativePath,
      path: outputPath,
      image_counts: imageResult.counts,
    });
  }

  const pageOrder = new Map(pages.map((page, index) => [page.relativePath, index]));
  partial.captured_pages.sort((left, right) =>
    (pageOrder.get(left.relative_path) ?? Number.MAX_SAFE_INTEGER) -
    (pageOrder.get(right.relative_path) ?? Number.MAX_SAFE_INTEGER));

  const capturedRelativePaths = new Set(partial.captured_pages.map((page) => page.relative_path));
  const missingPages = pages.filter((page) => !capturedRelativePaths.has(page.relativePath));
  const imageCounts = partial.captured_pages.reduce((counts, page) => ({
    embedded: counts.embedded + (page.image_counts?.embedded ?? 0),
    failed: counts.failed + (page.image_counts?.failed ?? 0),
  }), { embedded: 0, failed: 0 });
  const shouldFinalize = finalize || missingPages.length === 0;
  const metadataPath = path.join(snapshotDir, "metadata.json");

  if (shouldFinalize) {
    if (missingPages.length > 0) {
      throw new Error(`Cannot finalize snapshot; missing pages: ${missingPages.map((page) => page.relativePath).join(", ")}`);
    }

    if (!partial.entry_page) {
      throw new Error("Cannot finalize snapshot; Mayor Advice entry page was not captured.");
    }

    const capturedByPath = new Map(partial.captured_pages.map((page) => [page.relative_path, page]));
    const capturedPages = pages.map((page) => {
      const capturedPage = capturedByPath.get(page.relativePath);
      return {
        scope: capturedPage.scope,
        view: capturedPage.view,
        label: capturedPage.label,
        status: capturedPage.status,
        url: capturedPage.url,
        relative_path: capturedPage.relative_path,
        image_counts: capturedPage.image_counts,
      };
    });
    await writeSnapshotText(
      metadataPath,
      `${JSON.stringify({
        captured_at: capturedAt,
        snapshot_id: toTimestamp(capturedAt),
        dashboard_url: dashboardUrl,
        index_path: path.join(snapshotDir, "index.html"),
        entry_page: partial.entry_page,
        health,
        page_count: pages.length,
        static_navigation: {
          scope_rail_links: true,
          view_tab_links: true,
        },
        image_counts: imageCounts,
        pages: capturedPages,
      }, null, 2)}\n`,
    );
    await fs.rm(partialPath, { force: true });
  } else {
    await writeSnapshotText(partialPath, `${JSON.stringify(partial, null, 2)}\n`);
  }

  return {
    capturedAt,
    capturedThisBatch: selectedPages.length,
    capturedTotal: partial.captured_pages.length,
    pageCount: pages.length,
    nextIndex: Math.min(startIndex + pageLimit, pages.length),
    finalized: shouldFinalize,
    missingCount: shouldFinalize ? 0 : missingPages.length,
    indexPath: path.join(snapshotDir, "index.html"),
    metadataPath,
    partialPath,
    imageCounts,
  };
}

export async function installStaticDashboardSnapshot({
  sourceRoot,
  sourceSnapshotDir,
  repoRoot,
  sourceRepoRoot = repoRoot,
}) {
  if (!repoRoot) {
    throw new Error("installStaticDashboardSnapshot requires repoRoot.");
  }

  const resolvedSourceSnapshotDir = sourceSnapshotDir
    ?? (sourceRoot ? path.join(sourceRoot, "web", "Snapshot") : null);
  if (!resolvedSourceSnapshotDir) {
    throw new Error("installStaticDashboardSnapshot requires sourceRoot or sourceSnapshotDir.");
  }

  const snapshotDir = path.join(repoRoot, "web", "Snapshot");
  await fs.rm(snapshotDir, { recursive: true, force: true });
  await fs.mkdir(path.dirname(snapshotDir), { recursive: true });
  await fs.cp(resolvedSourceSnapshotDir, snapshotDir, { recursive: true });

  const metadataPath = path.join(snapshotDir, "metadata.json");
  const metadata = JSON.parse(await fs.readFile(metadataPath, "utf8"));
  metadata.index_path = path.join(snapshotDir, "index.html");
  await writeSnapshotText(metadataPath, `${JSON.stringify(metadata, null, 2)}\n`);
  const normalized = await normalizeStaticDashboardSnapshot({ repoRoot });
  const verification = await verifyStaticDashboardSnapshot({
    repoRoot,
    sourceRepoRoot,
    expectedRuntimeRoot: sourceRepoRoot,
  });

  return {
    snapshotDir,
    normalized,
    verification,
  };
}

export async function normalizeStaticDashboardSnapshot({ repoRoot }) {
  if (!repoRoot) {
    throw new Error("normalizeStaticDashboardSnapshot requires repoRoot.");
  }

  const snapshotDir = path.join(repoRoot, "web", "Snapshot");
  const files = await listFilesRecursive(snapshotDir, (filePath) =>
    /\.(?:html|json)$/i.test(filePath));
  let changed = 0;
  for (const filePath of files) {
    const text = await fs.readFile(filePath, "utf8");
    const normalized = normalizeSnapshotText(text);
    if (normalized !== text) {
      await fs.writeFile(filePath, normalized, "utf8");
      changed += 1;
    }
  }

  return {
    fileCount: files.length,
    changed,
  };
}

export async function verifyStaticDashboardSnapshot({
  repoRoot,
  sourceRepoRoot = repoRoot,
  expectedRuntimeRoot = sourceRepoRoot,
  expectedHostProcessPath = null,
  throwOnFailure = true,
}) {
  if (!repoRoot) {
    throw new Error("verifyStaticDashboardSnapshot requires repoRoot.");
  }

  const snapshotDir = path.join(repoRoot, "web", "Snapshot");
  const pagesDir = path.join(snapshotDir, "pages");
  const metadataPath = path.join(snapshotDir, "metadata.json");
  const indexPath = path.join(snapshotDir, "index.html");
  const errors = [];
  const metadata = await readJsonIfExists(metadataPath);
  const indexHtml = await readTextIfExists(indexPath);
  const expectedPages = await resolveDashboardSnapshotPages(sourceRepoRoot);
  const expectedPagePaths = new Set(expectedPages.map((page) => normalizeRelativePath(page.relativePath)));
  const expectedScopeViewCounts = expectedPages.reduce((counts, page) => {
    counts.set(page.scope, (counts.get(page.scope) ?? 0) + 1);
    return counts;
  }, new Map());
  const pageFiles = await listFilesRecursive(pagesDir, (filePath) => /\.html$/i.test(filePath));
  const htmlFiles = await listFilesRecursive(snapshotDir, (filePath) => /\.html$/i.test(filePath));
  const actualPagePaths = new Set(pageFiles.map((filePath) =>
    normalizeRelativePath(path.relative(snapshotDir, filePath))));

  if (!metadata) {
    errors.push("metadata.json is missing or invalid.");
  }

  if (!indexHtml) {
    errors.push("index.html is missing.");
  }

  if (metadata?.entry_page?.scope !== "mayor" || metadata?.entry_page?.view !== "advice") {
    errors.push("metadata entry_page is not mayor/advice.");
  }

  if (metadata?.page_count !== expectedPages.length) {
    errors.push(`metadata page_count ${metadata?.page_count} does not match expected ${expectedPages.length}.`);
  }

  if (pageFiles.length !== expectedPages.length) {
    errors.push(`page file count ${pageFiles.length} does not match expected ${expectedPages.length}.`);
  }

  for (const expectedPath of expectedPagePaths) {
    if (!actualPagePaths.has(expectedPath)) {
      errors.push(`missing page file ${expectedPath}.`);
    }
  }

  for (const actualPath of actualPagePaths) {
    if (!expectedPagePaths.has(actualPath)) {
      errors.push(`unexpected page file ${actualPath}.`);
    }
  }

  if (indexHtml && !indexHtml.includes("Mayor")) {
    errors.push("index.html does not contain Mayor content.");
  }

  if (indexHtml?.includes("All Dashboard Pages")) {
    errors.push("index.html contains the old sitemap heading.");
  }

  const missingMetadataFiles = [];
  for (const page of metadata?.pages ?? []) {
    const filePath = path.join(snapshotDir, ...String(page.relative_path ?? "").split("/"));
    if (!(await pathExists(filePath))) {
      missingMetadataFiles.push(page.relative_path);
    }
  }

  if (missingMetadataFiles.length > 0) {
    errors.push(`metadata lists missing files: ${missingMetadataFiles.join(", ")}.`);
  }

  let buttonCount = 0;
  let disabledButtonCount = 0;
  const missingSnapshotTag = [];
  const missingMobileCss = [];
  const scriptFiles = [];
  const liveSrcFiles = [];
  const enabledButtonFiles = [];
  const missingScopeNav = [];
  const missingViewNav = [];

  for (const filePath of htmlFiles) {
    const html = await fs.readFile(filePath, "utf8");
    const relativePath = normalizeRelativePath(path.relative(snapshotDir, filePath));
    if (!html.includes("snapshot-header-tag")) {
      missingSnapshotTag.push(relativePath);
    }

    if (!/max-width:\s*760px/.test(html) || !/orientation:\s*portrait/.test(html) || !/\.dashboard-header\s*\{\s*display:\s*none\s*!important/.test(html)) {
      missingMobileCss.push(relativePath);
    }

    if (/<script\b/i.test(html)) {
      scriptFiles.push(relativePath);
    }

    if (/\ssrc=["'](?!data:)(?:\/api\/|https?:\/\/localhost)/i.test(html)) {
      liveSrcFiles.push(relativePath);
    }

    const buttons = Array.from(html.matchAll(/<button\b[^>]*>/gi)).map((match) => match[0]);
    buttonCount += buttons.length;
    const enabledButtons = buttons.filter((button) => !/\bdisabled\b/i.test(button));
    disabledButtonCount += buttons.length - enabledButtons.length;
    if (enabledButtons.length > 0) {
      enabledButtonFiles.push(relativePath);
    }

    if (!html.includes('data-snapshot-nav="scope"')) {
      missingScopeNav.push(relativePath);
    }

    const scopeMatch = relativePath.match(/^pages\/([^/]+)\//);
    const scopeViewCount = scopeMatch ? (expectedScopeViewCounts.get(scopeMatch[1]) ?? 0) : 0;
    if (scopeViewCount > 1 && !html.includes('data-snapshot-nav="view"')) {
      missingViewNav.push(relativePath);
    }
  }

  if (missingSnapshotTag.length > 0) {
    errors.push(`missing SNAPSHOT header tag: ${missingSnapshotTag.join(", ")}.`);
  }

  if (missingMobileCss.length > 0) {
    errors.push(`missing mobile header CSS: ${missingMobileCss.join(", ")}.`);
  }

  if (scriptFiles.length > 0) {
    errors.push(`script tags remain in: ${scriptFiles.join(", ")}.`);
  }

  if (liveSrcFiles.length > 0) {
    errors.push(`live src attributes remain in: ${liveSrcFiles.join(", ")}.`);
  }

  if (enabledButtonFiles.length > 0) {
    errors.push(`enabled buttons remain in: ${enabledButtonFiles.join(", ")}.`);
  }

  if (missingScopeNav.length > 0) {
    errors.push(`missing static scope navigation: ${missingScopeNav.join(", ")}.`);
  }

  if (missingViewNav.length > 0) {
    errors.push(`missing static view navigation: ${missingViewNav.join(", ")}.`);
  }

  if (await pathExists(path.join(snapshotDir, "metadata.partial.json"))) {
    errors.push("metadata.partial.json still exists.");
  }

  if (expectedRuntimeRoot && normalizeFilesystemPath(metadata?.health?.runtime?.runtime_root) !== normalizeFilesystemPath(expectedRuntimeRoot)) {
    errors.push(`health runtime_root '${metadata?.health?.runtime?.runtime_root}' does not match '${expectedRuntimeRoot}'.`);
  }

  if (expectedHostProcessPath && normalizeFilesystemPath(metadata?.health?.runtime?.host_process_path) !== normalizeFilesystemPath(expectedHostProcessPath)) {
    errors.push(`health host_process_path '${metadata?.health?.runtime?.host_process_path}' does not match '${expectedHostProcessPath}'.`);
  }

  const result = {
    ok: errors.length === 0,
    errors,
    entryPage: metadata?.entry_page ?? null,
    metadataPageCount: metadata?.page_count ?? null,
    expectedPageCount: expectedPages.length,
    pageFiles: pageFiles.length,
    htmlFiles: htmlFiles.length,
    buttonCount,
    disabledButtonCount,
    enabledButtonCount: buttonCount - disabledButtonCount,
    embeddedImages: metadata?.image_counts?.embedded ?? null,
    failedImages: metadata?.image_counts?.failed ?? null,
    runtimeRoot: metadata?.health?.runtime?.runtime_root ?? null,
    hostProcessPath: metadata?.health?.runtime?.host_process_path ?? null,
    dashboardUrl: metadata?.dashboard_url ?? null,
  };

  if (!result.ok && throwOnFailure) {
    throw new Error(`Static dashboard snapshot verification failed:\n${errors.join("\n")}`);
  }

  return result;
}

async function removeOldSinglePageArtifacts(snapshotDir) {
  const entries = await fs.readdir(snapshotDir, { withFileTypes: true });
  await Promise.all(entries
    .filter((entry) => entry.isFile() && /^dashboard-\d{8}-\d{4}\.(?:html|png)$/i.test(entry.name))
    .map((entry) => fs.rm(path.join(snapshotDir, entry.name), { force: true })));
}

function normalizeSnapshotText(text) {
  return String(text)
    .replace(/\r\n/g, "\n")
    .replace(/[ \t]+(?=\n)/g, "");
}

async function writeSnapshotText(filePath, text) {
  await fs.writeFile(filePath, normalizeSnapshotText(text), "utf8");
}

async function pathExists(filePath) {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

async function readTextIfExists(filePath) {
  try {
    return await fs.readFile(filePath, "utf8");
  } catch (error) {
    if (error?.code === "ENOENT") {
      return null;
    }

    throw error;
  }
}

async function readJsonIfExists(filePath) {
  const text = await readTextIfExists(filePath);
  if (text === null) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

async function listFilesRecursive(root, predicate = () => true) {
  if (!(await pathExists(root))) {
    return [];
  }

  const files = [];
  const entries = await fs.readdir(root, { withFileTypes: true });
  for (const entry of entries) {
    const filePath = path.join(root, entry.name);
    if (entry.isDirectory()) {
      files.push(...await listFilesRecursive(filePath, predicate));
    } else if (entry.isFile() && predicate(filePath)) {
      files.push(filePath);
    }
  }

  return files;
}

function normalizeRelativePath(value) {
  return String(value).replaceAll("\\", "/");
}

function normalizeFilesystemPath(value) {
  if (!value) {
    return "";
  }

  return path.resolve(String(value)).toLocaleLowerCase();
}

async function readSnapshotPartial(partialPath, capturedAt) {
  try {
    return JSON.parse(await fs.readFile(partialPath, "utf8"));
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }

    return { captured_at: capturedAt, entry_page: null, captured_pages: [] };
  }
}

async function captureRenderedDashboardPage(tab, pageUrl, capturedAt, settleMilliseconds) {
  await tab.goto(pageUrl);
  await tab.playwright.waitForLoadState({ state: "load", timeoutMs: 15000 });
  await tab.playwright.waitForTimeout(settleMilliseconds);
  await waitForDashboardMarkers(tab, pageUrl);

  const page = await tab.playwright.evaluate(async (input) => {
    function attributeEscape(value) {
      return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("\"", "&quot;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
    }

    function stylesheetText() {
      const chunks = [];
      for (const sheet of Array.from(document.styleSheets)) {
        try {
          const rules = Array.from(sheet.cssRules ?? []);
          if (rules.length > 0) {
            chunks.push(rules.map((rule) => rule.cssText).join("\n"));
          }
        } catch {
          if (sheet.href) {
            chunks.push(`@import url("${new URL(sheet.href, location.href).href}");`);
          }
        }
      }

      return chunks.join("\n\n");
    }

    function collectImageSources() {
      const images = [];
      for (const image of Array.from(document.images ?? [])) {
        const src = image.getAttribute("src");
        if (!src || src.startsWith("data:")) {
          continue;
        }

        const absoluteUrl = new URL(src, location.href).href;
        images.push({
          src,
          escapedSrc: attributeEscape(src),
          absoluteUrl,
        });
      }

      return images;
    }

    const text = document.body?.innerText ?? "";
    return {
      html: document.documentElement.outerHTML,
      cssText: stylesheetText(),
      title: document.title,
      url: location.href,
      hasDashboardTitle: text.includes("RimBob Dashboard v2"),
      hasSystemScope: text.includes("SYSTEM"),
      images: collectImageSources(),
    };
  }, { capturedAt, pageUrl });

  if (!page.hasDashboardTitle || !page.hasSystemScope) {
    throw new Error(`Rendered page did not contain expected RimBob dashboard markers: ${pageUrl}`);
  }

  return page;
}

async function waitForDashboardMarkers(tab, pageUrl) {
  let latest = { hasDashboardTitle: false, hasSystemScope: false, isKnownLoadingState: false, rootChildren: 0, text: "" };
  for (let pass = 0; pass < 2; pass += 1) {
    for (let attempt = 0; attempt < 80; attempt += 1) {
      latest = await tab.playwright.evaluate(() => {
        const text = document.body?.innerText ?? "";
        return {
          hasDashboardTitle: text.includes("RimBob Dashboard v2"),
          hasSystemScope: text.includes("SYSTEM"),
          isKnownLoadingState: text.includes("READING MASTER") || text.includes("Scanning Git history from master."),
          rootChildren: document.querySelector("#root")?.children.length ?? 0,
          text: text.slice(0, 180),
        };
      });

      if (latest.hasDashboardTitle && latest.hasSystemScope && !latest.isKnownLoadingState) {
        return;
      }

      await tab.playwright.waitForTimeout(250);
    }

    await tab.reload();
    await tab.playwright.waitForLoadState({ state: "load", timeoutMs: 15000 });
  }

  throw new Error(`Dashboard shell did not finish rendering before capture: ${pageUrl} rootChildren=${latest.rootChildren} loading=${latest.isKnownLoadingState} text=${JSON.stringify(latest.text)}`);
}

function urlForPage(dashboardUrl, page) {
  const url = new URL(dashboardUrl);
  url.searchParams.set("scope", page.scope);
  url.searchParams.set("view", page.view);
  return url.href;
}

async function embedImages(images) {
  const result = [];
  let embedded = 0;
  let failed = 0;

  for (const image of images) {
    const cached = embeddedImageCache.get(image.absoluteUrl);
    if (cached) {
      result.push({
        ...image,
        dataUrl: cached.dataUrl,
        error: cached.error,
      });
      if (cached.error) {
        failed += 1;
      } else {
        embedded += 1;
      }
      continue;
    }

    try {
      const { contentType, bytes } = await fetchImageBytes(image.absoluteUrl);
      const dataUrl = `data:${contentType};base64,${bytes.toString("base64")}`;
      embeddedImageCache.set(image.absoluteUrl, { dataUrl });
      result.push({
        ...image,
        dataUrl,
      });
      embedded += 1;
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      const dataUrl = failedImagePlaceholderDataUrl(image);
      embeddedImageCache.set(image.absoluteUrl, { dataUrl, error: errorMessage });
      result.push({
        ...image,
        dataUrl,
        error: errorMessage,
      });
      failed += 1;
    }
  }

  return { images: result, counts: { embedded, failed } };
}

async function fetchImageBytes(absoluteUrl) {
  let lastError = null;
  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await fetch(absoluteUrl);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const contentType = response.headers.get("content-type") ?? "application/octet-stream";
      const bytes = Buffer.from(await response.arrayBuffer());
      return { contentType, bytes };
    } catch (error) {
      lastError = error;
      await new Promise((resolve) => setTimeout(resolve, 150));
    }
  }

  throw lastError;
}

function failedImagePlaceholderDataUrl(image) {
  const label = htmlEscape(path.posix.basename(image.src).replaceAll("_", " "));
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 48 48" role="img" aria-label="${label}"><rect width="48" height="48" rx="8" fill="#152026"/><path d="M12 33l8-10 6 7 4-5 6 8H12z" fill="#5f737d"/><circle cx="31" cy="17" r="4" fill="#8aa1aa"/></svg>`;
  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}

function htmlEscape(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("\"", "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function buildSiteIndex({ capturedAt, dashboardUrl, health, pages, title }) {
  const groups = new Map();
  for (const page of pages) {
    const key = `${page.kind}:${page.scope}`;
    if (!groups.has(key)) {
      groups.set(key, {
        kind: page.kind,
        scope: page.scope,
        scopeLabel: page.scopeLabel,
        status: page.status,
        pages: [],
      });
    }

    groups.get(key).pages.push(page);
  }

  const groupMarkup = Array.from(groups.values()).map((group) => `
    <section class="snapshot-group">
      <header>
        <span>${htmlEscape(group.kind)} / ${htmlEscape(group.status)}</span>
        <h2>${htmlEscape(group.scopeLabel)}</h2>
      </header>
      <div class="snapshot-links">
        ${group.pages.map((page) => `
          <a href="${htmlEscape(page.relativePath)}">
            <strong>${htmlEscape(page.viewLabel)}</strong>
            <small>${htmlEscape(page.scope)} / ${htmlEscape(page.view)}</small>
          </a>
        `).join("")}
      </div>
    </section>
  `).join("");

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${htmlEscape(title)}</title>
  <style>
    :root {
      color-scheme: dark;
      font-family: Bahnschrift, Aptos, "Segoe UI", sans-serif;
      background: #080b0d;
      color: #e7edf0;
      --panel: #10191d;
      --line: #26373d;
      --muted: #89979d;
      --cyan: #62d8e6;
      --ok: #50d38d;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background: linear-gradient(135deg, rgba(98,216,230,.08), transparent 28%), #080b0d;
      padding: 28px;
    }
    main { max-width: 1180px; margin: 0 auto; display: grid; gap: 18px; }
    .hero, .snapshot-group {
      border: 1px solid var(--line);
      border-radius: 8px;
      background: linear-gradient(rgba(20,33,39,.96), rgba(10,15,18,.96));
      padding: 18px;
      box-shadow: 0 22px 80px rgba(0,0,0,.25);
    }
    .eyebrow, .snapshot-group header span {
      color: var(--muted);
      font-size: .72rem;
      font-weight: 700;
      letter-spacing: .08em;
      text-transform: uppercase;
    }
    h1, h2, p { margin: 0; }
    h1 { font-size: 1.7rem; margin-top: 6px; }
    h2 { font-size: 1.1rem; margin-top: 4px; }
    .meta {
      margin-top: 14px;
      display: grid;
      gap: 6px;
      color: var(--muted);
      font-size: .9rem;
    }
    code { color: #c9f7ff; overflow-wrap: anywhere; }
    .snapshot-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 14px; }
    .snapshot-links { margin-top: 12px; display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 8px; }
    a {
      color: inherit;
      text-decoration: none;
      display: grid;
      gap: 4px;
      border: 1px solid var(--line);
      border-radius: 7px;
      padding: 10px;
      background: rgba(255,255,255,.035);
    }
    a:hover { border-color: rgba(98,216,230,.75); background: rgba(98,216,230,.10); }
    a strong { color: var(--cyan); }
    a small { color: var(--muted); }
  </style>
</head>
<body>
  <main>
    <section class="hero">
      <span class="eyebrow">RimBob static dashboard snapshot</span>
      <h1>All Dashboard Pages</h1>
      <div class="meta">
        <span>Captured: <code>${htmlEscape(capturedAt)}</code></span>
        <span>Source: <code>${htmlEscape(dashboardUrl)}</code></span>
        <span>Runtime root: <code>${htmlEscape(health?.runtime_root ?? "")}</code></span>
        <span>Host process: <code>${htmlEscape(health?.host_process_path ?? "")}</code></span>
        <span>Pages: <code>${pages.length}</code></span>
      </div>
    </section>
    <div class="snapshot-grid">
      ${groupMarkup}
    </div>
  </main>
</body>
</html>
`;
}

function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function toStaticHtml(sourceHtml, cssText, images, capturedAt, dashboardUrl, snapshotNavigation = null) {
  let html = sourceHtml
    .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, "")
    .replace(/<link\b(?=[^>]*\brel=["']?(?:modulepreload|preload|stylesheet)["']?)[^>]*>/gi, "")
    .replace(/<link\b(?=[^>]*\bas=["']?script["']?)[^>]*>/gi, "");

  for (const image of images) {
    if (!image.dataUrl) {
      continue;
    }

    const sourceMarker = [
      `data-snapshot-source-src="${htmlEscape(image.src)}"`,
      image.error ? `data-snapshot-image-error="${htmlEscape(image.error)}"` : "",
    ].filter(Boolean).join(" ");
    html = html.replace(
      new RegExp(`(<img\\b[^>]*?)\\ssrc="${escapeRegExp(image.escapedSrc)}"`, "g"),
      `$1 src="${image.dataUrl}" ${sourceMarker}`,
    );
    html = html.replace(
      new RegExp(`(<img\\b[^>]*?)\\ssrc='${escapeRegExp(image.escapedSrc)}'`, "g"),
      `$1 src="${image.dataUrl}" ${sourceMarker}`,
    );
  }

  if (snapshotNavigation) {
    html = linkStaticSnapshotNavigation(html, snapshotNavigation);
  }

  html = injectSnapshotHeaderTag(html);
  html = disableSnapshotButtons(html);

  const metadata = `<meta name="dashboard-snapshot" content="captured_at=${htmlEscape(capturedAt)}; source=${htmlEscape(dashboardUrl)}">`;
  const style = `<style data-dashboard-snapshot="inline-styles">\n${cssText}\n${snapshotNavigationCss()}\n</style>`;
  html = html.replace(/<\/head>/i, `${metadata}\n${style}\n</head>`);
  html = html.replace(/<body\b/i, "<body data-dashboard-snapshot-static=\"true\"");

  return `<!doctype html>\n${html}\n`;
}

function linkStaticSnapshotNavigation(html, { currentPage, pages }) {
  let linkedHtml = linkScopeButtons(html, currentPage, pages);
  linkedHtml = linkViewTabs(linkedHtml, currentPage, pages);
  return linkedHtml;
}

function injectSnapshotHeaderTag(html) {
  if (html.includes("snapshot-header-tag")) {
    return html;
  }

  return html.replace(
    /(<h1>RimBob Dashboard v2<\/h1>)/i,
    `$1<span class="snapshot-header-tag" aria-label="Static dashboard snapshot">SNAPSHOT</span>`,
  );
}

function disableSnapshotButtons(html) {
  return html.replace(/<button\b([^>]*)>/gi, (match, attributes) => {
    if (/\bdisabled\b/i.test(attributes)) {
      return match;
    }

    return `<button${attributes} disabled aria-disabled="true" data-snapshot-disabled="true">`;
  });
}

function linkScopeButtons(html, currentPage, pages) {
  const targets = scopeNavigationTargets(pages);
  let targetIndex = 0;
  return html.replace(
    /<button\b((?=[^>]*\bclass=["'][^"']*\bscope-button\b)[^>]*)>([\s\S]*?)<\/button>/gi,
    (match, attributes, content) => {
      const target = targets[targetIndex];
      targetIndex += 1;
      if (!target) {
        return match;
      }

      const href = relativeSnapshotHref(currentPage.relativePath, target.relativePath);
      const isCurrent = target.scope === currentPage.scope;
      return `<a${anchorAttributesForButton(attributes, href, "scope", isCurrent)}>${content}</a>`;
    },
  );
}

function linkViewTabs(html, currentPage, pages) {
  const targets = pages.filter((page) => page.scope === currentPage.scope);
  return html.replace(
    /<div class="view-tabs"([^>]*)>([\s\S]*?)<\/div>/gi,
    (match, attributes, content) => {
      let targetIndex = 0;
      const linkedContent = content.replace(/<button\b([^>]*)>([\s\S]*?)<\/button>/gi, (buttonMatch, buttonAttributes, buttonContent) => {
        const target = targets[targetIndex];
        targetIndex += 1;
        if (!target) {
          return buttonMatch;
        }

        const href = relativeSnapshotHref(currentPage.relativePath, target.relativePath);
        const isCurrent = target.scope === currentPage.scope && target.view === currentPage.view;
        return `<a${anchorAttributesForButton(buttonAttributes, href, "view", isCurrent)}>${buttonContent}</a>`;
      });
      return `<div class="view-tabs"${attributes}>${linkedContent}</div>`;
    },
  );
}

function scopeNavigationTargets(pages) {
  const targets = [];
  const seenScopes = new Set();
  for (const page of pages) {
    if (seenScopes.has(page.scope)) {
      continue;
    }

    seenScopes.add(page.scope);
    targets.push(defaultPageForScope(page, pages));
  }

  return targets;
}

function defaultPageForScope(scopePage, pages) {
  const scopePages = pages.filter((page) => page.scope === scopePage.scope);
  if (scopePage.kind === "minister") {
    return scopePages.find((page) => page.view === "advice") ?? scopePages[0];
  }

  return scopePages[0];
}

function relativeSnapshotHref(fromRelativePath, toRelativePath) {
  const fromDirectory = path.posix.dirname(fromRelativePath);
  return path.posix.relative(fromDirectory, toRelativePath) || path.posix.basename(toRelativePath);
}

function anchorAttributesForButton(buttonAttributes, href, navigationKind, isCurrent) {
  const cleanedAttributes = buttonAttributes
    .replace(/\s+type=["']button["']/gi, "")
    .trim();
  const preservedAttributes = cleanedAttributes ? ` ${cleanedAttributes}` : "";
  const currentAttribute = isCurrent ? ` aria-current="page"` : "";
  return `${preservedAttributes} href="${htmlEscape(href)}" data-snapshot-nav="${navigationKind}"${currentAttribute}`;
}

function snapshotNavigationCss() {
  return `
body[data-dashboard-snapshot-static="true"] a.scope-button,
body[data-dashboard-snapshot-static="true"] .view-tabs a {
  color: inherit;
  text-decoration: none;
}
body[data-dashboard-snapshot-static="true"] .view-tabs a {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  border: 1px solid var(--line);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.03);
  color: var(--muted);
  padding: 8px 12px;
  white-space: nowrap;
}
body[data-dashboard-snapshot-static="true"] .view-tabs a.active,
body[data-dashboard-snapshot-static="true"] .view-tabs a[aria-selected="true"] {
  color: var(--text);
  border-color: rgba(80, 211, 141, 0.7);
  background: rgba(80, 211, 141, 0.12);
}
body[data-dashboard-snapshot-static="true"] a.scope-button:hover,
body[data-dashboard-snapshot-static="true"] .view-tabs a:hover {
  border-color: rgba(98, 216, 230, 0.75);
}
body[data-dashboard-snapshot-static="true"] a.scope-button:focus-visible,
body[data-dashboard-snapshot-static="true"] .view-tabs a:focus-visible {
  outline: 2px solid var(--cyan);
  outline-offset: 2px;
}
body[data-dashboard-snapshot-static="true"] .snapshot-header-tag {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  align-self: center;
  min-height: 34px;
  border: 2px solid rgba(240, 189, 95, 0.95);
  border-radius: 7px;
  background: rgba(240, 189, 95, 0.18);
  color: #ffe7ad;
  padding: 4px 12px;
  font-size: 1rem;
  font-weight: 950;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  box-shadow: 0 0 0 1px rgba(240, 189, 95, 0.22), 0 0 22px rgba(240, 189, 95, 0.18);
}
@media (max-width: 760px), (orientation: portrait) {
  body[data-dashboard-snapshot-static="true"] .dashboard-v2-shell {
    grid-template-rows: 1fr;
  }

  body[data-dashboard-snapshot-static="true"] .dashboard-header {
    display: none !important;
  }
}
body[data-dashboard-snapshot-static="true"] button[disabled][data-snapshot-disabled="true"] {
  cursor: not-allowed;
  opacity: 0.48;
  filter: grayscale(0.2);
}
`;
}
