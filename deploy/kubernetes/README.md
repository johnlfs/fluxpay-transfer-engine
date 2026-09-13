# FluxPay Kubernetes Deployment

## English

This directory contains the local Kubernetes deployment model for FluxPay Transfer Engine.

The deployment separates application responsibilities instead of treating Kubernetes as a direct translation of Docker Compose:

- stateless API;
- background Worker;
- one-shot database Migrator Job;
- persistent PostgreSQL and RabbitMQ;
- ConfigMap and Secret separation;
- probes and resource limits;
- non-root workloads;
- reproducible kind bootstrap.

## Architecture

```text
Host 127.0.0.1:18080
        |
        v
kind port mapping
        |
        v
NodePort 30080
        |
        v
fluxpay-api Service
        |
   +----+----+
   |         |
   v         v
 API #1    API #2
   |         |
   +----+----+
        |
        v
   PostgreSQL
   StatefulSet
        |
       PVC

Worker Deployment
   |          |
   v          v
PostgreSQL  RabbitMQ
            StatefulSet
                 |
                PVC

Migrator Job
     |
     v
PostgreSQL
```

## Directory Structure

```text
deploy/kubernetes/
├── README.md
├── secret.example.yaml
├── kind/
│   └── cluster.yaml
└── base/
    ├── namespace.yaml
    ├── configmap.yaml
    ├── kustomization.yaml
    ├── api/
    │   ├── deployment.yaml
    │   └── service.yaml
    ├── migrator/
    │   └── job.yaml
    ├── postgres/
    │   ├── service.yaml
    │   └── statefulset.yaml
    ├── rabbitmq/
    │   ├── service.yaml
    │   └── statefulset.yaml
    └── worker/
        └── deployment.yaml
```

## Local Environment

The local laboratory uses kind, Kubernetes 1.37, kubectl, Docker and the Kustomize support included with kubectl.

The kind node maps host port `18080` to node port `30080`, so the API is available locally at `http://127.0.0.1:18080`.

Local application images:

```text
fluxpay-api:local
fluxpay-worker:local
fluxpay-migrator:local
```

The bootstrap builds these images with Docker Compose and loads them into kind.

## Secrets

Real credentials are not committed.

The bootstrap reads `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD` from `.env` and creates the Kubernetes Secret dynamically.

`deploy/kubernetes/secret.example.yaml` contains placeholders only.

Production environments should use an appropriate external secret-management solution.

## PostgreSQL

PostgreSQL runs as a single-replica StatefulSet for the local laboratory.

It uses a ClusterIP Service, PersistentVolumeClaim, startup/readiness/liveness probes and CPU/memory requests and limits.

Persistence was validated by writing data, deleting `postgres-0`, waiting for recreation and confirming that the data remained available.

Application schema migration is handled by the dedicated Migrator Job.

## RabbitMQ

RabbitMQ runs as a single-replica StatefulSet with a ClusterIP Service, PersistentVolumeClaim, probes and resource limits.

Persistence was validated by creating a durable queue, deleting `rabbitmq-0`, waiting for recreation and confirming that the queue remained available.

Observed FluxPay topology:

```text
Exchanges
fluxpay.events
fluxpay.retry
fluxpay.dead-letter

Queues
fluxpay.transfer-completed
fluxpay.transfer-completed.retry.5s
fluxpay.transfer-completed.retry.15s
fluxpay.transfer-completed.retry.45s
fluxpay.transfer-completed.dlq
```

## Migrator

`fluxpay-migrator` is a Kubernetes Job that runs as non-root and applies EF Core migrations before API and Worker deployment.

A fresh database applied all 7 migrations.

A second execution reported zero pending migrations, proving idempotent behavior.

## API

The API Deployment uses:

- 2 replicas;
- RollingUpdate;
- `maxUnavailable: 0`;
- `maxSurge: 1`;
- startup, readiness and liveness probes;
- resource requests and limits;
- non-root execution;
- dropped Linux capabilities;
- disabled ServiceAccount token automount.

Health endpoints:

```text
/health/live
/health/ready
```

Readiness also validates PostgreSQL connectivity.

## Worker

The Worker runs as one background-process replica.

It has no fake HTTP probe because the application does not expose a Worker health endpoint. Kubernetes still restarts the container when the main process exits.

The Worker handles Transactional Outbox polling, parallel publishing, RabbitMQ consumption, retries, dead-letter handling and Consumer Inbox persistence.

## End-to-End Validation

A real transfer was executed through the Kubernetes API.

Initial balances:

```text
source       1000.00
destination   100.00
```

Transfer amount:

```text
125.50
```

The first request returned `HTTP 201 Created` and `Completed`.

