import io
import copy
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from urllib.error import HTTPError

from common import APP, PROJECT, BackupError, backup_name, retention_candidates, sha256_file
from database import Database, verify_restore
from drive_store import Drive, NoRedirect
from run import encrypt_package, run


NOW = datetime(2026, 11, 27, 19, 43, tzinfo=timezone.utc)
FOLDER = "private_folder_1234"


def item(day, suffix="", state="verified"):
    at = NOW - timedelta(days=day)
    return {"id": f"backup_file_{day}{suffix}", "name": backup_name(at, 10000 - day, 1), "parents": [FOLDER],
            "trashed": False, "ownedByMe": True, "appProperties": {"app": APP, "project": PROJECT, "state": state, "sha256": "a" * 64}}


class RetentionTests(unittest.TestCase):
    def test_keeps_seven_distinct_days_and_four_distinct_weeks(self):
        files = [item(day) for day in range(60)]
        candidates = retention_candidates(files, FOLDER, files[0]["id"], NOW)
        kept = [x for x in files if x["id"] not in candidates]
        self.assertTrue(all(item(day)["id"] not in candidates for day in range(7)))
        self.assertEqual(len(kept), 9)  # seven days overlap two of the four weeks
        self.assertNotIn(item(19)["id"], candidates)
        self.assertIn(item(59)["id"], candidates)

    def test_foreign_pending_manual_and_wrong_folder_never_selected(self):
        files = [item(i) for i in range(40)]
        foreign = item(50, "foreign")
        foreign["appProperties"]["app"] = "someone-else"
        pending = item(51, "pending", state="pending")
        manual = item(52, "manual")
        manual["name"] = "manual-backup.tar.age"
        elsewhere = item(53, "elsewhere")
        elsewhere["parents"] = ["other-folder"]
        candidates = retention_candidates(files + [foreign, pending, manual, elsewhere], FOLDER, files[0]["id"], NOW)
        for file in (foreign, pending, manual, elsewhere):
            self.assertNotIn(file["id"], candidates)

    def test_missing_latest_fails_closed(self):
        with self.assertRaisesRegex(BackupError, "current_verified"):
            retention_candidates([item(1)], FOLDER, item(0)["id"], NOW)

    def test_future_name_fails_closed(self):
        with self.assertRaisesRegex(BackupError, "future_retention"):
            retention_candidates([item(-1)], FOLDER, item(-1)["id"], NOW)

    def test_filename_utc_run_collision_prevention(self):
        self.assertEqual(backup_name(NOW, 123, 2), "youripo-production-2026-11-27T194300Z-r123-a2.tar.age")
        with self.assertRaises(BackupError):
            backup_name(NOW.replace(tzinfo=None), 1, 1)
        with self.assertRaises(BackupError):
            backup_name(NOW, "../secret", 1)


class Response(io.BytesIO):
    def __init__(self, value, headers=None):
        super().__init__(json.dumps(value).encode())
        self.headers = headers or {}


class FakeOpener:
    def __init__(self, results):
        self.results, self.requests = list(results), []

    def open(self, req, timeout):
        self.requests.append(req)
        result = self.results.pop(0)
        if isinstance(result, Exception):
            raise result
        return result


TOKEN = {"access_token": "fictional-only", "scope": "https://www.googleapis.com/auth/drive.file"}


