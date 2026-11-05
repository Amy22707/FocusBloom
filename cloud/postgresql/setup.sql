-- FocusBloom PostgreSQL 同步服务
-- 在 CloudBase 当前环境的 SQL 编辑器中执行，可重复执行。
BEGIN;

CREATE TABLE IF NOT EXISTS public.focusbloom_sync_state (
    owner_id text PRIMARY KEY,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0)
);

CREATE TABLE IF NOT EXISTS public.focusbloom_sync_documents (
    owner_id text NOT NULL REFERENCES public.focusbloom_sync_state(owner_id),
    kind text NOT NULL CHECK (kind IN ('project', 'record', 'setting', 'workspace')),
    id text NOT NULL,
    version bigint NOT NULL CHECK (version > 0),
    payload jsonb NOT NULL,
    PRIMARY KEY (owner_id, kind, id)
);

ALTER TABLE public.focusbloom_sync_documents
    DROP CONSTRAINT IF EXISTS focusbloom_sync_documents_kind_check;
ALTER TABLE public.focusbloom_sync_documents
    ADD CONSTRAINT focusbloom_sync_documents_kind_check CHECK (kind IN ('project', 'record', 'setting', 'workspace'));

CREATE INDEX IF NOT EXISTS focusbloom_sync_owner_version
    ON public.focusbloom_sync_documents(owner_id, version);

ALTER TABLE public.focusbloom_sync_state ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.focusbloom_sync_documents ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.focusbloom_sync_state FROM PUBLIC, anon, authenticated;
REVOKE ALL ON public.focusbloom_sync_documents FROM PUBLIC, anon, authenticated;
GRANT ALL ON public.focusbloom_sync_state TO service_role;
GRANT ALL ON public.focusbloom_sync_documents TO service_role;