Repeating the identical request with the same `Idempotency-Key` returned `HTTP 200 OK` with the same TransferId.

Final balances:

```text
source        874.50
destination   225.50
```

Database state:

```text
accounts              2
transfers             1
idempotency records   1
outbox messages       1
inbox messages        1
```

The Outbox message was published, the RabbitMQ event was consumed and the Inbox record was processed.

RabbitMQ ended with zero ready and zero unacknowledged messages.

Validated path:

```text
HTTP API
   |
   v
PostgreSQL transaction
   |
   v
Transactional Outbox
   |
   v
Worker publisher
   |
   v
RabbitMQ
   |
   v
Worker consumer
   |
   v
Consumer Inbox
```

## Reproducible Bootstrap

Run:

```bash
./scripts/kubernetes-bootstrap-kind.sh
```

The script builds the images, creates or reuses the kind cluster, loads the images, creates Namespace/ConfigMap/Secret, deploys PostgreSQL and RabbitMQ, waits for them, executes the Migrator Job, deploys API and Worker, and validates health.

A complete from-scratch test was executed after deleting the kind cluster.

Fresh result:

```text
API          2/2
Worker       1/1
PostgreSQL   1/1
RabbitMQ     1/1
Migrator     Complete
Migrations   7
```

The new database contained zero business records while retaining the full migrated schema.

## Delete the Local Cluster

```bash
kind delete cluster --name fluxpay
```

This removes only the kind Kubernetes laboratory. It does not remove the separate Docker Compose environment or its volumes.

## Observability Scope

The base Kubernetes laboratory intentionally does not deploy OpenTelemetry Collector, Prometheus, Grafana or Tempo.

API and Worker retain their OpenTelemetry instrumentation, but OTLP exporters are registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is explicitly configured.

The Docker Compose environment provides the complete local observability stack. A Kubernetes environment that requires telemetry export should provide an external or in-cluster OTLP collector and configure the application workloads accordingly.

## Production Considerations

This setup is intentionally a local portfolio laboratory.

Production should reconsider managed or highly available PostgreSQL and RabbitMQ, external secret management, Ingress or Gateway API, TLS, a container registry such as GHCR, immutable image references, NetworkPolicies, PodDisruptionBudgets, autoscaling, multi-node scheduling, production observability, backups and disaster recovery.

---

# Implantação Kubernetes do FluxPay

## Português

Este diretório contém o modelo de implantação Kubernetes local do FluxPay Transfer Engine.

A implantação separa as responsabilidades da aplicação em vez de tratar Kubernetes como uma simples conversão do Docker Compose:

- API stateless;
- Worker de background;
- Migrator como Job de execução única;
- PostgreSQL e RabbitMQ persistentes;
- separação entre ConfigMap e Secret;
- probes e limites de recursos;
- workloads não-root;
- bootstrap reproduzível com kind.

## Arquitetura

```text
Host 127.0.0.1:18080
        |
        v
mapeamento do kind
        |
        v
NodePort 30080
        |
        v
Service fluxpay-api
        |
   +----+----+
   |         |
   v         v
 API #1    API #2
   |         |
   +----+----+
        |
        v
   PostgreSQL
   StatefulSet
        |
       PVC

Worker Deployment
   |          |
   v          v
PostgreSQL  RabbitMQ
            StatefulSet
                 |
                PVC

Migrator Job
     |
     v
PostgreSQL
```

## Ambiente Local

O laboratório usa kind, Kubernetes 1.37, kubectl, Docker e o suporte a Kustomize incluído no kubectl.

O node kind mapeia a porta `18080` do host para a porta `30080` do node. A API fica disponível em `http://127.0.0.1:18080`.

Imagens locais:

```text
fluxpay-api:local
fluxpay-worker:local
fluxpay-migrator:local
```

O bootstrap constrói as imagens com Docker Compose e as carrega no kind.

## Segredos

Credenciais reais não são versionadas.

O bootstrap lê `POSTGRES_PASSWORD` e `RABBITMQ_PASSWORD` da `.env` e cria o Secret Kubernetes dinamicamente.

`deploy/kubernetes/secret.example.yaml` contém apenas placeholders.

Em produção, deve-se usar uma solução apropriada de gerenciamento externo de segredos.

## PostgreSQL

O PostgreSQL roda como StatefulSet de uma única réplica no laboratório local.

Ele utiliza Service ClusterIP, PersistentVolumeClaim, probes de startup/readiness/liveness e requests/limits de CPU e memória.

A persistência foi validada gravando dados, excluindo `postgres-0`, aguardando sua recriação e confirmando que os dados permaneceram disponíveis.

As migrations do schema pertencem ao Job dedicado do Migrator.

## RabbitMQ

