-- 只读检查：列出当前环境实际存在的数据库角色与身份函数。
SELECT rolname AS role_name
FROM pg_catalog.pg_roles
WHERE rolname NOT LIKE 'pg_%'
ORDER BY rolname;

SELECT n.nspname AS schema_name, p.proname AS function_name,
       pg_catalog.pg_get_function_result(p.oid) AS result_type
FROM pg_catalog.pg_proc p
JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'auth'
  AND p.proname IN ('uid', 'role', 'jwt')
ORDER BY p.proname;
