# FluxPay Transfer Engine

High-integrity money transfer engine built with .NET 10, PostgreSQL and RabbitMQ.

FluxPay is a backend engineering portfolio project focused on transactional consistency, idempotency, concurrent money movement, reliable event delivery, observability, distributed tracing, performance analysis and containerized deployment.

The project intentionally goes beyond CRUD scenarios and explores engineering problems commonly found in financial and distributed systems.

---

# English

## Overview

FluxPay implements account-to-account transfers with strong consistency guarantees.

A transfer must:

- never create money;
- never lose money;
- never allow a negative source balance;
- remain atomic;
- support idempotent retries;
- behave correctly under concurrent requests;
- produce reliable integration events;
- remain observable across asynchronous boundaries.

The system combines a synchronous transactional command path with an asynchronous event pipeline based on Transactional Outbox and Transactional Inbox patterns.

---

## Main Technologies

- .NET 10
- ASP.NET Core
- C#
- Entity Framework Core 10
- PostgreSQL 18
- RabbitMQ 4
- Docker
- Docker Compose
- Kubernetes
- kind
- Kustomize
- OpenTelemetry
- Prometheus
- Grafana
- Grafana Tempo
- k6
- xUnit
- Testcontainers
- GitHub Actions

---

## Architecture

The repository follows a layered architecture.

    FluxPay.Api
        |
        v
    FluxPay.Application
        |
        v
    FluxPay.Domain

    FluxPay.Infrastructure
        |
        +--> PostgreSQL
        +--> RabbitMQ
        +--> Transactional Outbox
        +--> Transactional Inbox

    FluxPay.Worker
        |
        +--> Outbox Publisher
        +--> RabbitMQ Consumer
        +--> Retry / Dead Letter handling

    FluxPay.Migrator
        |
        +--> EF Core database migrations

The architecture tests enforce dependency boundaries between the projects.

---

## Transfer Flow

A successful transfer follows this path:

    HTTP Request
        |
        v
    Idempotency validation
        |
        v
    Begin PostgreSQL transaction
        |
        v
    Lock source and destination accounts
        |
        v
    Validate available balance
        |
        v
    Debit source account
        |
        v
    Credit destination account
        |
        v
    Persist transfer
        |
        v
    Persist Outbox message
        |
        v
    Commit transaction
        |
        v
    HTTP 201
        |
        v
    Outbox Worker
        |
        v
    RabbitMQ
        |
        v
    Transactional Inbox Consumer

The transfer and its Outbox message are committed in the same database transaction.

---

## Concurrency Control

Account balances are protected using deterministic PostgreSQL row locking.

The implementation prevents concurrent transfers from overspending the same source account.

A concurrency scenario with:

- source balance: 1,000;
- 100 concurrent transfer attempts;
- amount per transfer: 100;

produced exactly:

- 10 successful transfers;
- 90 rejected transfers;
- final source balance: 0.

This validates the no-negative-balance invariant under contention.

---

## Idempotency

Transfer creation requires an idempotency key.

The system guarantees that:

- retrying the same request with the same key does not create another transfer;
- using the same key with a different payload is rejected;
- concurrent duplicate requests cannot create duplicate money movements.

---

## Transactional Outbox

Integration events are first persisted in PostgreSQL.

The Worker later publishes them to RabbitMQ using publisher confirmations.

The Outbox includes:

- attempt tracking;
- retry scheduling;
- exponential backoff;
- dead-letter lifecycle;
- persisted tracing context;
- concurrent processing using PostgreSQL row locking.

Outbox concurrency uses:

    FOR UPDATE SKIP LOCKED

The current Worker uses four bounded processing lanes.

Each lane receives its own EF Core DbContext.

---

## RabbitMQ Publisher Pool

The original Outbox publisher processed messages sequentially using a single RabbitMQ channel.

Performance testing identified this as a real bottleneck.

The optimized architecture uses four independent publishers.

Each publisher owns:

- its own RabbitMQ connection;
- its own channel;
- its own operation lock;
- publisher confirmations.

Reliability guarantees were preserved during the optimization.

---

## Transactional Inbox

