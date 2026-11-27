#!/usr/bin/env bash
# Standard Ubuntu 24.04 runner only. No credentials are available in this step.
set -euo pipefail
test "$(. /etc/os-release && echo "$VERSION_CODENAME")" = noble
key_file="$(mktemp)"
trap 'rm -f "$key_file"' EXIT
curl --fail --silent --show-error --proto '=https' --tlsv1.2 \
  --connect-timeout 15 --max-time 60 \
  https://www.postgresql.org/media/keys/ACCC4CF8.asc --output "$key_file"
fingerprint="$(gpg --show-keys --with-colons "$key_file" 2>/dev/null | awk -F: '$1=="fpr" {print $10; exit}')"
test "$fingerprint" = B97B0AFCAA1A47F044F244A07FCC7D46ACCC4CF8
sudo install -m 644 "$key_file" /usr/share/keyrings/youripo-pgdg.asc
echo 'deb [signed-by=/usr/share/keyrings/youripo-pgdg.asc] https://apt.postgresql.org/pub/repos/apt noble-pgdg main' \
  | sudo tee /etc/apt/sources.list.d/youripo-pgdg.list >/dev/null
sudo install -d /etc/postgresql-common
echo 'create_main_cluster = false' | sudo tee /etc/postgresql-common/createcluster.conf >/dev/null
sudo apt-get update -qq
sudo apt-get install -y -qq age postgresql-17 postgresql-client-17
/usr/lib/postgresql/17/bin/pg_dump --version
age --version
