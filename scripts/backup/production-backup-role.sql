-- Run as the production database owner after reviewing the target project.
-- No passwords, owner credentials, default grants or application writes here.
BEGIN;
DO $guard$
BEGIN
  IF current_database() <> 'postgres' THEN RAISE EXCEPTION 'Unexpected database'; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'youripo_api')
     OR EXISTS (SELECT FROM pg_roles WHERE rolname = 'onlyipo_api') THEN
    RAISE EXCEPTION 'Expected production runtime roles';
  END IF;
  IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'youripo_backup') THEN
    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'youripo_backup'
               AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit))
       OR EXISTS (SELECT FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.member WHERE r.rolname='youripo_backup') THEN
      RAISE EXCEPTION 'Existing backup role requires manual review';
    END IF;
  ELSE
    CREATE ROLE youripo_backup NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT CONNECTION LIMIT 2;
  END IF;
END $guard$;
ALTER ROLE youripo_backup SET default_transaction_read_only = on;
ALTER ROLE youripo_backup SET statement_timeout = '180s';
ALTER ROLE youripo_backup SET lock_timeout = '10s';
GRANT CONNECT ON DATABASE postgres TO youripo_backup;
GRANT USAGE ON SCHEMA public TO youripo_backup;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM youripo_backup;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM youripo_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO youripo_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO youripo_backup;
DO $policies$
DECLARE item record;
BEGIN
  FOR item IN SELECT tablename FROM pg_tables WHERE schemaname='public' LOOP
    EXECUTE format('DROP POLICY IF EXISTS youripo_backup_read ON public.%I', item.tablename);
    EXECUTE format('CREATE POLICY youripo_backup_read ON public.%I AS PERMISSIVE FOR SELECT TO youripo_backup USING (true)', item.tablename);
  END LOOP;
END $policies$;
COMMIT;
-- Set a generated password through a private owner connection or masked role UI,
-- then enable LOGIN. Never put the password in SQL-editor history or this file.
-- Reapply after additive migrations: new tables get no automatic backup grant.