CREATE OR REPLACE FUNCTION public.focusbloom_sync_batch(
    p_cursor bigint,
    p_changes jsonb,
    p_workspace boolean
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $function$
DECLARE
    v_owner text := auth.uid();
    v_revision bigint;
    v_change jsonb;
    v_kind text;
    v_id text;
    v_payload jsonb;
    v_base bigint;
    v_current public.focusbloom_sync_documents%ROWTYPE;
    v_exists boolean;
    v_accepted jsonb := '[]'::jsonb;
    v_conflicts jsonb := '[]'::jsonb;
    v_documents jsonb;
    v_projects jsonb;
    v_cursor bigint;
    v_count integer;
    v_text text;
    v_minutes double precision;
    v_rule jsonb;
    v_interval jsonb;
    v_start timestamptz;
    v_end timestamptz;
    v_previous timestamptz;
    v_a timestamptz;
    v_b timestamptz;
    v_focus numeric;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated'
        OR v_owner IS NULL OR v_owner = '' OR v_owner = 'anon'
        OR COALESCE((auth.jwt()->>'is_anonymous')::boolean, false) THEN
        RAISE EXCEPTION '请使用邮箱账号登录后同步。' USING ERRCODE = '42501';
    END IF;
    IF p_cursor IS NULL OR p_cursor < 0
        OR jsonb_typeof(p_changes) IS DISTINCT FROM 'array'
        OR jsonb_array_length(p_changes) > 50 THEN
        RAISE EXCEPTION '同步请求格式错误。' USING ERRCODE = '22023';
    END IF;

    INSERT INTO public.focusbloom_sync_state(owner_id) VALUES (v_owner)
        ON CONFLICT (owner_id) DO NOTHING;
    SELECT revision INTO v_revision FROM public.focusbloom_sync_state
        WHERE owner_id = v_owner FOR UPDATE;

    FOR v_change IN
        SELECT value FROM jsonb_array_elements(p_changes) WITH ORDINALITY
        ORDER BY CASE value->>'kind' WHEN 'project' THEN 0 WHEN 'record' THEN 1 ELSE 2 END, ordinality
    LOOP
        v_kind := v_change->>'kind';
        v_id := v_change->>'id';
        v_payload := v_change->'payload';
        v_base := (v_change->>'baseVersion')::bigint;
        IF v_kind IS NULL OR v_kind NOT IN ('project', 'record', 'setting', 'workspace')
            OR (v_kind = 'workspace') <> p_workspace
            OR v_id IS NULL OR v_id = '' OR length(v_id) > 256
            OR v_base IS NULL OR v_base < 0 OR v_payload IS NULL THEN
            RAISE EXCEPTION '同步内容格式错误。' USING ERRCODE = '22023';
        END IF;

        IF v_kind = 'workspace' THEN
            IF v_id !~ '^(project_folders|todo_categories|todo_folders|todo_items|session_outcomes|session_todos|date_groups|date_events|calendar_plans|school_courses|course_changes|focus_goals|habits|habit_checkins|daily_reflections|settings)/.+'
                OR jsonb_typeof(v_payload) NOT IN ('object', 'null') THEN
                RAISE EXCEPTION '工作区同步数据无效。' USING ERRCODE = '22023';
            END IF;
            IF split_part(v_id, '/', 1) = 'settings'
                AND v_id !~ '^settings/(school\.term|reminder\.待办\.[0-9a-f-]{36}|project\.plan\.[0-9a-f-]{36})$' THEN
                RAISE EXCEPTION '这项设置不参与工作区同步。' USING ERRCODE = '22023';
            END IF;
            IF split_part(v_id, '/', 1) = 'date_events' AND jsonb_typeof(v_payload->'payload') = 'object' THEN
                v_payload := jsonb_set(v_payload, '{payload,coverPath}', 'null'::jsonb);
            ELSIF split_part(v_id, '/', 1) = 'daily_reflections' AND jsonb_typeof(v_payload->'payload') = 'object' THEN
                v_payload := jsonb_set(v_payload, '{payload,photos}', 'null'::jsonb);
            END IF;
        ELSIF v_kind = 'project' THEN
            IF v_payload->>'id' IS DISTINCT FROM v_id
                OR jsonb_typeof(v_payload->'name') IS DISTINCT FROM 'string'
                OR jsonb_typeof(v_payload->'category') IS DISTINCT FROM 'string'
                OR jsonb_typeof(v_payload->'color') IS DISTINCT FROM 'string'
                OR (v_payload ? 'deleted' AND jsonb_typeof(v_payload->'deleted') IS DISTINCT FROM 'boolean') THEN
                RAISE EXCEPTION '项目数据无效。' USING ERRCODE = '22023';
            END IF;
            v_payload := jsonb_build_object('id', v_id, 'name', v_payload->'name',
                'category', v_payload->'category', 'color', v_payload->'color',
                'deleted', COALESCE(v_payload->'deleted', 'false'::jsonb));
        ELSIF v_kind = 'setting' THEN
            IF v_id NOT IN ('timer.mode', 'timer.durationMinutes', 'pauseMinutes', 'restMinutes',
                'restrictions.websites', 'restrictions.websites.blacklist', 'restrictions.websiteMode')
                OR jsonb_typeof(v_payload) IS DISTINCT FROM 'string' THEN
                RAISE EXCEPTION '不支持同步这项设置。' USING ERRCODE = '22023';
            END IF;
            v_text := v_payload #>> '{}';
            IF v_id = 'timer.mode' AND v_text NOT IN ('CountUp', 'Countdown', 'Pomodoro') THEN
                RAISE EXCEPTION '计时方式无效。' USING ERRCODE = '22023';
            ELSIF v_id = 'restrictions.websiteMode' AND v_text NOT IN ('Whitelist', 'Blacklist') THEN
                RAISE EXCEPTION '网站限制方式无效。' USING ERRCODE = '22023';
            ELSIF v_id IN ('timer.durationMinutes', 'pauseMinutes', 'restMinutes') THEN
                v_minutes := v_text::double precision;
                IF v_minutes < 0 OR v_minutes > 1440
                    OR (v_id = 'timer.durationMinutes' AND v_minutes * 60000 < 10000)
                    OR (v_id = 'pauseMinutes' AND v_minutes <= 0) THEN
                    RAISE EXCEPTION '计时偏好无效：专注至少10秒，各项最多1440分钟。' USING ERRCODE = '22023';
                END IF;
            ELSIF v_id IN ('restrictions.websites', 'restrictions.websites.blacklist') THEN
                IF jsonb_typeof(v_text::jsonb) IS DISTINCT FROM 'array' THEN
                    RAISE EXCEPTION '网站名单格式无效。' USING ERRCODE = '22023';
                END IF;
                FOR v_rule IN SELECT value FROM jsonb_array_elements(v_text::jsonb) LOOP
                    IF jsonb_typeof(v_rule->'Domain') IS DISTINCT FROM 'string'
                        OR jsonb_typeof(v_rule->'IncludeSubdomains') IS DISTINCT FROM 'boolean' THEN
                        RAISE EXCEPTION '网站名单格式无效。' USING ERRCODE = '22023';
                    END IF;
                END LOOP;
            END IF;
        ELSE
            IF v_payload->>'id' IS DISTINCT FROM v_id
                OR jsonb_typeof(v_payload->'projectId') IS DISTINCT FROM 'string'
                OR COALESCE(v_payload->>'mode', '') NOT IN ('CountUp', 'Countdown', 'Pomodoro')
                OR COALESCE(v_payload->>'endReason', '') NOT IN ('Manual', 'CountdownElapsed', 'ApplicationExit', 'Interrupted', 'DurationLimit')
                OR jsonb_typeof(v_payload->'intervals') IS DISTINCT FROM 'array'
                OR jsonb_typeof(v_payload->'startedAt') IS DISTINCT FROM 'string'
                OR jsonb_typeof(v_payload->'endedAt') IS DISTINCT FROM 'string' THEN
                RAISE EXCEPTION '专注记录格式无效。' USING ERRCODE = '22023';
            END IF;
            v_start := (v_payload->>'startedAt')::timestamptz;
            v_end := (v_payload->>'endedAt')::timestamptz;
            v_previous := v_start;
            v_focus := 0;
            IF v_end < v_start THEN
                RAISE EXCEPTION '专注时间无效。' USING ERRCODE = '22023';
            END IF;
            FOR v_interval IN SELECT value FROM jsonb_array_elements(v_payload->'intervals') LOOP
                v_a := (v_interval->>'startedAt')::timestamptz;
                v_b := (v_interval->>'endedAt')::timestamptz;
                IF COALESCE(v_interval->>'kind', '') NOT IN ('Focus', 'Pause')
                    OR v_a IS NULL OR v_b IS NULL OR v_a < v_previous OR v_b < v_a OR v_b > v_end THEN
                    RAISE EXCEPTION '专注区间无效。' USING ERRCODE = '22023';
                END IF;
                IF v_interval->>'kind' = 'Focus' THEN v_focus := v_focus + extract(epoch FROM (v_b - v_a)); END IF;
                v_previous := v_b;
            END LOOP;
            IF v_focus < 10 THEN
                RAISE EXCEPTION '专注有效时长不能少于10秒。' USING ERRCODE = '22023';
            END IF;
            v_payload := jsonb_build_object('id', v_id, 'projectId', v_payload->'projectId',
                'mode', v_payload->'mode', 'startedAt', v_payload->'startedAt', 'endedAt', v_payload->'endedAt',
                'endReason', v_payload->'endReason', 'intervals', v_payload->'intervals',
                'strict', COALESCE(v_payload->'strict', 'false'::jsonb),
                'todoId', NULL, 'interceptionCount', v_payload->'interceptionCount');
        END IF;

        SELECT * INTO v_current FROM public.focusbloom_sync_documents
            WHERE owner_id = v_owner AND kind = v_kind AND id = v_id;
        v_exists := FOUND;
        IF v_exists AND v_current.payload = v_payload THEN
            v_accepted := v_accepted || jsonb_build_array(jsonb_build_object('kind', v_kind, 'id', v_id, 'version', v_current.version));
        ELSIF v_exists AND (v_kind = 'record' OR v_current.version <> v_base) THEN
            v_conflicts := v_conflicts || jsonb_build_array(jsonb_build_object('kind', v_kind, 'id', v_id,
                'version', v_current.version, 'payload', v_current.payload));
        ELSE
            IF NOT v_exists AND v_base <> 0 THEN
                RAISE EXCEPTION '云端版本不存在，请重新同步。' USING ERRCODE = '22023';
            END IF;
            IF v_kind = 'record' AND NOT EXISTS (
                SELECT 1 FROM public.focusbloom_sync_documents
                WHERE owner_id = v_owner AND kind = 'project' AND id = v_payload->>'projectId'
            ) THEN
                RAISE EXCEPTION '专注记录缺少对应项目。' USING ERRCODE = '22023';
            END IF;
            v_revision := v_revision + 1;
            INSERT INTO public.focusbloom_sync_documents(owner_id, kind, id, version, payload)
                VALUES(v_owner, v_kind, v_id, v_revision, v_payload)
                ON CONFLICT (owner_id, kind, id) DO UPDATE SET version = EXCLUDED.version, payload = EXCLUDED.payload;
            v_accepted := v_accepted || jsonb_build_array(jsonb_build_object('kind', v_kind, 'id', v_id, 'version', v_revision));
        END IF;
    END LOOP;
    UPDATE public.focusbloom_sync_state SET revision = v_revision WHERE owner_id = v_owner;

    SELECT COALESCE(jsonb_agg(jsonb_build_object('kind', d.kind, 'id', d.id, 'version', d.version, 'payload', d.payload) ORDER BY d.version), '[]'::jsonb),
           COALESCE(max(d.version), p_cursor), count(*)
        INTO v_documents, v_cursor, v_count
        FROM (SELECT * FROM public.focusbloom_sync_documents
              WHERE owner_id = v_owner AND (kind = 'workspace') = p_workspace AND version > p_cursor ORDER BY version LIMIT 100) d;

    SELECT COALESCE(jsonb_agg(jsonb_build_object('kind', p.kind, 'id', p.id, 'version', p.version, 'payload', p.payload)), '[]'::jsonb)
        INTO v_projects
        FROM public.focusbloom_sync_documents p
        WHERE p.owner_id = v_owner AND p.kind = 'project'
          AND p.id IN (SELECT value->'payload'->>'projectId' FROM jsonb_array_elements(v_documents) WHERE value->>'kind' = 'record');

    IF p_workspace THEN
        SELECT COALESCE(jsonb_agg(jsonb_build_object('kind', d.kind, 'id', d.id, 'version', d.version, 'payload', d.payload)), '[]'::jsonb)
        INTO v_projects
        FROM public.focusbloom_sync_documents d
        WHERE d.owner_id = v_owner AND d.kind = 'workspace'
          AND d.id IN (
            SELECT CASE split_part(value->>'id', '/', 1)
                WHEN 'todo_items' THEN 'todo_folders/' || (value->'payload'->>'folder_id')
                WHEN 'habit_checkins' THEN 'habits/' || (value->'payload'->>'habit_id')
                WHEN 'session_todos' THEN 'todo_items/' || (value->'payload'->>'todo_id') END
            FROM jsonb_array_elements(v_documents || v_conflicts)
            UNION
            SELECT 'todo_folders/' || (t.payload->>'folder_id')
            FROM public.focusbloom_sync_documents t
            WHERE t.owner_id = v_owner AND t.kind = 'workspace'
              AND t.id IN (SELECT 'todo_items/' || (value->'payload'->>'todo_id')
                           FROM jsonb_array_elements(v_documents || v_conflicts))
          );
    END IF;

    RETURN jsonb_build_object('cursor', v_cursor, 'hasMore', v_count = 100,
        'accepted', v_accepted, 'conflicts', v_conflicts, 'documents', v_projects || v_documents);
END;
$function$;

REVOKE ALL ON FUNCTION public.focusbloom_sync_batch(bigint, jsonb, boolean) FROM PUBLIC, anon, authenticated;

CREATE OR REPLACE FUNCTION public.focusbloom_sync(p_cursor bigint DEFAULT 0, p_changes jsonb DEFAULT '[]'::jsonb)
RETURNS jsonb LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$ SELECT public.focusbloom_sync_batch(p_cursor, p_changes, false) $$;

CREATE OR REPLACE FUNCTION public.focusbloom_sync_workspace(p_cursor bigint DEFAULT 0, p_changes jsonb DEFAULT '[]'::jsonb)
RETURNS jsonb LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$ SELECT public.focusbloom_sync_batch(p_cursor, p_changes, true) $$;

REVOKE ALL ON FUNCTION public.focusbloom_sync_workspace(bigint, jsonb) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.focusbloom_sync_workspace(bigint, jsonb) TO authenticated;

REVOKE ALL ON FUNCTION public.focusbloom_sync(bigint, jsonb) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.focusbloom_sync(bigint, jsonb) TO authenticated;

NOTIFY pgrst, 'reload schema';
COMMIT;
