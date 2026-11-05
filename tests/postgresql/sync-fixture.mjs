import {readFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
const require=createRequire(import.meta.url);
const {PGlite}=require(process.env.FOCUSBLOOM_PGLITE||'@electric-sql/pglite');
const setup=await readFile(new URL('../../cloud/postgresql/setup.sql',import.meta.url),'utf8');
export async function database() {
  const db=new PGlite();
  await db.exec(`CREATE ROLE anon; CREATE ROLE authenticated; CREATE ROLE service_role;
    CREATE SCHEMA auth; GRANT USAGE ON SCHEMA auth TO PUBLIC;
    CREATE FUNCTION auth.uid() RETURNS text LANGUAGE sql STABLE AS $$ SELECT nullif(current_setting('request.jwt.claims',true),'')::jsonb->>'sub' $$;
    CREATE FUNCTION auth.role() RETURNS text LANGUAGE sql STABLE AS $$ SELECT nullif(current_setting('request.jwt.claims',true),'')::jsonb->>'role' $$;
    CREATE FUNCTION auth.jwt() RETURNS jsonb LANGUAGE sql STABLE AS $$ SELECT nullif(current_setting('request.jwt.claims',true),'')::jsonb $$;`);
  await db.exec(setup);return db;
}
export async function call(db,user,changes=[],cursor=0,role='authenticated',workspace=false) {
  return db.transaction(async tx=>{
    await tx.exec(`SET LOCAL ROLE ${role}`);
    await tx.query("SELECT set_config('request.jwt.claims',$1,true)",[JSON.stringify({sub:user,role,is_anonymous:role==='anon'})]);
    return (await tx.query(`SELECT public.focusbloom_sync${workspace?'_workspace':''}($1,$2::jsonb) AS result`,[cursor,JSON.stringify(changes)])).rows[0].result;
  });
}
