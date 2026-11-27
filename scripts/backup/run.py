#!/usr/bin/env python3
"""One-shot production export. A verified upload is the only success condition."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import tempfile

from common import APP, PROJECT, ROLE, BackupError, backup_name, sha256_file, utc_now
from database import Database, verify_restore
from drive_store import Drive


def required(name):
    value = os.environ.get(name, "")
    if not value:
        raise BackupError("missing_" + name.lower())
    return value


def encrypt_package(root, manifest, dump, recipient, name):
    if not re.fullmatch(r"age1[0-9a-z]{58}", recipient):
        raise BackupError("invalid_age_public_recipient")
    manifest_path = root / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    archive = root / "application.tar"
    with tarfile.open(archive, "w") as bundle:
        bundle.add(dump, arcname="application.dump", recursive=False)
        bundle.add(manifest_path, arcname="manifest.json", recursive=False)
    encrypted = root / name
    try:
        subprocess.run(["age", "--encrypt", "--recipient", recipient, "--output", str(encrypted), str(archive)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=120,
                       env={k: os.environ[k] for k in ("PATH", "HOME") if k in os.environ})
    except (OSError, subprocess.SubprocessError):
        raise BackupError("age_encryption_failed") from None
    return encrypted


def run():
    os.umask(0o077)
    if (os.environ.get("GITHUB_REPOSITORY") != "anish689/OnlyIPO.Scheduler"
            or os.environ.get("GITHUB_REF") != "refs/heads/main"
            or os.environ.get("GITHUB_EVENT_NAME") not in ("schedule", "workflow_dispatch")
            or os.environ.get("PRODUCTION_BACKUP_ENABLED") != "true"):
        raise BackupError("production_backup_gate_closed")
    recipient = required("BACKUP_AGE_RECIPIENT")
    folder_id = required("BACKUP_DRIVE_FOLDER_ID")
    binaries = Path(required("PG_BIN"))
    certificate = Path(required("BACKUP_CA_FILE"))
    if sha256_file(certificate) != "700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7":
        raise BackupError("supabase_ca_checksum_mismatch")
    name = backup_name(utc_now(), required("GITHUB_RUN_ID"), required("GITHUB_RUN_ATTEMPT"))
    drive = Drive(required("DRIVE_CLIENT_ID"), required("DRIVE_CLIENT_SECRET"), required("DRIVE_REFRESH_TOKEN"))
    drive.assert_folder(folder_id)
    with tempfile.TemporaryDirectory(prefix="youripo-production-backup-") as directory:
        root = Path(directory)
        root.chmod(0o700)
        password = required("BACKUP_DB_PASSWORD")
        if any(x in password for x in ("\r", "\n", "\0")):
            raise BackupError("invalid_backup_password_format")
        host, user = "aws-0-ap-south-1.pooler.supabase.com", f"{ROLE}.{PROJECT}"
        passfile = root / "pgpass"
        passfile.write_text(f"{host}:5432:postgres:{user}:{password.replace(chr(92), chr(92)*2).replace(':', chr(92)+':')}\n")
        passfile.chmod(0o600)
        # Neither subprocesses nor their children inherit Drive/OAuth credentials.
        environment = {k: os.environ[k] for k in ("PATH", "HOME", "LANG", "LC_ALL") if k in os.environ}
        environment.update(PGHOST=host, PGPORT="5432", PGDATABASE="postgres", PGUSER=user,
                           PGPASSFILE=str(passfile), PGSSLMODE="verify-full", PGSSLROOTCERT=str(certificate),
                           PGCONNECT_TIMEOUT="20", PGAPPNAME="youripo-production-backup",
                           PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=180000 -c lock_timeout=10000")
        db = Database(binaries, environment)
        db.audit_reader()
        dump = root / "application.dump"
        with db.snapshot() as snapshot:
            inventory = db.inventory(snapshot)
            db.dump(dump, snapshot)
        verify_restore(db, dump, inventory)
        manifest = {"formatVersion": 1, "app": APP, "project": PROJECT, "createdAt": utc_now().isoformat(),
                    "fileName": name, "sourceCommit": required("GITHUB_SHA"),
                    "scope": "public application schema, data and RLS; no Supabase-managed schemas or external files",
                    "dumpSha256": sha256_file(dump), "restoreVerified": True, **inventory}
        encrypted = encrypt_package(root, manifest, dump, recipient, name)
        # Remove plaintext before the first outbound transfer of backup content.
        dump.unlink()
        (root / "manifest.json").unlink()
        (root / "application.tar").unlink()
        passfile.unlink()
        file_id = drive.upload_verified(encrypted, folder_id, sha256_file(encrypted))
        candidates = drive.retain(folder_id, file_id, utc_now(), apply=os.environ.get("BACKUP_RETENTION_ENABLED") == "true")
        result = {"status": "verified", "fileName": name, "restore": "passed", "offsiteChecksum": "matched",
                  "retention": "applied" if os.environ.get("BACKUP_RETENTION_ENABLED") == "true" else "dry-run",
                  "retentionCandidates": candidates}
        print(json.dumps(result))
        summary = os.environ.get("GITHUB_STEP_SUMMARY")
        if summary:
            with open(summary, "a") as stream:
                stream.write(f"Production backup **verified**: `{name}`. Isolated restore and Drive download checksum passed. "
                             f"Retention: {result['retention']}; {candidates} eligible old copies.\n")


if __name__ == "__main__":
    try:
        run()
    except BackupError as exc:
        print(f"Backup failed: {exc}", file=sys.stderr)
        sys.exit(1)
    except Exception:
        # No tracebacks containing provider bodies, secrets or database metadata.
        print("Backup failed: unexpected_failure; inspect the safe run stages and private configuration.", file=sys.stderr)
        sys.exit(1)