O RabbitMQ roda como StatefulSet de uma única réplica com Service ClusterIP, PersistentVolumeClaim, probes e limites de recursos.

A persistência foi validada criando uma fila durável, excluindo `rabbitmq-0`, aguardando sua recriação e confirmando que a fila permaneceu disponível.

Topologia observada:

```text
Exchanges
fluxpay.events
fluxpay.retry
fluxpay.dead-letter

Filas
fluxpay.transfer-completed
fluxpay.transfer-completed.retry.5s
fluxpay.transfer-completed.retry.15s
fluxpay.transfer-completed.retry.45s
fluxpay.transfer-completed.dlq
```

## Migrator

`fluxpay-migrator` é um Kubernetes Job não-root que aplica as migrations EF Core antes da implantação de API e Worker.

Um banco novo aplicou as 7 migrations.

Uma segunda execução encontrou zero migrations pendentes, confirmando comportamento idempotente.

## API

O Deployment da API usa:

- 2 réplicas;
- RollingUpdate;
- `maxUnavailable: 0`;
- `maxSurge: 1`;
- startup, readiness e liveness probes;
- requests e limits;
- execução não-root;
- capabilities Linux removidas;
- automount do token da ServiceAccount desabilitado.

Endpoints:

```text
/health/live
/health/ready
```

O readiness também valida a conectividade com PostgreSQL.

## Worker

O Worker roda com uma réplica como processo de background.

Não há probe HTTP artificial porque a aplicação não expõe endpoint de health do Worker. O Kubernetes continua reiniciando o container quando o processo principal encerra.

O Worker executa polling do Transactional Outbox, publicação paralela, consumo RabbitMQ, retries, dead-letter handling e persistência do Consumer Inbox.

## Validação End-to-End

Foi executada uma transferência real pela API Kubernetes.

Saldos iniciais:

```text
origem       1000.00
destino       100.00
```

Valor:

```text
125.50
```

A primeira requisição retornou `HTTP 201 Created` e `Completed`.

A repetição da mesma requisição com a mesma `Idempotency-Key` retornou `HTTP 200 OK` com o mesmo TransferId.

Saldos finais:

```text
origem        874.50
destino       225.50
```

Estado do banco:

```text
accounts              2
transfers             1
idempotency records   1
outbox messages       1
inbox messages        1
```

A mensagem do Outbox foi publicada, o evento RabbitMQ foi consumido e o Inbox foi processado.

O RabbitMQ terminou sem mensagens prontas ou não confirmadas.

Fluxo validado:

```text
API HTTP
   |
   v
transação PostgreSQL
   |
   v
Transactional Outbox
   |
   v
Worker publisher
   |
   v
RabbitMQ
   |
   v
Worker consumer
   |
   v
Consumer Inbox
```

## Bootstrap Reproduzível

Execute:

```bash
./scripts/kubernetes-bootstrap-kind.sh
```

O script constrói as imagens, cria ou reutiliza o cluster kind, carrega as imagens, cria Namespace/ConfigMap/Secret, sobe PostgreSQL e RabbitMQ, aguarda a infraestrutura, executa o Migrator Job, sobe API e Worker e valida a saúde do ambiente.

Foi realizado um teste completo após excluir o cluster kind.

Resultado do cluster novo:

```text
API          2/2
Worker       1/1
PostgreSQL   1/1
RabbitMQ     1/1
Migrator     Complete
Migrations   7
```

O banco novo continha zero registros de negócio e o schema completo criado pelas migrations.

## Remover o Cluster Local

```bash
kind delete cluster --name fluxpay
```

Esse comando remove apenas o laboratório Kubernetes do kind. Ele não remove o ambiente Docker Compose separado nem seus volumes.

## Escopo de Observabilidade

O laboratório Kubernetes base deliberadamente não implanta OpenTelemetry Collector, Prometheus, Grafana ou Tempo.

A API e o Worker mantêm sua instrumentação OpenTelemetry, mas os exporters OTLP são registrados somente quando `OTEL_EXPORTER_OTLP_ENDPOINT` é configurado explicitamente.

O ambiente Docker Compose fornece a stack local completa de observabilidade. Um ambiente Kubernetes que necessite exportar telemetria deve fornecer um collector OTLP externo ou dentro do cluster e configurar os workloads da aplicação para utilizá-lo.

## Considerações para Produção

Este setup é intencionalmente um laboratório local de portfólio.

Em produção, deve-se reavaliar PostgreSQL e RabbitMQ altamente disponíveis ou gerenciados, secret management externo, Ingress ou Gateway API, TLS, registry como GHCR, referências imutáveis de imagens, NetworkPolicies, PodDisruptionBudgets, autoscaling, scheduling multi-node, observabilidade de produção, backups e disaster recovery.