The RabbitMQ consumer uses a Transactional Inbox.

The Inbox prevents duplicate message processing and persists consumer state transactionally.

Consumer processing includes:

- message identity;
- duplicate detection;
- transactional processing;
- manual acknowledgements;
- retry queues;
- dead-letter queue;
- retry exhaustion handling.

---

## Messaging Retry Strategy

Outbox publication retry delays:

- 5 seconds;
- 15 seconds;
- 45 seconds;
- 135 seconds.

Maximum Outbox publication attempts:

- 5.

Consumer retry delays:

- 5 seconds;
- 15 seconds;
- 45 seconds.

Maximum consumer processing attempts:

- 4.

Messages that exhaust the retry policy are routed to dead-letter handling.

---

## Observability

FluxPay includes a complete observability stack.

Components:

- OpenTelemetry SDK;
- OpenTelemetry Collector;
- Prometheus;
- Grafana;
- Grafana Tempo.

The application exposes metrics for:

- transfer processing;
- transfer failures;
- Outbox publishing;
- Outbox failures;
- dead-letter events;
- runtime information.

---

## Distributed Tracing

Trace context is propagated across synchronous and asynchronous boundaries.

The complete trace can contain:

    POST /api/transfers
        |
        v
    transfer.completed.v1 publish
        |
        v
    transfer.completed.v1 process

W3C trace context is persisted inside the Transactional Outbox and later injected into RabbitMQ headers.

This allows the original HTTP request and asynchronous message processing to belong to the same distributed trace.

---

## Health Checks

The API exposes two health endpoints.

Liveness:

    GET /health/live

Liveness does not depend on external infrastructure.

Readiness:

    GET /health/ready

Readiness currently validates PostgreSQL connectivity.

RabbitMQ is intentionally not part of API readiness because the Transactional Outbox decouples synchronous transfers from broker availability.

---

## Resilience and Fault Injection

FluxPay was also validated by deliberately interrupting PostgreSQL, RabbitMQ and application pods in the Kubernetes environment.

Observed scenarios include:

- RabbitMQ outage with successful financial commit and deferred Outbox publication;
- PostgreSQL outage causing readiness failure and no partial financial state;
- consumer retries at 5s, 15s and 45s followed by DLQ on attempt 4;
- healthy message processing after a failed message was isolated in the DLQ;
- in-flight consumer recovery after Worker loss under at-least-once delivery;
- transactional rollback after an interrupted API operation;
- safe client retry with the same `Idempotency-Key` after an uncertain/lost response.

These experiments distinguish liveness from readiness and document the system's actual failure semantics rather than claiming exactly-once messaging.

For the full experiment matrix, observed evidence, limitations and Kubernetes force-delete nuance, see [the resilience and fault-injection report](docs/resilience/resilience-and-fault-injection.md).

---

## Performance Engineering

Performance tests are implemented with Grafana k6.

The benchmark uses:

- 10 virtual users;
- 30 seconds;
- independent account pairs;
- unique idempotency keys;
- no intentional account-lock contention.

### Original Baseline

Results before Outbox parallelism:

| Metric | Result |
| --- | ---: |
| Successful transfers | 6,550 |
| Throughput | 212.29 transfers/s |
| Failure rate | 0.00% |
| Average latency | 45.79 ms |
| p95 | 70.61 ms |
| p99 | 152.53 ms |
| Outbox backlog after load | 5,205 |
| Observed Outbox drain rate | 59.50 events/s |

The benchmark revealed that the synchronous API was producing integration events faster than the serial Outbox publisher could deliver them.

### After Outbox Parallelism

Four bounded Outbox processing lanes were introduced.

Results:

| Metric | Result |
| --- | ---: |
| Successful transfers | 4,845 |
| Throughput | 159.57 transfers/s |
| Failure rate | 0.00% |
| Average latency | 61.60 ms |
| p95 | 104.61 ms |
| p99 | 160.40 ms |
| Immediate Outbox backlog | 2,951 |
| Observed 30-second drain lower bound | >= 98.37 events/s |

The optimization moved the dominant asynchronous backlog from PostgreSQL toward the RabbitMQ consumer.