class DriveTests(unittest.TestCase):
    def drive(self, results):
        opener = FakeOpener([Response(TOKEN), *results])
        delays = []
        return Drive("client", "secret", "refresh", opener=opener, sleep=delays.append), opener, delays

    def test_rejects_broad_oauth_scope(self):
        with self.assertRaisesRegex(BackupError, "only_drive_file"):
            Drive("id", "secret", "refresh", opener=FakeOpener([Response({**TOKEN, "scope": "https://www.googleapis.com/auth/drive"})]))

    def test_redirect_disabled(self):
        self.assertIsNone(NoRedirect().redirect_request(None, None, 302, "", {}, "https://evil.invalid"))
        drive, _, _ = self.drive([])
        with self.assertRaisesRegex(BackupError, "unexpected_google"):
            drive._open("https://evil.invalid/token")

    def test_read_retry_honors_retry_after(self):
        drive, opener, delays = self.drive([
            HTTPError("", 429, "private provider body", {"Retry-After": "8"}, io.BytesIO(b"secret")), Response({"id": "ok"})])
        self.assertEqual(drive.call("files"), {"id": "ok"})
        self.assertEqual(delays, [8])
        self.assertEqual(len(opener.requests), 3)

    def test_mutation_never_retried_or_provider_body_printed(self):
        drive, opener, delays = self.drive([HTTPError("", 503, "PRIVATE", {}, io.BytesIO(b"PRIVATE"))])
        with self.assertRaisesRegex(BackupError, "^drive_http_503$"):
            drive.call("files", method="POST", value={})
        self.assertEqual(len(opener.requests), 2)
        self.assertEqual(delays, [])

    def test_excessive_retry_after_stops(self):
        drive, _, delays = self.drive([HTTPError("", 429, "", {"Retry-After": "1000"}, io.BytesIO())])
        with self.assertRaisesRegex(BackupError, "exceeds_run_budget"):
            drive.call("files")
        self.assertEqual(delays, [])

    def test_shared_folder_rejected(self):
        drive, _, _ = self.drive([Response({"ownedByMe": True, "trashed": False}),
                                  Response({"permissions": [{"type": "user", "role": "owner"}, {"type": "anyone", "role": "reader"}]})])
        with self.assertRaisesRegex(BackupError, "not_private_owner_only"):
            drive.assert_private(FOLDER)

    def test_retention_dry_run_does_not_mutate(self):
        drive, _, _ = self.drive([])
        drive.assert_folder = lambda value: None
        files = [item(i) for i in range(50)]
        calls = []
        def call(path, **kwargs):
            calls.append((path, kwargs))
            return {"files": files}
        drive.call = call
        self.assertGreater(drive.retain(FOLDER, files[0]["id"], NOW), 0)
        self.assertEqual([x[0] for x in calls], ["files"])

    def test_download_corruption_never_marks_verified(self):
        drive, _, _ = self.drive([])
        drive.assert_folder = lambda value: None
        drive.assert_private = lambda value: {"id": value, "parents": [FOLDER], "name": "backup.age", "size": "3",
            "appProperties": {"app": APP, "project": PROJECT, "state": "pending", "sha256": "a"*64}}
        mutations = []
        drive.call = lambda path, **kw: mutations.append((path, kw)) or {"ids": ["generated_file_1234"]}
        drive._json = lambda *a, **kw: {}
        answers = iter([Response({}, {"Location": "https://www.googleapis.com/upload/drive/v3/files?upload_id=fictional"}), io.BytesIO(b"bad")])
        drive._open = lambda *a, **kw: next(answers)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "backup.age"
            path.write_bytes(b"abc")
            with self.assertRaisesRegex(BackupError, "download_checksum"):
                drive.upload_verified(path, FOLDER, "a"*64)
        self.assertFalse(any(kw.get("method") == "PATCH" for _, kw in mutations))

    def test_retention_applies_only_old_verified_owned_copies(self):
        drive, _, _ = self.drive([])
        drive.assert_folder = lambda value: None
        files = [item(i) for i in range(50)] + [item(80, "pending", state="pending")]
        trashed = []
        def call(path, **kwargs):
            if path == "files":
                return {"files": copy.deepcopy(files)}
            self.assertEqual(kwargs, {"method": "PATCH", "value": {"trashed": True}})
            trashed.append(path.removeprefix("files/"))
            return {}
        drive.call = call
        drive.assert_private = lambda value: copy.deepcopy(next(x for x in files if x["id"] == value))
        self.assertEqual(drive.retain(FOLDER, files[0]["id"], NOW, apply=True), 41)
        self.assertEqual(len(trashed), 41)
        for day in (0, 1, 2, 3, 4, 5, 6, 12, 19):
            self.assertNotIn(item(day)["id"], trashed)
        self.assertNotIn(files[-1]["id"], trashed)

    def test_retention_stops_if_a_candidate_changes_after_listing(self):
        drive, _, _ = self.drive([])
        drive.assert_folder = lambda value: None
        files = [item(i) for i in range(50)]
        mutations = []
        def call(path, **kwargs):
            if path == "files":
                return {"files": copy.deepcopy(files)}
            mutations.append(path)
            return {}
        def fresh(value):
            result = copy.deepcopy(next(x for x in files if x["id"] == value))
            if value != files[0]["id"]:
                result["appProperties"]["state"] = "pending"
            return result
        drive.call, drive.assert_private = call, fresh
        with self.assertRaisesRegex(BackupError, "metadata_changed"):
            drive.retain(FOLDER, files[0]["id"], NOW, apply=True)
        self.assertEqual(mutations, [])


class GateTests(unittest.TestCase):
    def test_manual_local_execution_cannot_touch_production(self):
        with self.assertRaisesRegex(BackupError, "gate_closed"):
            run()


