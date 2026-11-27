#!/usr/bin/env python3
"""One-time private OAuth handoff to the isolated GitHub environment.

Run locally after reviewing the client and main-only environment. This never
uploads a database, prints credentials or enables the production backup schedule.
"""
import argparse
import base64
import hashlib
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time
from urllib.parse import parse_qs, urlencode, urlsplit
import webbrowser

from common import APP, PROJECT, BackupError
from drive_store import Drive, TOKEN

REPO = "anish689/OnlyIPO.Scheduler"
ENVIRONMENT = "production-backup"


def gh(args, private_input=None):
    result = subprocess.run(["gh", *args], input=private_input, stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, timeout=60, check=False)
    if result.returncode:
        raise BackupError("github_private_setup_failed")
    return result.stdout


def check_environment():
    env = json.loads(gh(["api", f"repos/{REPO}/environments/{ENVIRONMENT}"]))
    policy = env.get("deployment_branch_policy") or {}
    rules = json.loads(gh(["api", f"repos/{REPO}/environments/{ENVIRONMENT}/deployment-branch-policies"]))
    branches = rules.get("branch_policies", [])
    if (not policy.get("custom_branch_policies") or policy.get("protected_branches")
            or rules.get("total_count") != 1 or len(branches) != 1
            or branches[0].get("name") != "main" or branches[0].get("type") != "branch"):
        raise BackupError("backup_environment_not_main_branch_only")


def authorize(args):
    os.umask(0o077)
    check_environment()
    settings = json.loads(Path(args.client_json).read_text()).get("installed", {})
    client_id, client_secret = settings.get("client_id"), settings.get("client_secret")
    if not client_id or not client_secret:
        raise BackupError("expected_private_desktop_oauth_client_json")
    verifier = secrets.token_urlsafe(64)
    state = secrets.token_urlsafe(32)
    result = {}

    class Callback(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def do_GET(self):
            parsed = urlsplit(self.path)
            query = parse_qs(parsed.query)
            if parsed.path != "/callback" or not secrets.compare_digest(query.get("state", [""])[0], state):
                self.send_error(400, "Invalid authorization response")
                return
            result["code"] = query.get("code", [""])[0]
            result["error"] = bool(query.get("error"))
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Referrer-Policy", "no-referrer")
            self.end_headers()
            self.wfile.write(b"<title>YourIPO backup authorization</title><p>Authorization received. You can close this tab. Setup will finish in the private helper.</p><script>history.replaceState(null,'','/callback');</script>")

    with HTTPServer(("127.0.0.1", 0), Callback) as server:
        server.timeout = 1
        redirect = f"http://127.0.0.1:{server.server_port}/callback"
        parameters = dict(client_id=client_id, redirect_uri=redirect, response_type="code",
                          scope="https://www.googleapis.com/auth/drive.file", access_type="offline",
                          prompt="consent", state=state, code_challenge_method="S256",
                          code_challenge=base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode())
        url = "https://accounts.google.com/o/oauth2/v2/auth?" + urlencode(parameters)
        if args.authorization_url_file:
            target = Path(args.authorization_url_file)
            # New file only: never overwrite a potentially sensitive file/symlink.
            with target.open("x") as stream:
                stream.write(url)
            target.chmod(0o600)
            print("Private authorization link saved; complete Google consent in the browser.", flush=True)
        else:
            webbrowser.open(url)
            print("Complete the narrow Google Drive consent in the opened browser.", flush=True)
        deadline = time.monotonic() + 600
        while not result and time.monotonic() < deadline:
            server.handle_request()
    if result.get("error") or not result.get("code"):
        raise BackupError("oauth_consent_incomplete_or_expired")
    # Use the same no-redirect HTTP implementation. No tokens enter browser URLs.
    exchange = Drive.__new__(Drive)
    from urllib import request
    from drive_store import NoRedirect
    exchange.opener = request.build_opener(NoRedirect())
    exchange.sleep = time.sleep
    token = exchange._json(TOKEN, "POST", urlencode(dict(client_id=client_id, client_secret=client_secret,
        code=result["code"], code_verifier=verifier, redirect_uri=redirect, grant_type="authorization_code")).encode(),
        {"Content-Type": "application/x-www-form-urlencoded"})
    refresh = token.get("refresh_token")
    if not refresh:
        raise BackupError("offline_refresh_token_missing")
    drive = Drive(client_id, client_secret, refresh)
    folder_id = args.folder_id
    if args.create_folder:
        folder_id = drive.call("files", method="POST", value={"name": "YourIPO production automated backups",
            "mimeType": "application/vnd.google-apps.folder", "appProperties": {"app": APP, "project": PROJECT}},
            query={"fields": "id"})["id"]
    drive.assert_folder(folder_id)
    for name, value in {"DRIVE_CLIENT_ID": client_id, "DRIVE_CLIENT_SECRET": client_secret, "DRIVE_REFRESH_TOKEN": refresh}.items():
        gh(["secret", "set", name, "--repo", REPO, "--env", ENVIRONMENT], value.encode())
    gh(["variable", "set", "BACKUP_DRIVE_FOLDER_ID", "--repo", REPO, "--env", ENVIRONMENT, "--body", folder_id])
    print("Drive connection saved privately to the main-only backup environment. Schedule remains unchanged.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client-json", required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--create-folder", action="store_true")
    group.add_argument("--folder-id")
    parser.add_argument("--authorization-url-file")
    args = parser.parse_args()
    try:
        authorize(args)
    except BackupError as exc:
        print(f"Private setup stopped: {exc}. Earlier successful writes may remain; no secrets were printed.", file=sys.stderr)
        sys.exit(1)
    except Exception:
        print("Private setup stopped. Check client input, GitHub access and Google consent privately. No secrets were printed.", file=sys.stderr)
        sys.exit(1)