Worker CPU usage also increased, demonstrating an important systems engineering trade-off: improving one subsystem can increase contention for shared resources.

These results were produced on a constrained development host where API, Worker, PostgreSQL, RabbitMQ and the observability stack share the same machine.

They must not be interpreted as production capacity.

Detailed results are available under:

    docs/performance/k6-baseline.md

---

## Automated Tests

The project currently includes:

| Suite | Tests |
| --- | ---: |
| Unit | 55 |
| Integration | 37 |
| Architecture | 7 |

Integration tests use Testcontainers to create isolated infrastructure.

Run all tests with:

    dotnet test FluxPay.slnx

---

## Continuous Integration

GitHub Actions runs automatically on:

- pushes to `main`;
- pull requests targeting `main`;
- manual workflow execution.

The CI pipeline performs:

    checkout
        |
        v
    setup .NET
        |
        v
    restore tools
        |
        v
    restore dependencies
        |
        v
    Release build
        |
        +--> Unit Tests
        +--> Integration Tests
        +--> Architecture Tests
        |
        v
    Docker Compose validation
        |
        v
    Upload TRX test artifacts

The first workflow execution on GitHub completed successfully.

---

## Containerization

FluxPay uses multi-stage Docker builds.

Application images:

- `fluxpay-api`;
- `fluxpay-worker`;
- `fluxpay-migrator`.

The final images contain only the required .NET runtime.

The SDK is used only during the Docker build stage.

Containers run using the non-root `app` user.

---

## Database Migrator

Database migrations are not executed implicitly by the API.

FluxPay provides a dedicated one-shot Migrator service.

Startup sequence:

    PostgreSQL
        |
        v
    PostgreSQL healthy
        |
        v
    FluxPay Migrator
        |
        v
    Apply EF Core migrations
        |
        v
    Migrator exits successfully
        |
        +--> API starts
        |
        +--> Worker starts

The Migrator is idempotent.

Running it against an up-to-date database results in zero pending migrations.

---

## Docker Compose Stack

The Compose environment includes:

- PostgreSQL;
- RabbitMQ;
- FluxPay Migrator;
- FluxPay API;
- FluxPay Worker;
- OpenTelemetry Collector;
- Prometheus;
- Grafana;
- Tempo.

---

## Running Locally with Docker

Clone the repository and enter the project directory.

Create the local environment file:

    cp .env.example .env

Replace the placeholder passwords inside `.env`.

The `.env` file is ignored by Git.

Start the complete environment:

    docker compose up -d --build

Inspect services:

    docker compose ps -a

The Migrator should finish with exit code 0.

The API is available by default on:

    127.0.0.1:5080

Check liveness:

    curl http://127.0.0.1:5080/health/live

Check readiness:

    curl http://127.0.0.1:5080/health/ready

---

## Running Without Docker

Requirements:

- .NET SDK 10;
- PostgreSQL;
- RabbitMQ.

Restore local tools:

    dotnet tool restore

Restore packages:

    dotnet restore FluxPay.slnx

Build:

    dotnet build FluxPay.slnx

Run tests:

    dotnet test FluxPay.slnx

Runtime services require their corresponding environment variables.

---

## Kubernetes

FluxPay includes a reproducible local Kubernetes laboratory under `deploy/kubernetes`.

The deployment models the application responsibilities explicitly:

- two stateless API replicas behind a Service;
- one background Worker Deployment;
- a one-shot Migrator Job;
- persistent PostgreSQL and RabbitMQ StatefulSets;
- ConfigMap/Secret separation;
- non-root workloads;
- health probes and resource limits;
- local image loading into kind.

The complete environment can be created from scratch with:

    ./scripts/kubernetes-bootstrap-kind.sh

The bootstrap was validated after deleting the entire kind cluster. It recreated the cluster, persistent workloads, Secret, all 7 database migrations, API replicas and Worker, then finished with healthy API and RabbitMQ checks.

The Kubernetes environment was also validated with a real transfer through the complete path:

    API -> PostgreSQL -> Transactional Outbox -> Worker -> RabbitMQ -> Consumer Inbox

