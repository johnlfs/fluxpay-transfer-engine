# FluxPay Performance Baseline

## English

### 1. Objective

This benchmark evaluates two distinct parts of FluxPay:

1. the synchronous transfer command path;
2. the asynchronous Transactional Outbox pipeline.

The tests were executed on a constrained development server where the API, Worker, PostgreSQL, RabbitMQ, OpenTelemetry Collector, Prometheus, Grafana, and Tempo share the same host.

These numbers must not be interpreted as production capacity.

The goal of the benchmark is to:

- establish a reproducible baseline;
- identify real bottlenecks;
- validate system behavior under load;
- implement one targeted optimization;
- repeat the same test;
- compare the architectural effects.

---

### 2. Test Environment

Load generation tool:

- Grafana k6 2.2.0

Test profile:

- 10 virtual users;
- 30 seconds;
- constant VUs;
- one independent source/destination account pair per VU;
- unique idempotency key for every transfer;
- transfer amount of 1.00.

Each virtual user uses its own account pair.

This intentionally avoids account-level lock contention and primarily measures the normal transfer execution path.

The system preserves:

- PostgreSQL transactions;
- idempotency;
- account row locking;
- Transactional Outbox;
- RabbitMQ publisher confirmations;
- Transactional Inbox;
- distributed tracing.

---

## 3. Original Baseline

### Serial Outbox Publisher

The first benchmark was executed with the original Outbox design.

Results:

| Metric | Result |
| --- | ---: |
| Successful transfers | 6,550 |
| Transfer throughput | 212.29 transfers/s |
| Transfer failure rate | 0.00% |
| Average transfer latency | 45.79 ms |
| Median transfer latency | 43.18 ms |
| p90 | 61.32 ms |
| p95 | 70.61 ms |
| p99 | 152.53 ms |
| Maximum latency | 434.69 ms |
| Immediate Outbox backlog | 5,205 messages |
| Observed Outbox drain rate | 59.50 events/s |
| Worker CPU during drain | 13.6% |

The synchronous transfer path sustained more than 200 successful transfers per second with zero transfer failures.

However, the asynchronous pipeline could not keep pace with the rate at which the API created Outbox messages.

Immediately after the benchmark, 5,205 Outbox messages were still pending.

A separate 30-second observation measured the Outbox draining at approximately 59.50 events per second.

RabbitMQ itself did not contain a significant backlog at that point.

This indicated that the dominant bottleneck existed between PostgreSQL Outbox storage and RabbitMQ publication.

---

## 4. Root Cause Analysis

The original publishing path was effectively serial.

Architecture before optimization:

    Worker
      |
      v
    OutboxProcessor
      |
      v
    foreach message
      |
      v
    PublishAsync()
      |
      v
    SemaphoreSlim(1)
      |
      v
    single RabbitMQ channel
      |
      v
    publisher confirmation
      |
      v
    next message

Each Outbox message waited for the previous RabbitMQ publication operation to complete.

RabbitMQ publisher confirmations were intentionally enabled.

They were not removed merely to improve benchmark numbers because they are part of the reliability strategy.

Using Task.WhenAll over a single Entity Framework DbContext was also rejected because DbContext is not thread-safe.

---

## 5. Optimization

The Outbox Worker was changed to use four independent processing lanes.

Configuration:

    OutboxWorker:
      BatchSize: 100
      PollingIntervalMilliseconds: 1000
      Parallelism: 4

Each processing lane creates its own dependency injection scope.

Therefore, each lane receives its own FluxPayDbContext.

The architecture became:

    Outbox Worker
      |
      +-- Lane 1 --> DbContext 1 --> RabbitMQ Publisher 1
      |
      +-- Lane 2 --> DbContext 2 --> RabbitMQ Publisher 2
      |
      +-- Lane 3 --> DbContext 3 --> RabbitMQ Publisher 3
      |
      +-- Lane 4 --> DbContext 4 --> RabbitMQ Publisher 4

A RabbitMqPublisherPool was introduced with four independent RabbitMqPublisher instances.

