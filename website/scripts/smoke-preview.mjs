import process from 'node:process';

const baseUrl = new URL(
  process.env.NODAL_DOCS_PREVIEW_URL ?? 'http://127.0.0.1:3010/');

async function fetchWithRetry(path, attempts = 30) {
  const url = new URL(path, baseUrl);
  let lastError;

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      const response = await fetch(url, {redirect: 'follow'});
      if (response.ok) {
        return response;
      }

      lastError = new Error(`${url} returned HTTP ${response.status}.`);
    } catch (error) {
      lastError = error;
    }

    if (attempt < attempts) {
      await new Promise(resolve => setTimeout(resolve, 500));
    }
  }

  throw new Error(`Preview did not become healthy: ${lastError?.message}`);
}

function requireMatch(content, expression, description) {
  const match = expression.exec(content);
  if (!match) {
    throw new Error(`The API page does not reference ${description}.`);
  }

  return match[1];
}

const homeResponse = await fetchWithRetry('/');
const home = await homeResponse.text();
if (!home.includes('One graph model.')) {
  throw new Error('The Docusaurus home page did not contain its expected heading.');
}

const apiResponse = await fetchWithRetry('/api/index.html');
const apiUrl = new URL(apiResponse.url);
if (!['/api', '/api/'].includes(apiUrl.pathname)) {
  throw new Error(
    `The API index resolved outside its expected route; received ${apiUrl.pathname}.`);
}

const api = await apiResponse.text();
if (!api.includes('Nodal Framework API reference')) {
  throw new Error('The generated DocFX API index did not contain its expected heading.');
}

const stylesheetPath = requireMatch(
  api,
  /<link[^>]+href=["']([^"']*public\/docfx\.min\.css)["']/i,
  'the DocFX stylesheet');
const scriptPath = requireMatch(
  api,
  /<script[^>]+src=["']([^"']*public\/docfx\.min\.js)["']/i,
  'the DocFX runtime');
const logoPath = requireMatch(
  api,
  /<img[^>]+src=["']([^"']*logo\.svg)["']/i,
  'the DocFX logo');
const basePath = requireMatch(
  api,
  /<base[^>]+href=["']([^"']+)["']/i,
  'the integrated API base path');

if (basePath !== '/api/') {
  throw new Error(`The DocFX base path must be '/api/'; received '${basePath}'.`);
}

const assetBaseUrl = new URL(basePath, apiResponse.url);
const stylesheetUrl = new URL(stylesheetPath, assetBaseUrl);
const scriptUrl = new URL(scriptPath, assetBaseUrl);
const logoUrl = new URL(logoPath, assetBaseUrl);

for (const [kind, url, expectedType] of [
  ['stylesheet', stylesheetUrl, 'text/css'],
  ['runtime', scriptUrl, 'javascript'],
  ['logo', logoUrl, 'image/svg'],
]) {
  const response = await fetchWithRetry(url.pathname);
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes(expectedType)) {
    throw new Error(
      `The DocFX ${kind} returned '${contentType}' instead of '${expectedType}' from ${url.pathname}.`);
  }
}

console.log(`Documentation preview smoke test passed at ${baseUrl}`);
console.log(`DocFX canonical URL: ${apiResponse.url}`);
console.log(`DocFX stylesheet: ${stylesheetUrl.pathname}`);
console.log(`DocFX runtime: ${scriptUrl.pathname}`);
console.log(`DocFX logo: ${logoUrl.pathname}`);
