import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = process.cwd();

async function copy(source, destination) {
  const target = path.join(root, destination);
  await mkdir(path.dirname(target), { recursive: true });
  await copyFile(path.join(root, source), target);
}

await Promise.all([
  copy('node_modules/easymde/dist/easymde.min.css', 'wwwroot/vendor/easymde/easymde.min.css'),
  copy('node_modules/easymde/dist/easymde.min.js', 'wwwroot/vendor/easymde/easymde.min.js'),
  copy('node_modules/vis-network/standalone/umd/vis-network.min.js', 'wwwroot/vendor/vis-network/vis-network.min.js'),
  copy('node_modules/easymde/LICENSE', 'wwwroot/vendor/licenses/easymde-MIT.txt'),
  copy('node_modules/vis-network/LICENSE-APACHE-2.0', 'wwwroot/vendor/licenses/vis-network-APACHE-2.0.txt'),
  copy('node_modules/vis-network/LICENSE-MIT', 'wwwroot/vendor/licenses/vis-network-MIT.txt'),
  copy('node_modules/@fontsource-variable/space-grotesk/LICENSE', 'wwwroot/vendor/licenses/space-grotesk-OFL-1.1.txt'),
  copy('node_modules/@fontsource-variable/inter/LICENSE', 'wwwroot/vendor/licenses/inter-OFL-1.1.txt'),
  copy('node_modules/@fontsource/ibm-plex-mono/LICENSE', 'wwwroot/vendor/licenses/ibm-plex-mono-OFL-1.1.txt')
]);

const visNetworkPath = path.join(root, 'wwwroot/vendor/vis-network/vis-network.min.js');
const visNetworkSource = await readFile(visNetworkPath, 'utf8');
await writeFile(
  visNetworkPath,
  visNetworkSource.replace(/\r?\n?\/\/# sourceMappingURL=vis-network\.min\.js\.map\s*$/, ''),
  'utf8'
);

const fontAssets = [
  ...['latin-ext', 'latin'].flatMap(subset => [
    {
      source: `node_modules/@fontsource-variable/space-grotesk/files/space-grotesk-${subset}-wght-normal.woff2`,
      destination: `wwwroot/fonts/space-grotesk-${subset}-wght-normal.woff2`
    },
    {
      source: `node_modules/@fontsource-variable/inter/files/inter-${subset}-wght-normal.woff2`,
      destination: `wwwroot/fonts/inter-${subset}-wght-normal.woff2`
    },
    ...[400, 500, 600].map(weight => ({
      source: `node_modules/@fontsource/ibm-plex-mono/files/ibm-plex-mono-${subset}-${weight}-normal.woff2`,
      destination: `wwwroot/fonts/ibm-plex-mono-${subset}-${weight}-normal.woff2`
    }))
  ])
];

await Promise.all(fontAssets.map(asset => copy(asset.source, asset.destination)));

const latinExtRange = 'U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+0304,U+0308,U+0329,U+1D00-1DBF,U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF';
const latinRange = 'U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD';

function fontFace(family, weight, source, range, format = 'woff2') {
  return `@font-face {
  font-family: '${family}';
  font-style: normal;
  font-display: swap;
  font-weight: ${weight};
  src: url('${source}') format('${format}');
  unicode-range: ${range};
}`;
}

const fontCss = `${fontFace(
  'Space Grotesk Variable',
  '300 700',
  '/fonts/space-grotesk-latin-ext-wght-normal.woff2',
  latinExtRange,
  'woff2-variations'
)}

${fontFace(
  'Space Grotesk Variable',
  '300 700',
  '/fonts/space-grotesk-latin-wght-normal.woff2',
  latinRange,
  'woff2-variations'
)}

${fontFace(
  'Inter Variable',
  '100 900',
  '/fonts/inter-latin-ext-wght-normal.woff2',
  latinExtRange,
  'woff2-variations'
)}

${fontFace(
  'Inter Variable',
  '100 900',
  '/fonts/inter-latin-wght-normal.woff2',
  latinRange,
  'woff2-variations'
)}

${[400, 500, 600].flatMap(weight => [
  fontFace(
    'IBM Plex Mono',
    weight,
    `/fonts/ibm-plex-mono-latin-ext-${weight}-normal.woff2`,
    latinExtRange
  ),
  fontFace(
    'IBM Plex Mono',
    weight,
    `/fonts/ibm-plex-mono-latin-${weight}-normal.woff2`,
    latinRange
  )
]).join('\n\n')}
`;

await writeFile(path.join(root, 'wwwroot/css/fonts.css'), fontCss, 'utf8');

const requiredOutputs = [
  'wwwroot/css/tailwind.min.css',
  'wwwroot/css/fonts.css',
  'wwwroot/vendor/easymde/easymde.min.css',
  'wwwroot/vendor/easymde/easymde.min.js',
  'wwwroot/vendor/vis-network/vis-network.min.js',
  ...fontAssets.map(asset => asset.destination)
];

for (const output of requiredOutputs) {
  await readFile(path.join(root, output));
}