Each publisher owns:

- its own RabbitMQ connection;
- its own RabbitMQ channel;
- its own serialization lock;
- publisher confirmations.

Concurrent Outbox processing continues to rely on PostgreSQL row locking:

    FOR UPDATE SKIP LOCKED

This allows multiple lanes to safely compete for Outbox work without sharing Entity Framework DbContexts.

---

## 6. Benchmark After Outbox Parallelism

The same k6 profile was executed again:

- 10 virtual users;
- 30 seconds;
- same transfer endpoint;
- same account isolation model;
- same host;
- same reliability guarantees.

Results:

| Metric | Before | After |
| --- | ---: | ---: |
| Successful transfers | 6,550 | 4,845 |
| Transfer throughput | 212.29/s | 159.57/s |
| Failure rate | 0.00% | 0.00% |
| Average latency | 45.79 ms | 61.60 ms |
| Median latency | 43.18 ms | 56.08 ms |
| p90 | 61.32 ms | 87.76 ms |
| p95 | 70.61 ms | 104.61 ms |
| p99 | 152.53 ms | 160.40 ms |
| Maximum latency | 434.69 ms | 206.10 ms |
| Immediate Outbox backlog | 5,205 | 2,951 |
| Worker CPU observed | 13.6% | 41.4% |

The second benchmark still produced zero transfer failures.

The immediate PostgreSQL Outbox backlog fell substantially.

---

## 7. Asynchronous Pipeline Result

After the optimization, the Outbox was observed with 2,951 pending messages.

Thirty seconds later, the Outbox had reached zero.

Therefore:

    2951 / 30 = 98.37 events/s

Because the Outbox reached zero before or during the end of the observation window, 98.37 events/s is a lower bound rather than an exact maximum throughput value.

The actual instantaneous publication throughput was higher during portions of the drain.

Another database query performed shortly after the benchmark already showed the pending count falling from 2,951 to 899.

No Outbox messages entered retry state.

No Outbox messages were dead-lettered.

Final state:

| Condition | Result |
| --- | ---: |
| Outbox pending | 0 |
| Outbox waiting retry | 0 |
| Outbox dead-lettered | 0 |

---

## 8. Bottleneck Migration

The optimization changed where backpressure appeared.

Before optimization:

    PostgreSQL Outbox
        |
        | bottleneck
        v
    RabbitMQ
        |
        v
    Consumer

After optimization:

    PostgreSQL Outbox
        |
        v
    RabbitMQ
        |
        | new bottleneck
        v
    Consumer

During the optimized benchmark, the main RabbitMQ consumer queue reached:

| RabbitMQ metric | Observed value |
| --- | ---: |
| messages_ready | 2,064 |
| messages_unacknowledged | 1 |

The retry queues remained empty.

The dead-letter queue remained empty.

This demonstrates that parallel Outbox publishing successfully moved pressure downstream.

The Outbox publisher was no longer the only dominant asynchronous bottleneck.

The next limiting component became the consumer path.

---

## 9. Resource Trade-off

The optimization also increased Worker CPU usage:

| Version | Worker CPU |
| --- | ---: |
| Serial publisher | 13.6% |
| Four-lane publisher | 41.4% |

At the same time, synchronous transfer throughput decreased:

| Version | API throughput |
| --- | ---: |
| Serial Outbox publisher | 212.29 transfers/s |
| Four-lane Outbox publisher | 159.57 transfers/s |

This does not mean the parallel design is inherently slower.

All components were running on the same constrained host.

The API, Worker, PostgreSQL, RabbitMQ, telemetry stack, and monitoring stack compete for the same CPU, memory, database I/O, and scheduler time.

Increasing asynchronous work therefore increased contention for shared resources.

This benchmark demonstrates an important systems engineering principle:

Increasing throughput in one subsystem can reduce available resources for another subsystem.

---

## 10. Engineering Decision

Further optimization was intentionally stopped at this point.

The next obvious optimization candidates are:

