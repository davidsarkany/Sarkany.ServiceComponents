#!/usr/bin/env bash
# Interoperability check between the pre-rewrite baseline and the current build, using the sample services:
# for each pairing, Orders (one build) publishes OrderPlaced to RabbitMQ, Notifications (the other build) consumes it
# and queries Orders back over HTTP. Both directions must work for the rewrite to be a drop-in replacement.
# Needs Docker. The baseline is the original ServiceComponents 0.2.0-beta.12 from nuget.org.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
port="${INTEROP_RABBIT_PORT:-5679}"
broker="sc-interop-rabbit"
logs="artifacts/interop"
mkdir -p "$logs"

for target in Baseline Current; do
  dotnet build samples/Samples.sln -c Release -p:ScTarget=$target > "$logs/build-$target.log"
done

dll() {  # dll <project> <target>
  if [ "$2" = Baseline ]; then echo "samples/$1/bin/baseline/Release/net10.0/$1.dll"; else echo "samples/$1/bin/Release/net10.0/$1.dll"; fi
}

cleanup() {
  [ -n "${orders_pid:-}" ] && kill "$orders_pid" 2>/dev/null || true
  [ -n "${notifications_pid:-}" ] && kill "$notifications_pid" 2>/dev/null || true
  docker rm -f "$broker" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# Start the broker and wait for its startup log line. Do not poll with "docker exec rabbitmq-diagnostics" during boot:
# running it as root that early creates /var/lib/rabbitmq/.erlang.cookie with the wrong owner and the broker dies.
start_broker() {
  docker rm -f "$broker" >/dev/null 2>&1 || true
  docker run -d --name "$broker" -p "$port:5672" rabbitmq:4.1-management >/dev/null
  for _ in $(seq 1 90); do
    docker logs "$broker" 2>&1 | grep -q "Server startup complete" && { sleep 1; return 0; }
    sleep 1
  done
  echo "Broker did not start" >&2
  return 1
}
start_broker
amqp="amqp://guest:guest@localhost:$port"

wait_ready() {
  for _ in $(seq 1 60); do curl -sf "$1/.well-known/ready" >/dev/null && return 0; sleep 1; done
  echo "Service at $1 did not become ready" >&2; return 1
}

failed=0
for pairing in "Baseline Current" "Current Baseline" "Current Current" "Baseline Baseline"; do
  set -- $pairing
  orders_target=$1; notifications_target=$2
  name="orders-$orders_target-notifications-$notifications_target"
  echo "== Orders: $orders_target, Notifications: $notifications_target"

  dotnet "$(dll Orders.Service $orders_target)" --urls http://127.0.0.1:5101 \
    --Orders:Rabbit:Enabled=true --Orders:Rabbit:ConnectionString="$amqp" > "$logs/$name-orders.log" 2>&1 &
  orders_pid=$!
  dotnet "$(dll Notifications.Service $notifications_target)" --urls http://127.0.0.1:5201 \
    --Notifications:OrdersUri=http://127.0.0.1:5101/api/generic --Notifications:Rabbit:ConnectionString="$amqp" > "$logs/$name-notifications.log" 2>&1 &
  notifications_pid=$!
  wait_ready http://127.0.0.1:5101 && wait_ready http://127.0.0.1:5201

  order="o-$name"
  curl -sf -X POST http://127.0.0.1:5101/api/generic -H "domain-type: Orders.Api.PlaceOrder, Orders.Api" \
    -H "Content-Type: application/json" -d "{\"OrderId\":\"$order\",\"CustomerId\":\"c-1\",\"Product\":\"book\",\"Quantity\":2}" >/dev/null

  ok=0
  for _ in $(seq 1 20); do
    if curl -sf http://127.0.0.1:5201/notifications | grep -q "\"orderId\":\"$order\",\"message\":\"Customer c-1: 2 x book ordered\""; then ok=1; break; fi
    sleep 0.5
  done
  if [ $ok = 1 ]; then echo "   OK"; else echo "   FAILED (see $logs/$name-*.log)"; failed=1; fi

  kill "$orders_pid" "$notifications_pid" 2>/dev/null || true
  wait "$orders_pid" "$notifications_pid" 2>/dev/null || true
  orders_pid=""; notifications_pid=""
done

exit $failed
