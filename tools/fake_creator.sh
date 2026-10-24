#!/usr/bin/env bash
# Plays the co-op Creator from the terminal, to test the Explorer without a second game client.
#   tools/fake_creator.sh room                    create a room (prints its id and code)
#   tools/fake_creator.sh stroke <room_id>        send a live stroke (a tent shape)
#   tools/fake_creator.sh build <room_id> [name]  BUILD a mock drawing: pending, then ready
#   tools/fake_creator.sh clear <room_id>         clear the drawing
#   tools/fake_creator.sh state                   show rooms, players and inventions
set -euo pipefail
cd "$(dirname "$0")/.."
DB=thinkink
quiet() { spacetime "$@" 2>&1 | grep -v "UNSTABLE" || true; }
# Present the shared sync key first (rooms are closed to devices without it).
[ -f thinkink.key ] && quiet call $DB authorize "\"$(cat thinkink.key)\"" >/dev/null

case "${1:-}" in
  room)
    quiet call $DB create_room '"level1"'
    quiet sql $DB "SELECT room_id, room_code, ink_remaining FROM room"
    ;;
  stroke)
    quiet call $DB add_stroke "$2" '"cli-1"' "$RANDOM" '[0.2,0.2,0.4,0.6,0.6,0.2]'
    ;;
  build)
    name="${3:-bridge}"
    png="Assets/Resources/MockDrawings/$name.png"
    [ -f "$png" ] || { echo "No $png (try bridge, sun, fish, cat, scribble)"; exit 1; }
    capture="cli-$(date +%s)"
    b64=$(base64 -i "$png" | tr -d '\n')
    json=$(printf '{"status":"ok","capture_id":"%s","name":"%s","message":"Sent from the terminal.","width_m":2,"ink_cost":10,"components":[{"type":"support","activation":"always"}]}' "$capture" "$name")
    json_arg=$(python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$json")
    quiet call $DB publish_invention "$2" "\"$capture\"" "\"$b64\"" '""' 0 0
    echo "pending: $capture"
    sleep 1
    quiet call $DB publish_invention "$2" "\"$capture\"" "\"$b64\"" "$json_arg" 0 0
    echo "ready: $capture"
    ;;
  clear)
    quiet call $DB clear_drawing "$2"
    ;;
  state)
    quiet sql $DB "SELECT room_id, room_code, ink_remaining FROM room"
    quiet sql $DB "SELECT room_id, role, online FROM player"
    quiet sql $DB "SELECT invention_id, room_id, capture_id, status, name, ink_cost FROM invention"
    ;;
  *)
    sed -n 2,8p "$0"
    ;;
esac
