"""Small, dependency-free helpers for encrypted application backups."""
from datetime import datetime, timezone
import hashlib
import re


class BackupError(Exception):
    """A public-safe error code. Never use provider response bodies as messages."""


PROJECT = "vzbrqhpqawyenwxauztq"
ROLE = "youripo_backup"
APP = "youripo-production-backup-v1"
FILE_RE = re.compile(r"^youripo-production-(\d{4}-\d{2}-\d{2}T\d{6}Z)-r(\d+)-a(\d+)\.tar\.age$")
MAX_ARCHIVE_BYTES = 256 * 1024 * 1024


def utc_now():
    return datetime.now(timezone.utc)


def backup_name(at, run_id, attempt):
    if at.tzinfo is None or not re.fullmatch(r"[1-9][0-9]{0,19}", str(run_id)) or not re.fullmatch(r"[1-9][0-9]{0,4}", str(attempt)):
        raise BackupError("invalid_backup_identity")
    return f"youripo-production-{at.astimezone(timezone.utc):%Y-%m-%dT%H%M%SZ}-r{run_id}-a{attempt}.tar.age"


def sha256_file(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def retention_candidates(files, folder_id, current_id, now):
    """Only this application's verified files; never manual/foreign/pending files."""
    eligible = []
    for file in files:
        props = file.get("appProperties", {})
        match = FILE_RE.fullmatch(file.get("name", ""))
        if (not match or file.get("parents") != [folder_id] or file.get("trashed")
                or props.get("app") != APP or props.get("project") != PROJECT
                or props.get("state") != "verified"
                or not re.fullmatch(r"[0-9a-f]{64}", props.get("sha256", ""))):
            continue
        try:
            stamp = datetime.strptime(match[1], "%Y-%m-%dT%H%M%SZ").replace(tzinfo=timezone.utc)
        except ValueError:
            raise BackupError("invalid_retention_timestamp") from None
        if stamp > now:
            raise BackupError("future_retention_timestamp")
        eligible.append((stamp, int(match[2]), int(match[3]), file["id"]))
    eligible.sort(reverse=True)
    if not eligible or eligible[0][3] != current_id:
        raise BackupError("current_verified_backup_missing_or_not_latest")
    keep = {current_id}
    days, weeks = set(), set()
    for stamp, _, _, file_id in eligible:
        day = stamp.date()
        week = stamp.isocalendar()[:2]
        if day not in days and len(days) < 7:
            days.add(day)
            keep.add(file_id)
        if week not in weeks and len(weeks) < 4:
            weeks.add(week)
            keep.add(file_id)
    return [file_id for _, _, _, file_id in eligible if file_id not in keep]
