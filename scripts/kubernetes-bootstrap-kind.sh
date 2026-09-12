#!/usr/bin/env bash

set -Eeuo pipefail

CLUSTER_NAME="fluxpay"
NAMESPACE="fluxpay"

SCRIPT_DIR="$(
  cd "$(dirname "${BASH_SOURCE[0]}")"
  pwd
)"

ROOT_DIR="$(
  cd "${SCRIPT_DIR}/.."
  pwd
)"

cd "$ROOT_DIR"

echo "========================================"
echo "FluxPay Kubernetes Bootstrap"
echo "========================================"

required_commands=(
  docker
  kind
  kubectl
  python3
  curl
)

for command_name in "${required_commands[@]}"
do
    if ! command -v "$command_name" >/dev/null 2>&1
    then
        echo "ERROR: required command not found: $command_name"
        exit 1
    fi
done

if [ ! -f ".env" ]
then
    echo "ERROR: .env was not found in:"
    echo "$ROOT_DIR"
    exit 1
fi

echo
echo "========================================"
echo "Building application images"
echo "========================================"

docker compose build \
  api \
  worker \
  migrator

echo
echo "========================================"
echo "Checking kind cluster"
echo "========================================"

if kind get clusters \
    | grep -Fxq "$CLUSTER_NAME"
then
    echo "Cluster already exists: $CLUSTER_NAME"
else
    kind create cluster \
      --name "$CLUSTER_NAME" \
      --config deploy/kubernetes/kind/cluster.yaml \
      --wait 120s
fi

kubectl config use-context \
  "kind-${CLUSTER_NAME}" \
  >/dev/null

echo
echo "========================================"
echo "Cluster"
echo "========================================"

kubectl get nodes \
  -o wide

echo
echo "========================================"
echo "Loading local images into kind"
echo "========================================"

kind load docker-image \
  fluxpay-api:local \
  fluxpay-worker:local \
  fluxpay-migrator:local \
  --name "$CLUSTER_NAME"

echo
echo "========================================"
echo "Namespace and configuration"
echo "========================================"

kubectl apply \
  -f deploy/kubernetes/base/namespace.yaml

kubectl apply \
  -f deploy/kubernetes/base/configmap.yaml

echo
echo "========================================"
echo "Creating Kubernetes Secret"
echo "========================================"

TEMP_SECRET_FILE="$(
  mktemp /tmp/fluxpay-k8s-secret.XXXXXX
)"

cleanup_secret_file()
{
    rm -f "$TEMP_SECRET_FILE"
}

trap cleanup_secret_file EXIT

python3 \
  - ".env" "$TEMP_SECRET_FILE" <<'PY'
from pathlib import Path
import sys

source = Path(sys.argv[1])
destination = Path(sys.argv[2])

required = {
    "POSTGRES_PASSWORD",
    "RABBITMQ_PASSWORD",
}

values = {}

for raw_line in source.read_text().splitlines():
    line = raw_line.strip()

    if not line or line.startswith("#") or "=" not in line:
        continue

    key, value = line.split("=", 1)

    if key not in required:
        continue

    value = value.strip()

    if (
        len(value) >= 2
        and value[0] == value[-1]
        and value[0] in {"'", '"'}
    ):
        value = value[1:-1]

    values[key] = value

missing = required - values.keys()

if missing:
    raise SystemExit(
        "Missing required .env keys: "
        + ", ".join(sorted(missing))
    )

destination.write_text(
    "\n".join(
        f"{key}={values[key]}"
        for key in sorted(required)
    )
    + "\n"
)

destination.chmod(0o600)
PY

kubectl create secret generic \
  fluxpay-secrets \
  --namespace "$NAMESPACE" \
  --from-env-file="$TEMP_SECRET_FILE" \
  --dry-run=client \
  -o yaml \
  | kubectl apply -f -

cleanup_secret_file

trap - EXIT

echo
echo "========================================"
echo "Deploying PostgreSQL"
echo "========================================"

kubectl apply \
  -f deploy/kubernetes/base/postgres/service.yaml

kubectl apply \
  -f deploy/kubernetes/base/postgres/statefulset.yaml

echo
echo "========================================"
echo "Deploying RabbitMQ"
echo "========================================"

kubectl apply \
  -f deploy/kubernetes/base/rabbitmq/service.yaml

kubectl apply \
  -f deploy/kubernetes/base/rabbitmq/statefulset.yaml

echo
echo "========================================"
echo "Waiting for PostgreSQL"
echo "========================================"

kubectl rollout status \
  statefulset/postgres \
  --namespace "$NAMESPACE" \
  --timeout=180s

echo
echo "========================================"
echo "Waiting for RabbitMQ"
echo "========================================"

kubectl rollout status \
  statefulset/rabbitmq \
  --namespace "$NAMESPACE" \
  --timeout=240s

echo
echo "========================================"
echo "Running database migrations"
echo "========================================"

kubectl delete job \
  fluxpay-migrator \
  --namespace "$NAMESPACE" \
  --ignore-not-found

kubectl apply \
  -f deploy/kubernetes/base/migrator/job.yaml

kubectl wait \
  --for=condition=Complete \
  job/fluxpay-migrator \
  --namespace "$NAMESPACE" \
  --timeout=180s

echo
echo "========================================"
echo "Migration log"
echo "========================================"

kubectl logs \
  job/fluxpay-migrator \
  --namespace "$NAMESPACE"

echo
echo "========================================"
echo "Deploying API"
echo "========================================"

kubectl apply \
  -f deploy/kubernetes/base/api/service.yaml

kubectl apply \
  -f deploy/kubernetes/base/api/deployment.yaml

echo
echo "========================================"
echo "Deploying Worker"
echo "========================================"

kubectl apply \
  -f deploy/kubernetes/base/worker/deployment.yaml

echo
echo "========================================"
echo "Waiting for API"
echo "========================================"

kubectl rollout status \
  deployment/fluxpay-api \
  --namespace "$NAMESPACE" \
  --timeout=180s

echo
echo "========================================"
echo "Waiting for Worker"
echo "========================================"

kubectl rollout status \
  deployment/fluxpay-worker \
  --namespace "$NAMESPACE" \
  --timeout=180s

echo
echo "========================================"
echo "API health"
echo "========================================"

curl \
  --fail \
  --silent \
  --show-error \
  http://127.0.0.1:18080/health/live

echo

curl \
  --fail \
  --silent \
  --show-error \
  http://127.0.0.1:18080/health/ready

echo

echo
echo "========================================"
echo "RabbitMQ"
echo "========================================"

kubectl exec \
  --namespace "$NAMESPACE" \
  statefulset/rabbitmq \
  -- \
  rabbitmq-diagnostics \
  -q \
  ping

echo
echo "========================================"
echo "FluxPay Kubernetes state"
echo "========================================"

kubectl get \
  deployments,pods,services,statefulsets,jobs,pvc \
  --namespace "$NAMESPACE" \
  -o wide

echo
echo "========================================"
echo "Bootstrap completed successfully"
echo "========================================"