- RabbitMQ consumer concurrency;
- consumer prefetch tuning;
- Inbox processing throughput;
- additional Worker processes;
- independent infrastructure hosts;
- database connection pool tuning.

However, increasing concurrency without a separate benchmark and resource budget would turn performance tuning into an uncontrolled exercise.

The objective of this experiment was already achieved:

1. create a reproducible benchmark;
2. measure the original system;
3. identify a real bottleneck;
4. explain its architectural cause;
5. implement bounded parallelism;
6. preserve reliability guarantees;
7. repeat the same benchmark;
8. observe the bottleneck move downstream.

---

## 11. Reliability Guarantees Preserved

The optimization did not remove the reliability mechanisms already implemented in FluxPay.

The system still uses:

- database transactions for transfers;
- deterministic account locking;
- transfer idempotency;
- Transactional Outbox;
- PostgreSQL FOR UPDATE SKIP LOCKED;
- RabbitMQ persistent messages;
- RabbitMQ publisher confirmations;
- Outbox retry policy;
- Outbox dead-letter lifecycle;
- consumer retry queues;
- consumer dead-letter queue;
- Transactional Inbox;
- W3C distributed trace propagation.

The benchmark finished with:

- zero transfer failures;
- zero pending Outbox messages after drain;
- zero Outbox retries;
- zero Outbox dead-lettered messages;
- zero RabbitMQ pending messages after drain.

The API and Worker also completed graceful shutdown with exit code 0.

---

## 12. Interview Talking Points

This experiment can be described in an interview as follows:

"I first created a repeatable k6 baseline instead of optimizing based on assumptions.

The synchronous transfer path sustained roughly 212 transfers per second with zero failures and a p95 close to 71 milliseconds.

The benchmark showed that the API was not the first bottleneck. The Transactional Outbox accumulated more than five thousand messages.

I measured the Outbox separately and found that its serial publisher drained approximately 59 events per second.

The implementation was intentionally conservative: one publisher, one channel, publisher confirms, and sequential processing.

Instead of removing delivery guarantees, I introduced four bounded processing lanes. Each lane uses its own EF Core DbContext and the publisher pool uses independent RabbitMQ connections and channels.

PostgreSQL FOR UPDATE SKIP LOCKED prevents concurrent lanes from processing the same Outbox row.

After the change, the PostgreSQL backlog decreased much faster and the dominant backpressure moved into the RabbitMQ consumer queue.

The experiment also showed a resource trade-off: because all services ran on the same small host, additional Worker concurrency consumed more CPU and reduced synchronous API throughput.

At that point I stopped tuning blindly. The next optimization would require a dedicated consumer benchmark and explicit resource budget."

---

## 13. Performance Artifacts

The repository contains the following reproducible artifacts:

- tests/performance/transfer-smoke.js
- tests/performance/transfer-baseline.js
- tests/performance/results/transfer-baseline-before-outbox-parallelism.json
- tests/performance/results/transfer-baseline-after-outbox-parallelism.json

These artifacts preserve both the original and optimized benchmark results.

---

# Baseline de Performance do FluxPay

## Português Brasileiro

### 1. Objetivo

Este benchmark avalia duas partes distintas do FluxPay:

1. o caminho síncrono de execução de transferências;
2. o pipeline assíncrono do Transactional Outbox.

Os testes foram executados em um servidor de desenvolvimento com recursos limitados, no qual API, Worker, PostgreSQL, RabbitMQ, OpenTelemetry Collector, Prometheus, Grafana e Tempo compartilham o mesmo host.

Esses números não devem ser interpretados como capacidade de produção.

O objetivo do benchmark é:

- estabelecer um baseline reproduzível;
- identificar gargalos reais;
- validar o comportamento do sistema sob carga;
- implementar uma otimização direcionada;
- repetir exatamente o mesmo teste;
- comparar os efeitos arquiteturais.

---

### 2. Cenário de Teste

Ferramenta:

- Grafana k6 2.2.0

Perfil de carga:

- 10 usuários virtuais;
- 30 segundos;
- VUs constantes;
- um par independente de contas origem/destino por VU;
- chave de idempotência exclusiva para cada transferência;
- valor de transferência de 1,00.

Cada usuário virtual utiliza seu próprio par de contas.

Isso evita propositalmente contenção entre locks de contas e mede principalmente o caminho normal de execução das transferências.

O sistema mantém:

- transações PostgreSQL;
- idempotência;
- locks das contas;
- Transactional Outbox;
- publisher confirms do RabbitMQ;
- Transactional Inbox;
- tracing distribuído.

---

## 3. Baseline Original

### Publisher Serial do Outbox

O primeiro benchmark foi executado com o desenho original do Outbox.

Resultados:

| Métrica | Resultado |
| --- | ---: |
| Transferências bem-sucedidas | 6.550 |
| Throughput de transferências | 212,29 transferências/s |
| Taxa de falhas | 0,00% |
| Latência média | 45,79 ms |
| Mediana | 43,18 ms |
| p90 | 61,32 ms |
| p95 | 70,61 ms |
| p99 | 152,53 ms |
| Latência máxima | 434,69 ms |
| Backlog imediato do Outbox | 5.205 mensagens |
| Vazão observada de drenagem do Outbox | 59,50 eventos/s |
| CPU do Worker durante drenagem | 13,6% |

O caminho síncrono da API sustentou mais de 200 transferências bem-sucedidas por segundo sem nenhuma falha de transferência.

Entretanto, o pipeline assíncrono não conseguia acompanhar a velocidade com que a API criava mensagens no Outbox.

Imediatamente após o benchmark, 5.205 mensagens do Outbox ainda estavam pendentes.

Uma observação separada de 30 segundos mediu aproximadamente 59,50 eventos por segundo sendo drenados.

O RabbitMQ não apresentava backlog significativo naquele momento.

Isso indicou que o gargalo dominante estava entre o armazenamento do Outbox no PostgreSQL e a publicação no RabbitMQ.

---

## 4. Análise da Causa

O caminho original de publicação era efetivamente serial.

Arquitetura antes da otimização:

    Worker
      |
      v
    OutboxProcessor
      |
      v
    foreach mensagem
      |
      v
    PublishAsync()
      |
      v
    SemaphoreSlim(1)
      |
      v
    um único channel RabbitMQ
      |
      v
    publisher confirmation
      |
      v
    próxima mensagem

Cada mensagem do Outbox aguardava a operação de publicação anterior terminar.

Os publisher confirms do RabbitMQ estavam habilitados intencionalmente.

Eles não foram removidos apenas para melhorar os números do benchmark, pois fazem parte da estratégia de confiabilidade.

Também foi descartado utilizar Task.WhenAll sobre um único DbContext do Entity Framework, pois DbContext não é thread-safe.

---

## 5. Otimização

O Worker do Outbox foi alterado para utilizar quatro lanes independentes de processamento.

Configuração:

    OutboxWorker:
      BatchSize: 100
      PollingIntervalMilliseconds: 1000
      Parallelism: 4

Cada lane cria seu próprio escopo de injeção de dependência.

Consequentemente, cada lane recebe seu próprio FluxPayDbContext.

A arquitetura passou a ser:

    Outbox Worker
      |
      +-- Lane 1 --> DbContext 1 --> RabbitMQ Publisher 1
      |
      +-- Lane 2 --> DbContext 2 --> RabbitMQ Publisher 2
      |
      +-- Lane 3 --> DbContext 3 --> RabbitMQ Publisher 3
      |
      +-- Lane 4 --> DbContext 4 --> RabbitMQ Publisher 4

Foi criado um RabbitMqPublisherPool com quatro instâncias independentes de RabbitMqPublisher.

Cada publisher possui:

- conexão RabbitMQ própria;
- channel RabbitMQ próprio;
- lock de serialização próprio;
- publisher confirms.

O processamento concorrente do Outbox continua protegido no PostgreSQL por:

    FOR UPDATE SKIP LOCKED

