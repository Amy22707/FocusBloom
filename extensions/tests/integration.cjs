const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');

const repo = path.resolve(__dirname, '../..');
if (!process.env.FOCUSBLOOM_PLAYWRIGHT) throw new Error('请将 FOCUSBLOOM_PLAYWRIGHT 设为可用的 playwright 模块路径。');
const { chromium } = require(process.env.FOCUSBLOOM_PLAYWRIGHT);
const temporary = path.join(repo, '.tools', 'standalone-extension', String(process.pid));
const extension = path.join(temporary, 'extension');
const profile = path.join(temporary, 'profile');
fs.mkdirSync(temporary, { recursive:true });
fs.cpSync(path.join(repo, 'extensions', 'browser'), extension, { recursive:true });
const manifest = JSON.parse(fs.readFileSync(path.join(extension, 'manifest.json'), 'utf8').replace(/^\uFEFF/, ''));
assert.ok(!manifest.permissions.includes('nativeMessaging'));
let context;
let extensionId;
let popup;
let blockedPage;
const webServer = http.createServer((request, reply) => {
  if (request.url === '/asset.svg') {
    reply.writeHead(200, { 'Content-Type':'image/svg+xml' });
    reply.end('<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12"><rect width="12" height="12" fill="green"/></svg>'); return;
  }
  reply.writeHead(200, { 'Content-Type':'text/html; charset=utf-8' });
  reply.end(`<html><head><title>学习页面</title></head><body><h1>学习页面</h1><img id="asset" src="http://localhost:${webServer.address().port}/asset.svg"></body></html>`);
});
const waitUntil = async predicate => {
  const deadline = Date.now() + 15000;
  while (!(await predicate()) && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve,50));
  assert.ok(await predicate(), '浏览器未达到预期状态');
};
async function launchBrowser() {
  const launch = { headless:true, ignoreDefaultArgs:['--disable-extensions'],
    args:['--enable-unsafe-extension-debugging', `--disable-extensions-except=${extension}`, `--load-extension=${extension}`] };
  if (process.env.FOCUSBLOOM_BROWSER) launch.executablePath = process.env.FOCUSBLOOM_BROWSER;
  if (launch.executablePath?.endsWith('chrome.exe')) launch.args = launch.args.filter(arg => !arg.startsWith('--load-extension=') && !arg.startsWith('--disable-extensions-except='));
  context = await chromium.launchPersistentContext(profile, launch);
  context.setDefaultTimeout(15000);
  let id;
  if (launch.executablePath?.endsWith('chrome.exe')) {
    const cdp = await context.browser().newBrowserCDPSession();
    id = (await cdp.send('Extensions.loadUnpacked', { path:extension })).id;
    const loaded = await cdp.send('Extensions.getExtensions');
    assert.ok(loaded.extensions.some(item => item.id === id && item.enabled));
    await cdp.detach();
  } else {
    const worker = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker');
    id = worker.url().split('/')[2];
  }
  if (extensionId) assert.equal(id, extensionId);
  extensionId = id;
  blockedPage = `chrome-extension://${extensionId}/blocked.html`;
  popup = await context.newPage();
  await popup.goto(`chrome-extension://${extensionId}/popup.html`);
  await popup.locator('#timer').waitFor();
}
const command = payload => popup.evaluate(message => chrome.runtime.sendMessage(message), payload);
const getView = () => command({ type:'getView' });
async function visitRestricted(page, address) {
  try { await page.goto(address); }
  catch (error) { if (!error.message.includes('ERR_ABORTED')) throw error; }
  await waitUntil(() => page.url().startsWith(blockedPage));
}

