import {createInterface} from 'node:readline';
import {database, call} from './sync-fixture.mjs';

const db=await database();
try {
  for await (const line of createInterface({input:process.stdin})) {
    const {user, workspace, cursor, changes}=JSON.parse(line);
    console.log(JSON.stringify(await call(db,user,changes,cursor,'authenticated',workspace)));
  }
} finally {await db.close();}
