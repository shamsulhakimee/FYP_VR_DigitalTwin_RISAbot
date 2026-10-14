#!/usr/bin/env bash
#
# Build RisaBotWeb and install it on a robot, so a phone on the robot's AP can
# open http://<robot>:8000/ and get the app from a plain-HTTP origin.
#
#   ./deploy-to-robot.sh                 # build + push the bundle to 192.168.4.1
#   ./deploy-to-robot.sh 192.168.68.54   # ...to a robot on the LAN instead
#   ./deploy-to-robot.sh --portal        # also redeploy app.py (needed once, for the mount)
#   ./deploy-to-robot.sh --dns           # also install the risabot.internal name (needed once)
#   ./deploy-to-robot.sh --no-build      # push whatever is already in dist/
#
# Why this exists at all: an HTTPS page cannot fetch http:// or open ws://, and
# every robot URL is one of those. Vercel therefore can only ever host the demo.
# Serving the app from the robot puts the page on the same scheme as its API.
#
# The bundle lands in /etc/risabot/portal/webapp, a sibling of the portal's own
# WorkingDirectory. app.py mounts it at "/" if — and only if — that directory
# exists at import time, so the very first deploy needs --portal (or at least a
# portal restart) before anything is served.
#
set -euo pipefail

HOST=192.168.4.1
USER=sunrise
BUILD=1
PORTAL=0
DNS=0

for arg in "$@"; do
  case "$arg" in
    --portal)   PORTAL=1 ;;
    --dns)      DNS=1 ;;
    --no-build) BUILD=0 ;;
    -*)         echo "unknown flag: $arg" >&2; exit 2 ;;
    *)          HOST="$arg" ;;
  esac
done

cd "$(dirname "$0")"
SSH="ssh ${USER}@${HOST}"

# --- 1. build ---------------------------------------------------------------
# No VITE_DEMO here on purpose. The hosted build sets it in vercel.json; a robot
# build must talk to real hardware, and demo mode would silently answer every
# call from DemoClient instead.
if [ "$BUILD" = 1 ]; then
  echo "==> building"
  npm run build
fi
[ -f dist/index.html ] || { echo "dist/index.html missing — run without --no-build" >&2; exit 1; }

# --- 2. optionally redeploy the portal --------------------------------------
# app.py is NOT run from the repo: risabot-wifi-portal.service sets
# WorkingDirectory=/etc/risabot/portal/backend and uvicorn imports the copy
# there, so a git pull alone changes nothing. uvicorn dies on an import error
# and the portal is the only channel you would use to fix it — hence the .bak
# and the ast.parse before the restart.
if [ "$PORTAL" = 1 ]; then
  echo "==> deploying app.py"
  scp ../tools/wifi_provisioning/backend/app.py "${USER}@${HOST}:/tmp/app.py"
  $SSH "set -e
    D=/etc/risabot/portal/backend
    sudo -n cp \$D/app.py \$D/app.py.\$(date +%Y%m%d-%H%M%S).bak
    sudo -n cp /tmp/app.py \$D/app.py
    sudo -n rm -rf \$D/__pycache__
    sudo -n /opt/risabot-portal-venv/bin/python3 -c \"import ast,sys; ast.parse(open('\$D/app.py').read())\"
    echo '    syntax ok'"
fi

# --- 2b. optionally install the hostname -------------------------------------
# Gives AP clients http://risabot.internal:8000/ so the robot's address never has
# to be typed or read off a screen. dnsmasq is the DNS server for everything on
# the hotspot (dhcp-option=6 points at 192.168.4.1), so this needs no client-side
# setup at all. --test first: dnsmasq refuses to start on a bad config, and it
# owns DHCP for the hotspot, so a broken file locks clients off the AP entirely.
if [ "$DNS" = 1 ]; then
  echo "==> installing risabot.internal"
  scp ../tools/wifi_provisioning/dnsmasq.conf "${USER}@${HOST}:/tmp/dnsmasq.conf"
  $SSH "set -e
    sudo -n cp /etc/risabot/dnsmasq.conf /etc/risabot/dnsmasq.conf.\$(date +%Y%m%d-%H%M%S).bak
    sudo -n cp /tmp/dnsmasq.conf /etc/risabot/dnsmasq.conf
    sudo -n /usr/sbin/dnsmasq --test -C /etc/risabot/dnsmasq.conf
    sudo -n systemctl restart risabot-dnsmasq
    sleep 2
    systemctl is-active risabot-dnsmasq"
  # Query dnsmasq directly on its ap0 address. `getent hosts` would consult the
  # robot's own resolver, which is not dnsmasq, and the robot has no nslookup,
  # dig or host — so this needs the stdlib helper or it cannot be checked at all.
  # Both queries matter: the second proves forwarding still works, without which
  # hotspot clients resolve our name and nothing else.
  scp -q ../tools/wifi_provisioning/check_dns.py "${USER}@${HOST}:/tmp/check_dns.py"
  $SSH "python3 /tmp/check_dns.py risabot.internal 192.168.4.1 &&
        python3 /tmp/check_dns.py google.com 192.168.4.1"
fi

# --- 3. ship the bundle -----------------------------------------------------
echo "==> uploading bundle"
tar -czf - -C dist . | $SSH "cat > /tmp/risabotweb.tgz"

# Unpack beside the live directory and swap, so a half-extracted tree is never
# the thing being served. StaticFiles resolves paths per request, so the swap
# alone is enough — no restart needed once the mount is registered.
$SSH "set -e
  W=/etc/risabot/portal/webapp
  sudo -n rm -rf \$W.new && sudo -n mkdir -p \$W.new
  sudo -n tar -xzf /tmp/risabotweb.tgz -C \$W.new
  sudo -n chown -R root:root \$W.new
  sudo -n rm -rf \$W.old
  [ -d \$W ] && sudo -n mv \$W \$W.old || true
  sudo -n mv \$W.new \$W
  rm -f /tmp/risabotweb.tgz
  echo \"    installed \$(find \$W -type f | wc -l) files\""

# --- 4. restart the portal --------------------------------------------------
# Unconditional: on a first deploy the mount does not exist yet, and on later
# ones a restart is cheap and rules out a stale import.
echo "==> restarting portal"
$SSH "sudo -n systemctl restart risabot-wifi-portal && sleep 3 && systemctl is-active risabot-wifi-portal"

# --- 5. prove it ------------------------------------------------------------
# A 200 with text/html on / means the mount took. The API check guards against
# the mount at "/" having shadowed the routes it is registered after.
echo "==> verifying"
$SSH "curl -s -o /dev/null -w '    /            %{http_code} %{content_type}\n' http://localhost:8000/
      curl -s -o /dev/null -w '    /api/status  %{http_code}\n' http://localhost:8000/api/status"

echo
echo "Done. On a phone joined to the robot's AP, open:  http://risabot.internal:8000/"
echo "(that name needs --dns installed once; the address still works as a fallback)"