Isso permite que múltiplas lanes disputem trabalho do Outbox sem compartilhar DbContexts do Entity Framework.

---

## 6. Benchmark Após Paralelismo do Outbox

O mesmo perfil k6 foi executado novamente:

- 10 usuários virtuais;
- 30 segundos;
- mesmo endpoint de transferência;
- mesmo modelo de isolamento de contas;
- mesmo host;
- mesmas garantias de confiabilidade.

Resultados:

| Métrica | Antes | Depois |
| --- | ---: | ---: |
| Transferências bem-sucedidas | 6.550 | 4.845 |
| Throughput | 212,29/s | 159,57/s |
| Taxa de falhas | 0,00% | 0,00% |
| Latência média | 45,79 ms | 61,60 ms |
| Mediana | 43,18 ms | 56,08 ms |
| p90 | 61,32 ms | 87,76 ms |
| p95 | 70,61 ms | 104,61 ms |
| p99 | 152,53 ms | 160,40 ms |
| Latência máxima | 434,69 ms | 206,10 ms |
| Backlog imediato do Outbox | 5.205 | 2.951 |
| CPU observada do Worker | 13,6% | 41,4% |

O segundo benchmark também apresentou zero falhas de transferência.

O backlog imediato no PostgreSQL Outbox caiu substancialmente.

---

## 7. Resultado do Pipeline Assíncrono

Após a otimização, o Outbox foi observado com 2.951 mensagens pendentes.

Trinta segundos depois, o Outbox havia chegado a zero.

Portanto:

    2951 / 30 = 98,37 eventos/s

Como o Outbox atingiu zero antes ou durante o final da janela de observação, 98,37 eventos/s representa um limite inferior, e não o throughput máximo exato.

A vazão instantânea real foi maior em partes da drenagem.

Outra consulta ao banco realizada pouco depois do benchmark já mostrava o número de pendentes caindo de 2.951 para 899.

Nenhuma mensagem do Outbox entrou em retry.

Nenhuma mensagem do Outbox foi enviada para dead-letter.

Estado final:

| Condição | Resultado |
| --- | ---: |
| Outbox pendente | 0 |
| Outbox aguardando retry | 0 |
| Outbox dead-lettered | 0 |

---

## 8. Migração do Gargalo

A otimização alterou o local em que o backpressure apareceu.

Antes:

    PostgreSQL Outbox
        |
        | gargalo
        v
    RabbitMQ
        |
        v
    Consumer

Depois:

    PostgreSQL Outbox
        |
        v
    RabbitMQ
        |
        | novo gargalo
        v
    Consumer

Durante o benchmark otimizado, a fila principal do consumer RabbitMQ chegou a:

| Métrica RabbitMQ | Valor observado |
| --- | ---: |
| messages_ready | 2.064 |
| messages_unacknowledged | 1 |

As filas de retry permaneceram vazias.

A dead-letter queue permaneceu vazia.

Isso demonstra que a publicação paralela do Outbox conseguiu deslocar a pressão para a próxima etapa do pipeline.

O publisher do Outbox deixou de ser o único gargalo assíncrono dominante.

O próximo componente limitante passou a ser o consumer.

---

## 9. Trade-off de Recursos

A otimização também aumentou o consumo de CPU do Worker:

| Versão | CPU do Worker |
| --- | ---: |
| Publisher serial | 13,6% |
| Publisher com quatro lanes | 41,4% |

Ao mesmo tempo, o throughput síncrono de transferências caiu:

| Versão | Throughput da API |
| --- | ---: |
| Outbox serial | 212,29 transferências/s |
| Outbox com quatro lanes | 159,57 transferências/s |

Isso não significa que o desenho paralelo seja inerentemente mais lento.

Todos os componentes estavam executando no mesmo host com recursos limitados.

API, Worker, PostgreSQL, RabbitMQ, stack de telemetry e stack de monitoramento disputavam CPU, memória, I/O de banco e tempo de escalonamento.

