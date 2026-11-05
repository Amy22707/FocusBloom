export class CloudError extends Error {
  constructor(code,message) {super(message);this.code=code;}
  get requiresSignIn() {return ['invalid_grant','unauthenticated','login_required','Unauthorized'].includes(this.code);}
}
export function cloudClient(options,request) {
  const base=`https://${options.environmentId}.api.tcloudbasegateway.com/`;
  async function send(path,body,token) {
    const headers={'Content-Type':'application/json'};if(token)headers.Authorization='Bearer '+token;
    const response=await (request||fetch)(base+path,{method:body===undefined?'GET':'POST',headers,body:JSON.stringify(body),signal:AbortSignal.timeout(30000)});
    const result=await response.json();
    if(result.error==='failed_precondition'&&/token hash not match|token expiry/i.test(result.error_description||''))throw new CloudError('invalid_grant','登录状态已失效，请重新登录；本机数据会保留。');
    if(!response.ok||result.error) throw new CloudError(result.error||result.code||(response.status===401?'Unauthorized':String(response.status)),result.code==='PGRST202'?'同步接口尚未部署，请先执行PostgreSQL初始化脚本。':result.error_description||result.message||result.error||`请求失败（${response.status}）`);
    return result;
  }
  function session(result,email) { return {userId:result.sub,email,accessToken:result.access_token,refreshToken:result.refresh_token,expiresAt:Date.now()+result.expires_in*1000}; }
  return {
    async sendCode(email) {const result=await send('auth/v1/verification',{email,target:'ANY'});return {email,id:result.verification_id,isUser:Boolean(result.is_user),sentAt:Date.now()};},
    async signIn(verification,code) {
      const result=await send('auth/v1/verification/verify',{verification_id:verification.id,verification_code:code});
      const login=await send(verification.isUser?'auth/v1/signin':'auth/v1/signup',verification.isUser?{verification_token:result.verification_token}:{email:verification.email,verification_token:result.verification_token});
      return session(login,verification.email);
    },
    async refresh(previous) {
      const result=await send('auth/v1/token',{grant_type:'refresh_token',refresh_token:previous.refreshToken});
      if(result.sub!==previous.userId) throw new Error('账号身份变化，请重新登录。');
      return session(result,previous.email);
    },
    async synchronize(session,payload) {
      const result=await send('v1/rdb/rest/rpc/'+encodeURIComponent(options.syncRpc||'focusbloom_sync'),{p_cursor:payload.cursor,p_changes:payload.changes},session.accessToken);
      if(!Array.isArray(result.documents)||!Array.isArray(result.accepted)||!Array.isArray(result.conflicts)) throw new Error('同步服务返回了无效响应。');
      return result;
    },
    async signInWithPassword(email,password) {return session(await send('auth/v1/signin',{username:email,password}),email);},
    async getProfile(session) {const profile=await send('auth/v1/user/me',undefined,session.accessToken);return {name:profile.name||'',picture:profile.picture||''};}
  };
}