The replay of the same request with the same `Idempotency-Key` returned the same transfer without moving the balance twice.

For manifests, architecture, persistence tests, E2E evidence and production considerations, see [the Kubernetes deployment guide](deploy/kubernetes/README.md).

---

## Repository Structure

    src/
      FluxPay.Api/
      FluxPay.Application/
      FluxPay.Domain/
      FluxPay.Infrastructure/
      FluxPay.Migrator/
      FluxPay.Worker/

    tests/
      FluxPay.UnitTests/
      FluxPay.IntegrationTests/
      FluxPay.ArchitectureTests/
      performance/

    deploy/
      observability/
      kubernetes/

    docs/
      performance/
      resilience/

    .github/
      workflows/

    docker-compose.yml

---

## Engineering Decisions Demonstrated

FluxPay demonstrates practical implementation of:

- transactional consistency;
- optimistic retry safety through idempotency;
- pessimistic row locking;
- deterministic lock ordering;
- concurrent money movement;
- Transactional Outbox;
- Transactional Inbox;
- publisher confirmations;
- retry and dead-letter strategies;
- distributed tracing;
- observability;
- health checks;
- bounded concurrency;
- performance bottleneck analysis;
- backpressure analysis;
- containerization;
- dedicated database migration lifecycle;
- automated integration testing;
- architectural dependency testing;
- CI automation;
- resilience and fault-injection validation.

---

# FluxPay Transfer Engine

## Português Brasileiro

FluxPay é um motor de transferências financeiras desenvolvido com .NET 10, PostgreSQL e RabbitMQ.

O projeto foi criado como laboratório técnico e projeto de portfólio voltado a problemas reais de backend sênior: consistência transacional, concorrência, idempotência, mensageria confiável, observabilidade, tracing distribuído, performance e deploy containerizado.

---

## Objetivo

Uma transferência no FluxPay deve:

- nunca criar dinheiro;
- nunca perder dinheiro;
- nunca permitir saldo negativo;
- ser atômica;
- suportar retries idempotentes;
- funcionar corretamente sob concorrência;
- gerar eventos de integração confiáveis;
- permanecer observável mesmo atravessando processamento assíncrono.

---

## Tecnologias Principais

- .NET 10
- ASP.NET Core
- C#
- Entity Framework Core 10
- PostgreSQL 18
- RabbitMQ 4
- Docker
- Docker Compose
- Kubernetes
- kind
- Kustomize
- OpenTelemetry
- Prometheus
- Grafana
- Grafana Tempo
- k6
- xUnit
- Testcontainers
- GitHub Actions

---

## Arquitetura

A solução é dividida em:

    FluxPay.Domain
        regras de domínio

    FluxPay.Application
        casos de uso e abstrações

    FluxPay.Infrastructure
        PostgreSQL, RabbitMQ, Outbox e Inbox

    FluxPay.Api
        interface HTTP

    FluxPay.Worker
        processamento assíncrono

    FluxPay.Migrator
        migrations do banco de dados

Testes de arquitetura verificam automaticamente as dependências entre essas camadas.

---

## Consistência das Transferências

Cada transferência ocorre dentro de uma transação PostgreSQL.

O fluxo realiza:

1. validação de idempotência;
2. abertura da transação;
3. lock das contas;
4. validação do saldo;
5. débito da origem;
6. crédito do destino;
7. persistência da transferência;
8. persistência do evento no Outbox;
9. commit.

Transferência e evento do Outbox são gravados atomicamente.

---

## Concorrência

O projeto utiliza locking determinístico no PostgreSQL.

Em uma prova com:

- saldo inicial de 1.000;
- 100 transferências concorrentes;
- valor de 100 por transferência;

o resultado foi:

- 10 transferências concluídas;
- 90 rejeitadas;
- saldo final igual a 0.

Nenhum saldo negativo foi produzido.

---

## Idempotência

Cada solicitação de transferência utiliza uma chave de idempotência.

O sistema impede:

- duplicação de transferências em retries;
- reutilização da mesma chave com payload diferente;
- criação concorrente da mesma transferência lógica.

---

## Transactional Outbox