@unittest.skipUnless(os.environ.get("BACKUP_TEST_PG_BIN"), "set BACKUP_TEST_PG_BIN for isolated PostgreSQL 17 rehearsal")
class PostgreSQLTests(unittest.TestCase):
    def test_reader_snapshot_restore_and_encryption(self):
        with tempfile.TemporaryDirectory(prefix="youripo-backup-test-", dir="/tmp") as directory:
            root = Path(directory)
            root.chmod(0o700)
            env = {k: os.environ[k] for k in ("PATH", "HOME", "LANG") if k in os.environ}
            env.update(PGHOST=str(root), PGPORT="5432", PGUSER="postgres", PGDATABASE="postgres", PGCONNECT_TIMEOUT="10")
            db = Database(os.environ["BACKUP_TEST_PG_BIN"], env)
            db.command("initdb", ["-D", str(root / "db"), "-U", "postgres", "--auth-local=trust", "--auth-host=reject", "--no-locale"], output=subprocess.DEVNULL)
            try:
                db.command("pg_ctl", ["-D", str(root/"db"), "-l", str(root/"server.log"), "-o", f"-c listen_addresses='' -k {root} -p 5432", "-w", "start"], output=subprocess.DEVNULL)
                db.sql('''CREATE ROLE youripo_api NOLOGIN;
                  CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY);
                  INSERT INTO public."__EFMigrationsHistory" VALUES ('fictional-migration-1');
                  CREATE TABLE public."Ipos" (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL);
                  INSERT INTO public."Ipos" (name) VALUES ('Fictional North'),('Fictional South');
                  ALTER TABLE public."Ipos" ENABLE ROW LEVEL SECURITY;
                  ALTER TABLE public."Ipos" FORCE ROW LEVEL SECURITY;
                  CREATE POLICY user_only ON public."Ipos" TO youripo_api USING (id=1);
                  ALTER TABLE public."__EFMigrationsHistory" ENABLE ROW LEVEL SECURITY;
                ''')
                db.sql((Path(__file__).parent / "production-backup-role.sql").read_text())
                db.sql("ALTER ROLE youripo_backup LOGIN;")
                reader = Database(db.bin, {**env, "PGUSER": "youripo_backup"})
                reader.audit_reader()
                with self.assertRaises(BackupError):
                    reader.sql('INSERT INTO public."Ipos" (name) VALUES (\'should fail\');')
                dump = root / "application.dump"
                with reader.snapshot() as snapshot:
                    manifest = reader.inventory(snapshot)
                    # A committed new row after snapshot creation must NOT enter
                    # either the archive or its matching manifest.
                    db.sql('INSERT INTO public."Ipos" (name) VALUES (\'Later fictional row\');')
                    reader.dump(dump, snapshot)
                self.assertEqual(next(x["rows"] for x in manifest["counts"] if x["name"] == "Ipos"), 2)
                self.assertTrue(verify_restore(reader, dump, manifest))
                key = root / "fictional.agekey"
                subprocess.run(["age-keygen", "-o", str(key)], check=True, capture_output=True)
                recipient = subprocess.run(["age-keygen", "-y", str(key)], check=True, capture_output=True, text=True).stdout.strip()
                encrypted = encrypt_package(root, manifest, dump, recipient, backup_name(NOW, 123, 1))
                decrypted = root / "decrypted.tar"
                subprocess.run(["age", "--decrypt", "-i", str(key), "-o", str(decrypted), str(encrypted)], check=True, capture_output=True)
                with tarfile.open(decrypted) as archive:
                    self.assertEqual(set(archive.getnames()), {"application.dump", "manifest.json"})
                    self.assertEqual(archive.extractfile("application.dump").read(), dump.read_bytes())
                db.sql('DROP POLICY youripo_backup_read ON public."Ipos";')
                with self.assertRaisesRegex(BackupError, "coverage_or_privilege"):
                    reader.audit_reader()
                db.sql((Path(__file__).parent / "production-backup-role.sql").read_text())
                db.sql('GRANT INSERT ON public."Ipos" TO youripo_backup;')
                with self.assertRaisesRegex(BackupError, "coverage_or_privilege"):
                    reader.audit_reader()
                dump.write_bytes(b"corrupt")
                with self.assertRaisesRegex(BackupError, "pg_restore_failed"):
                    verify_restore(reader, dump, manifest)
            finally:
                db.command("pg_ctl", ["-D", str(root/"db"), "-m", "immediate", "-w", "stop"], output=subprocess.DEVNULL)


if __name__ == "__main__":
    unittest.main()