(async () => {
  try {
    await new Promise(resolve => webServer.listen({ host:'127.0.0.1', port:0 }, resolve));
    await launchBrowser();
    const port = webServer.address().port;
    const allowed = `http://127.0.0.1:${port}/lesson`;
    const denied = `http://localhost:${port}/outside`;
    const page = await context.newPage();
    await page.goto(denied);
    await popup.locator('[data-pane=rules]').click();
    await popup.locator('#domain').fill('127.0.0.1');
    await popup.locator('#subdomains').uncheck();
    await popup.locator('#websiteForm button[type=submit]').click();
    await waitUntil(async () => (await getView()).state.websites.length === 1);
    await popup.locator('[data-pane=preferences]').click();
    await popup.locator('#projectName').fill('独立学习验证');
    await popup.locator('#backToFocus').click();
    await popup.locator('#start').click();
    await waitUntil(async () => (await getView()).effective);
    await waitUntil(() => page.url().startsWith(blockedPage));
    await page.goto(allowed);
    await page.waitForFunction(() => document.querySelector('#asset')?.naturalWidth === 12);
    const deniedTab = await context.newPage();
    await visitRestricted(deniedTab, denied);
    await popup.locator('#pause').click();
    await waitUntil(async () => (await getView()).state.state === 'Paused');
    assert.equal((await getView()).effective, true);
    await popup.locator('[data-pane=rules]').click();
    assert.ok(await popup.locator('#listMode').isDisabled());
    const rejected = await command({ type:'setWebsiteMode', fields:{ listMode:'Blacklist' } });
    assert.match(rejected.error, /结束/);
    await popup.locator('[data-pane=focus]').click();
    await popup.locator('#resume').click();
    await waitUntil(async () => (await getView()).state.state === 'Focusing');
    await command({ type:'setEnabled', enabled:false });
    await waitUntil(() => deniedTab.url() === denied);
    await command({ type:'setEnabled', enabled:true });
    await waitUntil(() => deniedTab.url().startsWith(blockedPage));
    await popup.locator('#end').click();
    await waitUntil(() => deniedTab.url() === denied);
    await popup.locator('[data-pane=rules]').click();
    await popup.locator('#listMode').selectOption('Blacklist');
    await popup.locator('#domain').fill('localhost');
    await popup.locator('#websiteForm button[type=submit]').click();
    await waitUntil(async () => (await getView()).state.websites[0]?.domain === 'localhost');
    await popup.locator('[data-pane=focus]').click();
    await popup.locator('#start').click();
    await waitUntil(() => deniedTab.url().startsWith(blockedPage));
    await page.goto(allowed);
    assert.equal(page.url(), allowed);
    const beforeRestart = (await getView()).state;
    await context.close(); context = undefined;
    await launchBrowser();
    const afterRestart = (await getView()).state;
    assert.equal(afterRestart.state, 'Focusing');
    assert.equal(afterRestart.project, beforeRestart.project);
    assert.equal(afterRestart.websiteMode, 'Blacklist');
    assert.deepEqual(afterRestart.sessionWebsites, beforeRestart.sessionWebsites);
    assert.ok((await getView()).effective);
    const blocked = await context.newPage();
    await visitRestricted(blocked, denied);
    fs.mkdirSync(path.join(repo, 'artifacts', 'qa'), { recursive:true });
    await blocked.screenshot({ path:path.join(repo, 'artifacts', 'qa', 'extension-blocked.png') });
    await popup.locator('#end').click();
    await waitUntil(() => blocked.url() === denied);
    await popup.locator('#quickSetup').click();
    await popup.locator('#timerMode').selectOption('Countdown');
    await popup.locator('#durationMinutes').fill('0.1');
    assert.equal(await popup.locator('#durationMinutes').evaluate(input => input.checkValidity()), false);
    const tooShort = await command({type:'start',fields:{timerMode:'Countdown',durationMinutes:.1}});
    assert.match(tooShort.error,/10秒/);
    await popup.locator('#durationMinutes').fill('0.16666666666666666');
    await popup.locator('#restMinutes').fill('0.025');
    await popup.locator('#backToFocus').click();
    await popup.locator('#start').click();
    await waitUntil(async () => (await getView()).state.state === 'Resting');
    await waitUntil(() => blocked.url() === denied);
    assert.equal((await getView()).effective, false);
    await waitUntil(async () => (await getView()).state.state === 'Ended');
    await popup.locator('#settings').click();
    await popup.locator('#timerMode').selectOption('CountUp');
    await popup.locator('#restMinutes').fill('0');
    await popup.locator('#timerForm button[type=submit]').click();
    await popup.locator('[data-pane=rules]').click();
    await popup.locator('#listMode').selectOption('Whitelist');
    await waitUntil(async () => (await getView()).state.websites[0]?.domain === '127.0.0.1');
    await popup.locator('[data-pane=preferences]').click();
    await popup.locator('#palette').selectOption('sage');
    await popup.locator('#appearance').selectOption('Dark');
    await waitUntil(async () => (await getView()).preferences.appearance === 'Dark');
    await popup.locator('#backToFocus').click();
    assert.ok(await popup.locator('#timer').isVisible());
    assert.ok(await popup.locator('#timerForm').isHidden());
    await popup.setViewportSize({ width:400, height:600 });
    assert.ok((await popup.locator('#start').boundingBox()).y < 540, '面板主要操作应完整可见');
    assert.ok(await popup.locator('.focusNote').evaluate(element => element.getBoundingClientRect().bottom <= 600), '面板底部内容应完整可见');
    assert.ok(await popup.evaluate(() => document.documentElement.scrollWidth <= 400), '面板不能横向溢出');
    await popup.screenshot({ path:path.join(repo, 'artifacts', 'qa', 'extension-popup-dark.png'), animations:'disabled' });
    await command({ type:'setTheme', fields:{ palette:'fog-blue', appearance:'Light' } });
    await popup.screenshot({ path:path.join(repo, 'artifacts', 'qa', 'extension-popup.png'), animations:'disabled' });
    await popup.locator('#open').click();
    await waitUntil(() => context.pages().some(item => item.url().includes('popup.html?view=page')));
    const workspace = context.pages().find(item => item.url().includes('popup.html?view=page'));
    await workspace.locator('#fullscreen').click();
    assert.ok(await workspace.evaluate(() => Boolean(document.fullscreenElement)));
    await workspace.locator('#fullscreen').click();
    await workspace.screenshot({ path:path.join(repo, 'artifacts', 'qa', 'extension-focus-page.png'), fullPage:true });
    await workspace.locator('[data-pane=rules]').click();
    await workspace.screenshot({ path:path.join(repo, 'artifacts', 'qa', 'extension-rules-page.png'), fullPage:true });
    assert.equal(await workspace.locator('[aria-current=page]').textContent(), '网站规则');
    await workspace.locator('#settings').click();
    assert.ok(await workspace.locator('#timerForm').isVisible());
    await workspace.setViewportSize({ width:600, height:780 });
    assert.ok(await workspace.evaluate(() => document.documentElement.scrollWidth <= innerWidth), '窄窗口不能横向溢出');
    await command({type:'start',fields:{timerMode:'CountUp',restMinutes:15}});
    const clockWorker = context.serviceWorkers().find(worker => worker.url().includes(extensionId));
    await clockWorker.evaluate(() => { const now = Date.now; Date.now = () => now() + 25 * 3600000; });
    const limited = await getView();
    assert.equal(limited.state.state, 'Ended'); assert.equal(limited.state.timer, '24:00:00'); assert.equal(limited.effective, false);
    assert.ok(await popup.locator('#limitNotice').isHidden(), '自动结束时保持当前页面，不主动弹窗');
    const reopened = await context.newPage(); await reopened.setViewportSize({width:400,height:600});
    await reopened.goto(`chrome-extension://${extensionId}/popup.html`);
    await reopened.locator('#limitNotice').waitFor({state:'visible'});
    await reopened.screenshot({path:path.join(repo,'artifacts','qa','extension-limit-notice.png'),animations:'disabled'});
    await reopened.locator('#limitNotice button').click();
    await waitUntil(async () => (await getView()).limitNotice === null);
    console.log('Standalone extension passed: no native host, white/black lists, resources, pause, disable, browser restart, countdown/rest, saved settings, themes and fullscreen.');
  } finally {
    if (context) await context.close();
    webServer.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
