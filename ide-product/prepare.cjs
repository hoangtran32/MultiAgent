'use strict';
// Build our product from a pinned source tree; never patch an installed third-party editor.
const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const root = path.resolve(__dirname, '..');
const source = path.resolve(process.argv[2]);
const pin = require('./upstream.json');
if (cp.execFileSync('git', ['rev-parse', 'HEAD'], { cwd: source, encoding: 'utf8' }).trim() !== pin.commit) throw Error('Unexpected editor source commit');
const version = fs.readFileSync(path.join(root, 'src-desktop/MultiAgentHub.csproj'), 'utf8').match(/<Version>([^<]+)<\/Version>/)[1];
const file = path.join(source, 'product.json');
const product = JSON.parse(fs.readFileSync(file, 'utf8'));
Object.assign(product, {
  nameShort: 'Hub Code', nameLong: 'Hub Code', applicationName: 'hub-code',
  dataFolderName: '.multiagent-hub-code', sharedDataFolderName: '.multiagent-hub-shared',
  win32MutexName: 'multiagenthubcode', win32DirName: 'Hub Code', win32NameVersion: 'Hub Code',
  win32RegValueName: 'MultiAgentHubCode', win32AppUserModelId: 'MultiAgentHub.Code', win32ShellNameShort: 'Hub Code',
  win32x64AppId: '{{325E441D-6D84-4AC1-9CDE-A75BD59A7F63}',
  win32x64UserAppId: '{{38A1AB30-C83C-440F-BC10-30552B830E48}',
  win32TunnelServiceMutex: 'multiagenthub-tunnelservice', win32TunnelMutex: 'multiagenthub-tunnel',
  urlProtocol: 'multiagent-hub', quality: 'oss', hubVersion: version,
  hubSourceCommit: cp.execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
  reportIssueUrl: 'https://github.com/hoangtran32/MultiAgent/issues/new',
  licenseUrl: 'https://github.com/microsoft/vscode/blob/' + pin.commit + '/LICENSE.txt',
  extensionsGallery: { serviceUrl: 'https://open-vsx.org/vscode/gallery', itemUrl: 'https://open-vsx.org/vscode/item' },
  enableTelemetry: false, enableCrashReporter: false
});
// Hub supplies its own agent. Do not bundle or advertise a second account/provider.
delete product.defaultChatAgent;
delete product.updateUrl;
delete product.downloadUrl;
delete product.configurationSync;
fs.writeFileSync(file, JSON.stringify(product, null, 2) + '\n');
const copilot = path.resolve(source, 'extensions', 'copilot');
if (!copilot.startsWith(source + path.sep)) throw Error('Unsafe editor customization path');
fs.rmSync(copilot, { recursive: true, force: true });
const dirsFile = path.join(source, 'build/npm/dirs.ts');
const dirs = fs.readFileSync(dirsFile, 'utf8');
if (!dirs.includes("'extensions/copilot',")) throw Error('Upstream dependency layout changed');
fs.writeFileSync(dirsFile, dirs.replace(/^.*'extensions\/copilot',.*\r?\n/m, ''));
const packingFile = path.join(source, 'build/gulpfile.vscode.ts');
const packing = fs.readFileSync(packingFile, 'utf8');
const shimCall = 'prepareBuiltInCopilotRipgrepShim(platform, arch, builtInCopilotExtensionDir, appNodeModulesDir);';
if (!packing.includes(shimCall)) throw Error('Upstream agent packing hook changed');
fs.writeFileSync(packingFile, packing.replace(shimCall, 'if (fs.existsSync(builtInCopilotExtensionDir)) ' + shimCall));
const electronFile = path.join(source, 'build/lib/electron.ts');
const electron = fs.readFileSync(electronFile, 'utf8');
if (!electron.includes("companyName: 'Microsoft Corporation'")) throw Error('Upstream executable branding hook changed');
fs.writeFileSync(electronFile, electron.replace("companyName: 'Microsoft Corporation'", "companyName: 'Multi-Agent Hub contributors'")
  .replace("copyright: 'Copyright (C) 2026 Microsoft. All rights reserved'", "copyright: 'Copyright (C) 2026 Multi-Agent Hub contributors. Includes MIT-licensed Microsoft code.'"));
fs.copyFileSync(path.join(root, 'app.ico'), path.join(source, 'resources/win32/code.ico'));
const icon = fs.readFileSync(path.join(root, 'ide-extension/icon.svg'));
for (const relative of [
  'src/vs/workbench/browser/media/code-icon.svg',
  ...['dark', 'light', 'hcDark', 'hcLight'].map(t => `src/vs/workbench/browser/parts/editor/media/letterpress-${t}.svg`)
]) { const target = path.join(source, relative); if (!fs.existsSync(target)) throw Error('Missing branding target: ' + relative); fs.writeFileSync(target, icon); }
// The editor's API version remains upstream's version; Hub's release version is separate.
const pkgFile = path.join(source, 'package.json');
const pkg = fs.readFileSync(pkgFile, 'utf8').replaceAll('--max-old-space-size=8192', '--max-old-space-size=6144');
fs.writeFileSync(pkgFile, pkg);
console.log('Prepared Hub Code ' + version + ' from editor source ' + pin.commit);