Ao aumentar o trabalho assíncrono, aumentou-se também a contenção por recursos compartilhados.

O benchmark demonstra um princípio importante de engenharia de sistemas:

Aumentar o throughput de um subsistema pode diminuir os recursos disponíveis para outro.

---

## 10. Decisão de Engenharia

A otimização foi propositalmente interrompida neste ponto.

Os próximos candidatos naturais seriam:

- concorrência no consumer RabbitMQ;
- ajuste de prefetch;
- throughput do Inbox;
- múltiplos processos Worker;
- separação dos componentes em hosts independentes;
- tuning do pool de conexões do banco.

Entretanto, aumentar concorrência sem um benchmark separado e sem orçamento explícito de recursos transformaria a otimização em um exercício sem limite.

O objetivo deste experimento já foi atingido:

1. criar um benchmark reproduzível;
2. medir o sistema original;
3. identificar um gargalo real;
4. explicar sua causa arquitetural;
5. implementar paralelismo limitado;
6. preservar as garantias de confiabilidade;
7. repetir exatamente o mesmo benchmark;
8. observar o gargalo migrar para outra etapa.

---

## 11. Garantias de Confiabilidade Preservadas

A otimização não removeu os mecanismos de confiabilidade já implementados no FluxPay.

O sistema continua utilizando:

- transações de banco para transferências;
- locking determinístico de contas;
- idempotência;
- Transactional Outbox;
- PostgreSQL FOR UPDATE SKIP LOCKED;
- mensagens persistentes no RabbitMQ;
- publisher confirms;
- política de retry do Outbox;
- lifecycle de dead-letter do Outbox;
- filas de retry do consumer;
- dead-letter queue do consumer;
- Transactional Inbox;
- propagação W3C de tracing distribuído.

O benchmark terminou com:

- zero falhas de transferência;
- zero mensagens pendentes no Outbox após drenagem;
- zero retries do Outbox;
- zero mensagens dead-lettered do Outbox;
- zero mensagens pendentes no RabbitMQ após drenagem.

API e Worker também realizaram graceful shutdown com exit code 0.

---

## 12. Pontos para Entrevista

Uma forma de explicar este experimento em entrevista:

"Primeiro eu criei um baseline reproduzível com k6 em vez de otimizar por suposição.

O caminho síncrono de transferência sustentou aproximadamente 212 transferências por segundo, sem falhas, com p95 próximo de 71 milissegundos.

O teste mostrou que a API não era o primeiro gargalo. O Transactional Outbox acumulou mais de cinco mil mensagens.

Medi o Outbox separadamente e encontrei uma drenagem de aproximadamente 59 eventos por segundo no publisher serial.

A implementação era propositalmente conservadora: um publisher, um channel, publisher confirms e processamento sequencial.

Em vez de remover garantias de entrega, implementei quatro lanes limitadas. Cada lane usa seu próprio DbContext do EF Core, e o pool de publishers utiliza conexões e channels independentes no RabbitMQ.

O PostgreSQL utiliza FOR UPDATE SKIP LOCKED para impedir que duas lanes processem simultaneamente a mesma linha do Outbox.

Depois da mudança, o backlog do PostgreSQL caiu muito mais rápido e o principal backpressure migrou para a fila do consumer RabbitMQ.

O experimento também revelou um trade-off de recursos: como todos os serviços executavam no mesmo host pequeno, a concorrência adicional do Worker consumiu mais CPU e reduziu o throughput síncrono da API.

Nesse ponto eu interrompi o tuning cego. A próxima otimização exigiria um benchmark específico do consumer e um orçamento explícito de recursos."

---

## 13. Artefatos de Performance

O repositório contém os seguintes artefatos reproduzíveis:

- tests/performance/transfer-smoke.js
- tests/performance/transfer-baseline.js
- tests/performance/results/transfer-baseline-before-outbox-parallelism.json
- tests/performance/results/transfer-baseline-after-outbox-parallelism.json

Esses arquivos preservam os resultados anteriores e posteriores à otimização.
