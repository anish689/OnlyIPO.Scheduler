"""OAuth drive.file storage. No broad Drive scope, redirects or response logging."""
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
import hashlib
import json
import re
import time
from urllib import error, parse, request

from common import APP, PROJECT, BackupError, MAX_ARCHIVE_BYTES, retention_candidates

API = "https://www.googleapis.com/drive/v3/"
UPLOAD = "https://www.googleapis.com/upload/drive/v3/files"
TOKEN = "https://oauth2.googleapis.com/token"
FIELDS = "id,name,mimeType,parents,trashed,size,sha256Checksum,appProperties,ownedByMe"


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Drive:
    def __init__(self, client_id, client_secret, refresh_token, *, opener=None, sleep=time.sleep):
        self.opener = opener or request.build_opener(NoRedirect())
        self.sleep = sleep
        # This exchange is never logged, and a rejected/expired refresh token is
        # an actionable failure, not a reason to request a broader scope.
        payload = parse.urlencode(dict(client_id=client_id, client_secret=client_secret,
                                       refresh_token=refresh_token, grant_type="refresh_token")).encode()
        response = self._json(TOKEN, "POST", payload, {"Content-Type": "application/x-www-form-urlencoded"})
        self.token = response.get("access_token")
        scopes = response.get("scope", "").split()
        if not self.token or scopes != ["https://www.googleapis.com/auth/drive.file"]:
            raise BackupError("drive_token_requires_only_drive_file_scope")

    def _open(self, url, method="GET", data=None, headers=None, *, retry=False):
        # Endpoints are fixed or validated resumable URLs. Never follow redirects
        # with a bearer token, even on downloads or upload completion.
        parsed = parse.urlsplit(url)
        if parsed.scheme != "https" or parsed.netloc not in ("www.googleapis.com", "oauth2.googleapis.com"):
            raise BackupError("unexpected_google_endpoint")
        for attempt in range(3 if retry else 1):
            try:
                return self.opener.open(request.Request(url, data, headers or {}, method=method), timeout=60)
            except error.HTTPError as exc:
                status = exc.code
                retry_after = exc.headers.get("Retry-After")
                exc.close()
                if not retry or attempt == 2 or status not in (429, 500, 502, 503, 504):
                    raise BackupError(f"drive_http_{status}") from None
                delay = 2 ** (attempt + 1)
                if retry_after:
                    try:
                        delay = max(delay, int(retry_after) if retry_after.isdigit() else
                                    (parsedate_to_datetime(retry_after) - datetime.now(timezone.utc)).total_seconds())
                    except (ValueError, TypeError, OverflowError):
                        raise BackupError("drive_retry_after_invalid") from None
                if delay > 60:
                    raise BackupError("drive_retry_after_exceeds_run_budget")
                self.sleep(delay)
            except (OSError, error.URLError, TimeoutError):
                raise BackupError("drive_network_failure") from None

    def _json(self, url, method="GET", data=None, headers=None, *, retry=False):
        with self._open(url, method, data, headers, retry=retry) as response:
            try:
                raw = response.read(2 * 1024 * 1024 + 1)
                if len(raw) > 2 * 1024 * 1024:
                    raise BackupError("drive_metadata_too_large")
                result = json.loads(raw)
                if not isinstance(result, dict):
                    raise ValueError()
                return result
            except (ValueError, UnicodeError):
                raise BackupError("drive_invalid_metadata") from None

    def call(self, path, *, method="GET", value=None, query=None):
        headers = {"Authorization": f"Bearer {self.token}"}
        data = None
        if value is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(value).encode()
        return self._json(API + path + ("?" + parse.urlencode(query) if query else ""),
                          method, data, headers, retry=method == "GET")

    @staticmethod
    def file_id(value):
        if not re.fullmatch(r"[A-Za-z0-9_-]{10,200}", value or ""):
            raise BackupError("invalid_drive_file_id")
        return value

    def metadata(self, file_id):
        return self.call(f"files/{self.file_id(file_id)}", query={"fields": FIELDS})

    def assert_private(self, file_id):
        item = self.metadata(file_id)
        permissions = self.call(f"files/{self.file_id(file_id)}/permissions", query={
            "fields": "nextPageToken,permissions(id,type,role)", "pageSize": 100})
        entries = permissions.get("permissions", [])
        if (not item.get("ownedByMe") or item.get("trashed") or permissions.get("nextPageToken")
                or len(entries) != 1 or entries[0].get("role") != "owner" or entries[0].get("type") != "user"):
            raise BackupError("drive_item_not_private_owner_only")
        return item

    def assert_folder(self, folder_id):
        item = self.assert_private(folder_id)
        if item.get("mimeType") != "application/vnd.google-apps.folder" or item.get("appProperties", {}).get("app") != APP:
            raise BackupError("drive_folder_not_owned_by_backup_app")

    def upload_verified(self, path, folder_id, digest):
        self.assert_folder(folder_id)
        size = path.stat().st_size
        if not 0 < size <= MAX_ARCHIVE_BYTES:
            raise BackupError("encrypted_backup_size_out_of_bounds")
        generated = self.call("files/generateIds", query={"count": 1, "space": "drive"})
        file_id = self.file_id(generated["ids"][0])
        props = {"app": APP, "project": PROJECT, "state": "pending", "sha256": digest}
        metadata = {"id": file_id, "name": path.name, "mimeType": "application/octet-stream",
                    "parents": [folder_id], "appProperties": props}
        headers = {"Authorization": f"Bearer {self.token}", "Content-Type": "application/json",
                   "X-Upload-Content-Type": "application/octet-stream", "X-Upload-Content-Length": str(size)}
        with self._open(UPLOAD + "?uploadType=resumable", "POST", json.dumps(metadata).encode(), headers) as response:
            location = response.headers.get("Location", "")
        parsed = parse.urlsplit(location)
        if parsed.scheme != "https" or parsed.netloc != "www.googleapis.com" or parsed.path != "/upload/drive/v3/files":
            raise BackupError("drive_invalid_upload_location")
        # One bounded encrypted upload; do not repeat an ambiguous mutation.
        with path.open("rb") as stream:
            encrypted = stream.read(MAX_ARCHIVE_BYTES + 1)
        if len(encrypted) != size:
            raise BackupError("encrypted_backup_changed")
        self._json(location, "PUT", encrypted, {"Authorization": f"Bearer {self.token}",
                   "Content-Type": "application/octet-stream", "Content-Length": str(size)})
        item = self.assert_private(file_id)
        if (item.get("id") != file_id or item.get("parents") != [folder_id]
                or item.get("name") != path.name or int(item.get("size", -1)) != size
                or item.get("appProperties") != props
                or item.get("sha256Checksum", digest) != digest):
            raise BackupError("drive_uploaded_metadata_mismatch")
        checksum = hashlib.sha256()
        downloaded = 0
        with self._open(API + f"files/{file_id}?alt=media", headers={"Authorization": f"Bearer {self.token}"}, retry=True) as response:
            while chunk := response.read(1024 * 1024):
                downloaded += len(chunk)
                if downloaded > size:
                    raise BackupError("drive_download_size_mismatch")
                checksum.update(chunk)
        if downloaded != size or checksum.hexdigest() != digest:
            raise BackupError("drive_download_checksum_mismatch")
        self.assert_folder(folder_id)
        self.assert_private(file_id)
        props["state"] = "verified"
        self.call(f"files/{file_id}", method="PATCH", value={"appProperties": props})
        if self.metadata(file_id).get("appProperties") != props:
            raise BackupError("drive_verification_marker_not_saved")
        return file_id

    def retain(self, folder_id, current_id, now, *, apply=False):
        self.assert_folder(folder_id)
        files, page_token = [], None
        for _ in range(100):
            query = {"q": f"'{self.file_id(folder_id)}' in parents and trashed = false and appProperties has {{ key='app' and value='{APP}' }}",
                     "fields": f"nextPageToken,files({FIELDS})", "pageSize": 1000, "spaces": "drive"}
            if page_token:
                query["pageToken"] = page_token
            page = self.call("files", query=query)
            files.extend(page.get("files", []))
            page_token = page.get("nextPageToken")
            if not page_token:
                break
        else:
            raise BackupError("drive_retention_listing_exceeded")
        candidates = retention_candidates(files, folder_id, current_id, now)
        if apply:
            # Recheck eligibility and sharing immediately before every mutation.
            current = self.assert_private(current_id)
            if current != next(x for x in files if x["id"] == current_id):
                raise BackupError("drive_retention_metadata_changed")
            for file_id in candidates:
                candidate = self.assert_private(file_id)
                original = next(x for x in files if x["id"] == file_id)
                if candidate != original:
                    raise BackupError("drive_retention_metadata_changed")
                self.assert_folder(folder_id)
                self.call(f"files/{file_id}", method="PATCH", value={"trashed": True})
        return len(candidates)
