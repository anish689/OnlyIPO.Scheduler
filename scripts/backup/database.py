"""Read-only export and isolated PostgreSQL restore. No live restore code exists."""
from contextlib import contextmanager
import json
import os
from pathlib import Path
import re
import select
import subprocess
import tempfile

from common import BackupError, ROLE


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


class Database:
    def __init__(self, binaries, environment):
        self.bin = Path(binaries)
        self.env = environment

    def command(self, tool, args=(), *, data=None, output=subprocess.PIPE, timeout=240):
        try:
            result = subprocess.run([str(self.bin / tool), *args], input=data,
                                    stdout=output, stderr=subprocess.DEVNULL,
                                    env=self.env, timeout=timeout, check=False)
        except (OSError, subprocess.TimeoutExpired):
            raise BackupError(f"{tool}_unavailable_or_timeout") from None
        if result.returncode:
            raise BackupError(f"{tool}_failed")
        return result.stdout

    def sql(self, query, snapshot=None):
        if snapshot is not None:
            if not re.fullmatch(r"[0-9A-Fa-f-]+", snapshot):
                raise BackupError("invalid_snapshot")
            query = f"BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SET TRANSACTION SNAPSHOT '{snapshot}';\n{query}\nCOMMIT;"
        return self.command("psql", ["-X", "-q", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1"], data=query.encode()).decode().strip()

    def json(self, query, snapshot=None):
        try:
            return json.loads(self.sql(query, snapshot))
        except (ValueError, UnicodeError):
            raise BackupError("invalid_database_metadata") from None

    @contextmanager
    def snapshot(self):
        process = subprocess.Popen([str(self.bin / "psql"), "-X", "-q", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1"],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.DEVNULL, env=self.env, text=True, bufsize=1)
        try:
            process.stdin.write("BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SELECT pg_export_snapshot();\n")
            process.stdin.flush()
            if not select.select([process.stdout], [], [], 30)[0]:
                raise BackupError("snapshot_start_timeout")
            value = process.stdout.readline().strip()
            if not re.fullmatch(r"[0-9A-Fa-f-]+", value):
                raise BackupError("snapshot_not_started")
            yield value
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
            process.stdin.close()
            process.stdout.close()

    def audit_reader(self):
        flags = self.json("""SELECT json_build_object('user',current_user,'database',current_database(),
            'version',current_setting('server_version_num')::int / 10000,
            'privileged',rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit,
            'memberships',EXISTS(SELECT FROM pg_auth_members WHERE member=r.oid),
            'schemaCreate',has_schema_privilege(current_user,'public','CREATE'))
            FROM pg_roles r WHERE rolname=current_user;""")
        if flags != {"user": ROLE, "database": "postgres", "version": 17,
                     "privileged": False, "memberships": False, "schemaCreate": False}:
            raise BackupError("unexpected_database_role_or_version")
        if self.sql("""SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='public' AND p.prosecdef AND has_function_privilege(current_user,p.oid,'EXECUTE');""") != "0":
            raise BackupError("backup_reader_can_execute_privileged_application_function")
        checks = self.json("""SELECT coalesce(json_agg(json_build_object('table',c.relname,
          'read',has_table_privilege(current_user,c.oid,'SELECT'),
          'write',has_table_privilege(current_user,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'),
          'allRows',NOT c.relrowsecurity OR EXISTS(SELECT FROM pg_policy p WHERE p.polrelid=c.oid
              AND p.polname='youripo_backup_read' AND p.polpermissive AND p.polcmd='r'
              AND p.polroles=ARRAY[(SELECT oid FROM pg_roles WHERE rolname=current_user)]
              AND pg_get_expr(p.polqual,p.polrelid)='true'),
          'restrictive',EXISTS(SELECT FROM pg_policy p WHERE p.polrelid=c.oid AND NOT p.polpermissive
              AND p.polcmd IN ('r','*') AND (0=ANY(p.polroles) OR (SELECT oid FROM pg_roles WHERE rolname=current_user)=ANY(p.polroles))))
          ORDER BY c.relname),'[]'::json) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
          WHERE n.nspname='public' AND c.relkind IN ('r','p');""")
        if not checks or any(not x["read"] or x["write"] or not x["allRows"] or x["restrictive"] for x in checks):
            raise BackupError("backup_reader_coverage_or_privilege_failure")

    def inventory(self, snapshot=None):
        tables = self.json("""SELECT coalesce(json_agg(json_build_object('name',c.relname,
          'rls',c.relrowsecurity,'forceRls',c.relforcerowsecurity,
          'columns',(SELECT json_agg(json_build_object('name',a.attname,'type',format_type(a.atttypid,a.atttypmod),
            'notNull',a.attnotnull,'identity',a.attidentity,'generated',a.attgenerated,
            'default',pg_get_expr(d.adbin,d.adrelid)) ORDER BY a.attnum)
            FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
            WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
          'constraints',(SELECT coalesce(json_agg(pg_get_constraintdef(k.oid) ORDER BY k.conname),'[]'::json)
            FROM pg_constraint k WHERE k.conrelid=c.oid),
          'indexes',(SELECT coalesce(json_agg(pg_get_indexdef(i.indexrelid) ORDER BY ic.relname),'[]'::json)
            FROM pg_index i JOIN pg_class ic ON ic.oid=i.indexrelid WHERE i.indrelid=c.oid),
          'policies',(SELECT coalesce(json_agg(json_build_object('name',p.polname,'command',p.polcmd,
            'permissive',p.polpermissive,'using',pg_get_expr(p.polqual,p.polrelid),
            'check',pg_get_expr(p.polwithcheck,p.polrelid),
            'roles',(SELECT json_agg(CASE WHEN x=0 THEN 'PUBLIC' ELSE pg_get_userbyid(x) END ORDER BY x=0,pg_get_userbyid(x)) FROM unnest(p.polroles) x)) ORDER BY p.polname),'[]'::json)
            FROM pg_policy p WHERE p.polrelid=c.oid)) ORDER BY c.relname),'[]'::json)
          FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
          WHERE n.nspname='public' AND c.relkind IN ('r','p');""", snapshot)
        if not tables:
            raise BackupError("empty_application_schema")
        counts = []
        for table in tables:
            # Count all tables in the same exported snapshot; no application values
            # leave the private manifest. PostgreSQL quotes every identifier.
            count = self.sql(f"SELECT count(*) FROM public.{identifier(table['name'])};", snapshot)
            if not count.isdigit():
                raise BackupError("invalid_table_count")
            counts.append({"name": table["name"], "rows": int(count)})
        migrations = self.json('SELECT coalesce(json_agg("MigrationId" ORDER BY "MigrationId"),\'[]\'::json) FROM public."__EFMigrationsHistory";', snapshot)
        roles = self.json("""SELECT coalesce(json_agg(json_build_object('name',rolname,'superuser',rolsuper,
          'createDb',rolcreatedb,'createRole',rolcreaterole,'login',rolcanlogin,'replication',rolreplication,
          'bypassRls',rolbypassrls,'inherit',rolinherit,'connectionLimit',rolconnlimit) ORDER BY rolname),'[]'::json)
          FROM pg_roles WHERE rolname NOT LIKE 'pg_%';""", snapshot)
        return {"tables": tables, "counts": counts, "migrations": migrations, "rolesWithoutPasswords": roles}

    def dump(self, destination, snapshot):
        self.command("pg_dump", ["-w", "--format=custom", "--schema=public", "--enable-row-security",
                                 f"--snapshot={snapshot}", f"--file={destination}", "--lock-wait-timeout=10000"], output=subprocess.DEVNULL)
        destination.chmod(0o600)


def verify_restore(source, dump, manifest):
    """Always a disposable socket-only cluster; never uses the source connection."""
    with tempfile.TemporaryDirectory(prefix="youripo-restore-", dir="/tmp") as directory:
        root = Path(directory)
        root.chmod(0o700)
        local_env = {k: os.environ[k] for k in ("PATH", "LANG", "LC_ALL", "HOME") if k in os.environ}
        local_env.update(PGHOST=str(root), PGPORT="5432", PGUSER="postgres", PGDATABASE="postgres", PGCONNECT_TIMEOUT="10")
        local = Database(source.bin, local_env)
        local.command("initdb", ["-D", str(root / "db"), "-U", "postgres", "--auth-local=trust", "--auth-host=reject", "--no-locale"], output=subprocess.DEVNULL)
        started = False
        try:
            local.command("pg_ctl", ["-D", str(root / "db"), "-l", str(root / "server.log"), "-o",
                                     f"-c listen_addresses='' -k {root} -p 5432", "-w", "start"], output=subprocess.DEVNULL)
            started = True
            for role in manifest["rolesWithoutPasswords"]:
                if role["name"] != "postgres":
                    # Role names are needed for ACL/policy restoration; no source
                    # password, privileged flag or login is enabled in rehearsal.
                    local.sql(f"CREATE ROLE {identifier(role['name'])} NOLOGIN;")
            local.sql("DROP SCHEMA public;")
            local.command("pg_restore", ["--exit-on-error", "--no-owner", "--dbname=postgres", str(dump)], output=subprocess.DEVNULL)
            restored = local.inventory()
            for key in ("tables", "counts", "migrations"):
                if restored[key] != manifest[key]:
                    raise BackupError(f"restore_{key}_mismatch")
            return True
        finally:
            if started:
                local.command("pg_ctl", ["-D", str(root / "db"), "-m", "immediate", "-w", "stop"], output=subprocess.DEVNULL)