Eventos não são enviados diretamente ao RabbitMQ durante a transação financeira.

Primeiro são persistidos no PostgreSQL.

O Worker posteriormente publica esses eventos no RabbitMQ com publisher confirmations.

O Outbox possui:

- contador de tentativas;
- política de retry;
- backoff;
- dead-letter;
- tracing persistido;
- processamento concorrente.

O processamento concorrente utiliza:

    FOR UPDATE SKIP LOCKED

O Worker atualmente utiliza quatro lanes independentes.

---

## Transactional Inbox

O consumer RabbitMQ utiliza Transactional Inbox.

Isso oferece:

- deduplicação;
- processamento transacional;
- ACK manual;
- retries;
- filas de retry;
- dead-letter queue;
- proteção contra processamento repetido.

---

## Observabilidade

A stack inclui:

- OpenTelemetry;
- OpenTelemetry Collector;
- Prometheus;
- Grafana;
- Tempo.

API e Worker exportam métricas e traces.

---

## Tracing Distribuído

O contexto W3C é persistido junto da mensagem do Outbox.

Quando o evento é publicado, o contexto original é restaurado e propagado através do RabbitMQ.

Um trace pode conectar:

    POST /api/transfers
        |
        v
    publicação do evento
        |
        v
    processamento pelo consumer

Isso permite acompanhar uma operação através dos limites síncronos e assíncronos do sistema.

---

## Health Checks

Liveness:

    GET /health/live

Readiness:

    GET /health/ready

O readiness valida PostgreSQL.

A API não depende diretamente da disponibilidade imediata do RabbitMQ porque o Transactional Outbox desacopla o command path do broker.

---

## Resiliência e Fault Injection

FluxPay também foi validado interrompendo deliberadamente PostgreSQL, RabbitMQ e Pods da aplicação no ambiente Kubernetes.

Cenários observados incluem:

- indisponibilidade do RabbitMQ com commit financeiro e publicação posterior pelo Outbox;
- indisponibilidade do PostgreSQL com falha de readiness e ausência de estado financeiro parcial;
- retries do consumer em 5s, 15s e 45s, seguidos de DLQ na tentativa 4;
- processamento normal de uma mensagem saudável após isolamento da mensagem problemática na DLQ;
- recuperação de entrega em voo após perda do Worker sob semântica at-least-once;
- rollback transacional após interrupção de uma operação na API;
- retry seguro do cliente com a mesma `Idempotency-Key` após resposta incerta/perdida.

Os experimentos distinguem liveness de readiness e registram as semânticas reais de falha do sistema, sem alegar exactly-once messaging.

Para a matriz completa de experimentos, evidências observadas, limitações e a nuance de force-delete no Kubernetes, consulte o [relatório de resiliência e fault injection](docs/resilience/resilience-and-fault-injection.md).

---

## Performance

Os testes utilizam Grafana k6.

Baseline original:

| Métrica | Resultado |
| --- | ---: |
| Transferências | 6.550 |
| Throughput | 212,29/s |
| Falhas | 0% |
| p95 | 70,61 ms |
| p99 | 152,53 ms |
| Backlog do Outbox | 5.205 |
| Drenagem observada | 59,50 eventos/s |

O teste identificou o publisher serial do Outbox como gargalo.

Após implementar quatro lanes de publicação:

| Métrica | Resultado |
| --- | ---: |
| Transferências | 4.845 |
| Throughput | 159,57/s |
| Falhas | 0% |
| p95 | 104,61 ms |
| p99 | 160,40 ms |
| Backlog imediato do Outbox | 2.951 |
| Limite inferior de drenagem | >= 98,37 eventos/s |

O principal backpressure migrou do Outbox PostgreSQL para o consumer RabbitMQ.

Também houve maior consumo de CPU pelo Worker, demonstrando o trade-off entre throughput assíncrono e recursos compartilhados.

Os resultados não representam capacidade de produção.

O relatório completo está em:

    docs/performance/k6-baseline.md

---

## Testes Automatizados

Atualmente:

| Suite | Quantidade |
| --- | ---: |
| Unitários | 55 |
| Integração | 37 |
| Arquitetura | 7 |

