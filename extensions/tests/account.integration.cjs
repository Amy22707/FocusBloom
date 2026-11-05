const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.FOCUSBLOOM_PLAYWRIGHT);
const repo=path.resolve(__dirname,'../..');
const root=path.join(repo,'.tools','account-extension-test',String(process.pid));
const extension=path.join(root,'extension');fs.mkdirSync(root,{recursive:true});fs.cpSync(path.join(repo,'extensions/browser'),extension,{recursive:true});
fs.writeFileSync(path.join(extension,'cloud-config.json'),JSON.stringify({environmentId:'test-env',publishableKey:'test-public',syncRpc:'focusbloom_sync'}));
(async()=>{
 const context=await chromium.launchPersistentContext(path.join(root,'profile'),{headless:true,executablePath:process.env.FOCUSBLOOM_BROWSER,ignoreDefaultArgs:['--disable-extensions'],args:['--enable-unsafe-extension-debugging']});
 try {
  const cdp=await context.browser().newBrowserCDPSession();const {id}=await cdp.send('Extensions.loadUnpacked',{path:extension});await cdp.detach();
  const page=await context.newPage();await page.setViewportSize({width:400,height:600});await page.goto(`chrome-extension://${id}/popup.html`);
  await page.waitForFunction(()=>document.querySelector('#palette').options.length===6);
  const worker=context.serviceWorkers().find(worker=>worker.url().includes(id));
  await worker.evaluate(()=>{
    const original=globalThis.fetch;let user='one';let count=0;
    globalThis.testOffline=false;
    globalThis.fetch=async(url,options)=>{
      if(!String(url).includes('test-env.api.tcloudbasegateway.com'))return original(url,options);
      const route=new URL(url).pathname,body=options.body?JSON.parse(options.body):{};
      if(globalThis.testAuthExpired&&route.includes('/rpc/'))return new Response(JSON.stringify({error:'invalid_grant',error_description:'login expired'}),{status:401,headers:{'Content-Type':'application/json'}});
      let result;
      if(route.endsWith('/verification')) {if(globalThis.testDelayVerification) await new Promise(resolve=>globalThis.testReleaseVerification=resolve);user=body.email.startsWith('two')?'two':'one';result={verification_id:'verification',is_user:true};}
      else if(route.endsWith('/verify'))result={verification_token:'verified'};
      else if(route.endsWith('/signin')) {if(body.username)user=body.username.startsWith('two')?'two':'one';result={sub:user,access_token:'access-'+user,refresh_token:'refresh-'+user,expires_in:7200};}
      else if(route.endsWith('/user/me'))result={name:'专注者'+user,picture:''};
      else {
        if(globalThis.testOffline)throw new Error('模拟离线');
        result={cursor:100,hasMore:false,documents:globalThis.testPomodoro?[{kind:'setting',id:'timer.mode',version:10000,payload:'Pomodoro'}]:[],conflicts:[],accepted:body.p_changes.map(change=>({kind:change.kind,id:change.id,version:++count}))};
      }
      return new Response(JSON.stringify(result),{status:200,headers:{'Content-Type':'application/json'}});
    };
  });
  const command=payload=>page.evaluate(payload=>chrome.runtime.sendMessage(payload),payload);
  await command({type:'start',fields:{project:'访客阅读'}});
  await worker.evaluate(()=>{const now=Date.now;Date.now=()=>now()+20000;});await command({type:'end'});
  await page.locator('[data-pane=history]').click();await page.locator('.historyCard').waitFor();assert.match(await page.locator('#historySummary').textContent(),/1 次专注/);
  await page.screenshot({path:path.join(repo,'artifacts/qa/extension-history.png'),animations:'disabled'});
  await page.locator('[data-pane=preferences]').click();await page.locator('#accountLogin').evaluate(e=>e.open=true);
  await page.locator('#accountEmailInput').fill('one@example.com');await page.locator('#sendCode').click();
  await page.waitForFunction(()=>document.querySelector('#syncStatus').textContent.includes('验证码已发送'));
  await page.locator('#accountCode').fill('123456');await page.locator('#mergeGuest').check();await page.locator('#accountForm button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#accountEmail').textContent==='one@example.com'&&document.querySelector('#syncStatus').textContent.includes('同步完成'));
  assert.equal((await command({type:'getHistory'})).total,1);
  assert.equal(await page.locator('#focusProject option').count(),2);
  await page.waitForFunction(()=>document.querySelector('#accountName').textContent==='专注者one');assert.equal(await page.locator('#accountLogin').isVisible(),false);
  await page.screenshot({path:path.join(repo,'artifacts/qa/extension-account.png'),fullPage:true,animations:'disabled'});
  await worker.evaluate(()=>globalThis.testOffline=true);await command({type:'syncNow'});assert.match((await command({type:'getView'})).account.status,/离线/);
  await worker.evaluate(()=>globalThis.testOffline=false);await command({type:'syncNow'});assert.match((await command({type:'getView'})).account.status,/同步完成/);
  await command({type:'start',fields:{project:'保留账号内正在进行的计时'}});await worker.evaluate(()=>globalThis.testAuthExpired=true);await command({type:'syncNow'});
  let expired=await command({type:'getView'});assert.equal(expired.account.signedIn,false);assert.equal(expired.account.needsLogin,true);assert.equal(expired.state.state,'Focusing');assert.equal((await command({type:'getHistory'})).total,1);
  const storedExpiry=await worker.evaluate(async()=>(await chrome.storage.local.get('focusAccount')).focusAccount);assert.equal(storedExpiry.userId,'one');assert.equal(storedExpiry.accessToken,'');assert.equal(storedExpiry.refreshToken,'');
  await page.reload();await page.waitForFunction(()=>!document.querySelector('#loginExpiredBanner').hidden);assert.equal(await page.locator('#accountName').textContent(),'未登录');
  await page.locator('#relogin').click();assert.equal(await page.locator('#accountLogin').isVisible(),true);
  await page.screenshot({path:path.join(repo,'artifacts/qa/extension-login-expired.png'),fullPage:true,animations:'disabled'});
  assert.match((await command({type:'passwordSignIn',fields:{email:'one@example.com',password:'Example9!'}})).error,/先结束/);
  await worker.evaluate(()=>{globalThis.testAuthExpired=false;const now=Date.now;Date.now=()=>now()+20000;});await command({type:'end'});await command({type:'passwordSignIn',fields:{email:'one@example.com',password:'Example9!'}});await command({type:'syncNow'});
  assert.equal((await command({type:'getView'})).account.signedIn,true);assert.equal((await command({type:'getHistory'})).total,2);
  await worker.evaluate(()=>globalThis.testPomodoro=true);await command({type:'syncNow'});await worker.evaluate(()=>globalThis.testPomodoro=false);
  await command({type:'signOut'});assert.equal((await command({type:'getHistory'})).total,1);
  await page.locator('#accountLogin').evaluate(e=>e.open=true);await page.locator('#loginMethod').selectOption('password');await page.locator('#accountEmailInput').fill('one@example.com');await page.locator('#accountPassword').fill('Example9!');await page.locator('#accountForm button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#accountEmail').textContent==='one@example.com'&&document.querySelector('#syncStatus').textContent.includes('同步完成'));
  assert.equal(await page.locator('#accountPassword').inputValue(),'');assert.equal(await worker.evaluate(async()=>(await chrome.storage.local.get('focusProfile:one'))['focusProfile:one'].sharedSettings['timer.mode']),'Pomodoro');
  await command({type:'signOut'});
  await worker.evaluate(()=>{const now=Date.now;Date.now=()=>now()+60001;});
  await command({type:'sendCode',fields:{email:'two@example.com'}});await command({type:'signIn',fields:{email:'two@example.com',code:'123456',mergeGuest:false}});
  assert.equal((await command({type:'getHistory'})).total,0);assert.equal((await command({type:'getView'})).account.email,'two@example.com');
  await command({type:'signOut'});assert.equal((await command({type:'getHistory'})).total,1);
  const view=await command({type:'getView'});assert.ok(!JSON.stringify(view).includes('refresh-one'));assert.ok(!JSON.stringify(view).includes('access-one'));
  await command({type:'start',fields:{timerMode:'Countdown',durationMinutes:1/6,project:'验证期间继续计时'}});
  await worker.evaluate(()=>{globalThis.testDelayVerification=true;});const pendingCode=command({type:'sendCode',fields:{email:'one@example.com'}});
  await page.waitForFunction(async()=>(await chrome.runtime.sendMessage({type:'getView'})).account.busy);
  await worker.evaluate(()=>{const now=Date.now;Date.now=()=>now()+20000;});const ended=await command({type:'getView'});assert.equal(ended.state.state,'Ended');assert.equal(ended.effective,false);
  await worker.evaluate(()=>{globalThis.testDelayVerification=false;globalThis.testReleaseVerification();});await pendingCode;
  console.log('Account integration passed: OTP and password login, profile display, hidden signed-in login form, preserved Pomodoro preferences, history, offline retry, separate users, and timer deadlines during delayed authentication.');
 } finally {await context.close();}
})().catch(error=>{console.error(error);process.exitCode=1});