Os testes de integração utilizam Testcontainers.

Executar todos:

    dotnet test FluxPay.slnx

---

## CI

O GitHub Actions executa:

- restore;
- build Release;
- testes unitários;
- testes de integração;
- testes arquiteturais;
- validação do Docker Compose;
- upload dos resultados TRX.

O workflow roda em pushes e pull requests para `main`.

---

## Docker

API, Worker e Migrator utilizam Dockerfiles multi-stage.

As imagens finais:

- não contêm o .NET SDK;
- contêm somente o runtime necessário;
- executam como usuário não-root `app`.

---

## Migrator

O FluxPay não executa migrations automaticamente dentro da API.

Existe um serviço dedicado:

    FluxPay.Migrator

No Docker Compose:

    PostgreSQL healthy
        |
        v
    Migrator
        |
        v
    migrations aplicadas
        |
        v
    exit code 0
        |
        +--> API
        +--> Worker

Isso mantém o lifecycle de banco separado do runtime da aplicação.

---

## Executando com Docker

Crie seu ambiente local:

    cp .env.example .env

Substitua os valores `change-me` por senhas locais.

Suba o ambiente:

    docker compose up -d --build

Veja os serviços:

    docker compose ps -a

Teste a API:

    curl http://127.0.0.1:5080/health/live

E:

    curl http://127.0.0.1:5080/health/ready

---

## Kubernetes

O FluxPay inclui um laboratório Kubernetes local e reproduzível em `deploy/kubernetes`.

A implantação representa explicitamente as responsabilidades da aplicação:

- duas réplicas stateless da API atrás de um Service;
- um Worker em Deployment;
- um Migrator executado como Job;
- PostgreSQL e RabbitMQ persistentes em StatefulSets;
- separação entre ConfigMap e Secret;
- workloads executados como non-root;
- health probes e limites de recursos;
- carregamento das imagens locais no kind.

Todo o ambiente pode ser criado do zero com:

    ./scripts/kubernetes-bootstrap-kind.sh

O bootstrap foi validado após excluir completamente o cluster kind. Ele recriou o cluster, workloads persistentes, Secret, as 7 migrations do banco, as réplicas da API e o Worker, terminando com API e RabbitMQ saudáveis.

O ambiente Kubernetes também foi validado com uma transferência real pelo fluxo completo:

    API -> PostgreSQL -> Transactional Outbox -> Worker -> RabbitMQ -> Consumer Inbox

O replay da mesma requisição com a mesma `Idempotency-Key` retornou a mesma transferência sem movimentar o saldo duas vezes.

Para manifests, arquitetura, testes de persistência, evidências E2E e considerações de produção, consulte o [guia de implantação Kubernetes](deploy/kubernetes/README.md).

---

## Estrutura do Projeto

    src/
      FluxPay.Api/
      FluxPay.Application/
      FluxPay.Domain/
      FluxPay.Infrastructure/
      FluxPay.Migrator/
      FluxPay.Worker/

    tests/
      FluxPay.UnitTests/
      FluxPay.IntegrationTests/
      FluxPay.ArchitectureTests/
      performance/

    deploy/
      observability/
      kubernetes/

    docs/
      performance/
      resilience/

    .github/
      workflows/

---

## O Que Este Projeto Demonstra

FluxPay demonstra na prática:

- arquitetura backend em .NET;
- modelagem de domínio;
- consistência financeira;
- concorrência;
- idempotência;
- PostgreSQL locking;
- Transactional Outbox;
- Transactional Inbox;
- RabbitMQ;
- publisher confirms;
- retries e dead-letter;
- métricas;
- tracing distribuído;
- OpenTelemetry;
- análise de gargalos;
- backpressure;
- testes de carga;
- Testcontainers;
- testes arquiteturais;
- Docker;
- database migration lifecycle;
- GitHub Actions;
- CI automatizado;
- validação de resiliência e fault injection.

O objetivo não é apenas atingir um número alto de requests por segundo.

O objetivo é conseguir demonstrar por que o sistema se comporta como se comporta, quais garantias foram preservadas e como gargalos reais foram identificados e tratados.
