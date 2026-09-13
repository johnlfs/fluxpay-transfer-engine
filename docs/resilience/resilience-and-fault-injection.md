# FluxPay Resilience and Fault-Injection Evidence

## English

### 1. Purpose

This document records failure experiments executed against the Kubernetes deployment of FluxPay Transfer Engine.

The goal was not to claim theoretical resilience from code inspection alone. Dependencies were deliberately interrupted and the resulting behavior was observed through HTTP responses, Kubernetes state, PostgreSQL state, RabbitMQ queues, Worker logs, and Outbox/Inbox records.

These experiments are engineering evidence for the implemented architecture in a local kind environment. They are not a formal availability certification.

---

### 2. System Under Test

The tested deployment contained:

- two FluxPay API replicas;
- one FluxPay Worker replica;
- one PostgreSQL StatefulSet replica with persistent storage;
- one RabbitMQ StatefulSet replica with persistent storage;
- a completed database Migrator Job;
- NodePort access to the API;
- Transactional Outbox publication;
- RabbitMQ publisher confirmations;
- Transactional Inbox consumption;
- TTL-based retry queues;
- a dead-letter queue.

The synchronous path is:

    Client
      |
      v
    FluxPay API
      |
      v
    PostgreSQL transaction
      |
      +--> account debit
      +--> account credit
      +--> transfer
      +--> idempotency record
      +--> Outbox message

The asynchronous path is:

    PostgreSQL Outbox
      |
      v
    Worker / Outbox Processor
      |
      v
    RabbitMQ
      |
      v
    transfer.completed.v1 consumer
      |
      v
    Transactional Inbox

---

### 3. Failure Matrix

| Failure scenario | Observed behavior | Main guarantee demonstrated |
| --- | --- | --- |
| RabbitMQ unavailable | Transfer remained available; Outbox retained the event and later published it | Broker outage does not corrupt or block the synchronous financial transaction |
| PostgreSQL unavailable | API readiness became unhealthy; transfer request failed with no partial financial state | Fail-safe behavior for the critical transactional dependency |
| Consumer processing repeatedly fails | 5s, 15s and 45s retries, then DLQ on attempt 4 | Bounded retry and poison-message isolation |
| Valid message after DLQ | New message was processed normally while the failed message remained isolated | DLQ does not permanently block the pipeline |
| Worker lost during an unacknowledged delivery | Delivery returned to the consumer pipeline and was completed by the replacement Worker | At-least-once delivery with durable broker state |
| API request interrupted inside an open DB transaction | No account, transfer, idempotency or Outbox partial state remained | PostgreSQL transactional rollback protects financial state |
| Client retries with the same Idempotency-Key | One transfer was created; later replay returned the same transfer | Safe client retry after an uncertain/lost response |

---

### 4. RabbitMQ Outage

RabbitMQ was scaled to zero replicas while PostgreSQL, the API and the Worker remained deployed. A transfer was submitted while the broker was unavailable.

The transfer completed successfully. The source was debited once, the destination was credited once, the transfer stayed `Completed`, and one Outbox message existed but was not yet published.

The Outbox persisted the publication failure and scheduled retry. The first observed failure contained a broker-unreachable error.

After RabbitMQ was restored, the Worker recovered without restart. The same Outbox row eventually had `published_at` populated, `next_attempt_at` cleared, `dead_lettered_at` null, and `last_error` cleared.

The final `attempt_count` was 3 because one additional publication attempt occurred while RabbitMQ was starting but was not yet ready. The third attempt succeeded.

The consumer Inbox then contained one processed message and the RabbitMQ queues drained to zero.

#### Conclusion

RabbitMQ is intentionally not a synchronous dependency of the transfer command. The Transactional Outbox decouples the durable financial transaction from immediate broker availability.

This experiment demonstrated:

- no lost transfer;
- no lost integration event;
- persisted publication failure state;
- retry with backoff;
- publisher transport recovery;
- no Worker restart required.

---

### 5. PostgreSQL Outage

#### 5.1 Liveness and readiness

PostgreSQL was scaled to zero replicas.

Before the outage, `/health/live` and `/health/ready` both returned HTTP 200. During the outage, both API pods remained `Running` but became `0/1 Ready`.

Direct pod access showed:

- `/health/live` -> HTTP 200, `Healthy`;
- `/health/ready` -> HTTP 503, `Unhealthy`;
- PostgreSQL health check -> `Unhealthy`.

Once both API pods were removed from ready Service endpoints, NodePort traffic could no longer be used to test process liveness. Direct pod access was required to distinguish liveness from Service routing.

#### 5.2 Financial fail-safe behavior

Two test accounts were created with balances `1000.00` and `100.00`. PostgreSQL was then stopped and a transfer of `125.50` was sent directly to a live API pod.

The request returned HTTP 500 because the transactional dependency was unavailable.

After PostgreSQL was restored, the database still showed:

- source balance `1000.00`;
- destination balance `100.00`;
- unchanged transfer count;
- unchanged idempotency count;
- unchanged Outbox count;
- no row for the attempted Idempotency-Key.

#### Conclusion

PostgreSQL is a critical synchronous dependency of the transfer command. When PostgreSQL is unavailable, FluxPay fails the operation rather than accepting an operation it cannot persist atomically.

---

### 6. Consumer Retry and Dead-Letter Queue

The consumer allows four processing attempts. Retry delays are:

| Failed attempt | Delay before next attempt |
| ---: | ---: |
| 1 | 5 seconds |
| 2 | 15 seconds |
| 3 | 45 seconds |
| 4 | no retry; dead-letter |

Structurally invalid messages are handled as permanent failures and are dead-lettered without transient retries.

A structurally valid `transfer.completed.v1` message was published while PostgreSQL was unavailable. Envelope validation succeeded, but processing failed inside the Transactional Inbox because the required PostgreSQL transaction could not complete.

Observed Worker logs showed:

    Attempt=1 -> retry -> 5 seconds
    Attempt=2 -> retry -> 15 seconds
    Attempt=3 -> retry -> 45 seconds
    Attempt=4 -> exhausted -> dead-letter

Queue observation confirmed the message moving through the 5s, 15s and 45s retry queues and finally reaching `fluxpay.transfer-completed.dlq`.

After PostgreSQL was restored, the failed message had no partial Inbox row.

A second valid event was then published while the failed event remained in the DLQ. The new event was processed on attempt 1, created one completed Inbox record, was acknowledged, and left the main queue empty.

#### Conclusion

The test demonstrated bounded retries, real delay queues, final DLQ isolation, no partial Inbox claim, and continued processing of later healthy messages.

---

### 7. Worker Loss During an In-Flight Delivery

A controlled PostgreSQL lock was placed on `consumer_inbox_messages`. A real transfer was then executed and its Outbox event was published.

The consumer received the event but blocked while trying to persist the Inbox claim. RabbitMQ showed `messages_ready = 0` and `messages_unacknowledged = 1`.

PostgreSQL simultaneously showed the controlled lock holder and a Worker session waiting while inserting into the Inbox.

The Worker pod was force-deleted. Kubernetes created a replacement Worker pod with a new pod UID. While the PostgreSQL lock was still held, RabbitMQ again showed one unacknowledged delivery and PostgreSQL showed a new waiting consumer session.

After the lock was released, the replacement Worker completed and acknowledged the message.

The Inbox contained exactly one row for the event, the RabbitMQ main queue returned to zero, and the financial transfer remained completed with the expected balances.

The final observed consumer attempt was 3 because the controlled lock caused retry activity during the experiment. Therefore this test should not be described as "the first delivery was redelivered exactly once."

The accurate conclusion is that the consumer pipeline recovered an in-flight delivery after the original Worker disappeared, consistent with at-least-once semantics.

FluxPay does not claim exactly-once messaging. The reliability model is:

    at-least-once broker delivery
        +
    Transactional Inbox
        +
    idempotent consumer processing
        =
    one persisted consumer effect for the message

---

### 8. API Interruption During an Open Transfer Transaction

Code inspection confirmed this transaction order:

1. open PostgreSQL transaction;
2. attempt the Idempotency-Key claim;
3. load both accounts using deterministic `ORDER BY id FOR UPDATE`;
4. debit source;
5. credit destination;
6. complete the transfer entity;
7. add transfer;
8. add Outbox event;
9. save changes;
10. complete the idempotency record;
11. commit.

The idempotency claim therefore belongs to the same database transaction as the money movement and Outbox write.

Two new accounts were created with balances `1000.00` and `100.00`. A controlled PostgreSQL transaction locked both account rows. A transfer request was sent directly to one selected API replica.

PostgreSQL showed that API connection blocked on `SELECT ... ORDER BY id FOR UPDATE`, and the blocking relationship identified the selected API pod IP as the blocked client.

A separate database session could not see the new idempotency row because that claim had not been committed.

#### Kubernetes force-delete nuance

The selected API pod object was removed with force delete and Kubernetes created a replacement API pod. However, the old PostgreSQL client session did not disappear immediately.

This is important operational evidence: `kubectl delete pod --force` removes the Kubernetes object without waiting for confirmation that the underlying process has actually terminated.

The old PostgreSQL session was explicitly terminated before the artificial account lock was released, ensuring the blocked transaction could not resume and commit.

After that connection was terminated, the database showed:

| State | Result |
| --- | ---: |
| Source balance | 1000.00 |
| Destination balance | 100.00 |
| Idempotency rows for the key | 0 |
| Transfers for the account pair | 0 |
| Outbox messages for the transfer | 0 |

No partial state survived the interrupted transaction.

#### Client retry with the same idempotency key

The client retried the same request using the same `Idempotency-Key`.

The retry returned HTTP 201 and created one completed transfer. Database state became:

- source balance `874.50`;
- destination balance `225.50`;
- one idempotency row;
- one transfer;
- one Outbox message.

The same request was sent again with the same key. The replay returned HTTP 200 and the same Transfer ID. Balances did not change again, and the database still contained exactly one idempotency row, one transfer and one Outbox message.

The Outbox event was published and the Inbox recorded one processed message.

#### Conclusion

This demonstrated the recovery model for an uncertain client outcome: if an attempt does not commit and the client cannot rely on the response, reusing the same Idempotency-Key safely allows the operation to be retried without creating a second transfer.

---

### 9. Health-Check Semantics

`GET /health/live` answers whether the API process is alive. It intentionally does not depend on PostgreSQL or RabbitMQ.

`GET /health/ready` answers whether the API instance can currently serve the transactional request path. It validates PostgreSQL.

RabbitMQ is intentionally not part of API readiness because the Outbox allows the synchronous transfer transaction to commit while broker publication is temporarily unavailable.

---

### 10. Guarantees Demonstrated

The experiments provide concrete evidence that:

- debit and credit are atomic inside PostgreSQL;
- transfer persistence and Outbox persistence share the financial transaction;
- idempotency claim and completion participate in the same transaction;
- unavailable PostgreSQL causes fail-safe transfer failure;
- unavailable RabbitMQ does not lose an already committed transfer event;
- Outbox publication failures are persisted and retried;
- Outbox publication can recover without Worker restart;
- consumer retry is bounded and observable;
- exhausted consumer work is isolated in a DLQ;
- a DLQ message does not block later valid messages;
- broker delivery is treated as at-least-once;
- Inbox processing prevents duplicate persisted consumer effects;
- Kubernetes can replace failed application pods;
- client retry after an uncertain response is safe when the same idempotency key is reused.

---

### 11. What These Tests Do Not Prove

The experiments intentionally avoid overstating the architecture. They do not prove:

- exactly-once delivery by RabbitMQ;
- exactly-once messaging across the distributed system;
- zero downtime during every possible Kubernetes failure;
- multi-node PostgreSQL high availability;
- multi-node RabbitMQ high availability;
- correctness under every possible network partition;
- multi-region disaster recovery;
- production SLO compliance;
- production capacity;
- formal verification of all race conditions.

The local Kubernetes environment uses one PostgreSQL replica and one RabbitMQ replica. Persistence and application recovery were tested, but infrastructure high availability requires a different deployment topology.

---

### 12. Interview Talking Points

A concise explanation is:

> FluxPay keeps the financial transaction in PostgreSQL and treats the broker as an asynchronous dependency. Debit, credit, transfer, idempotency and Outbox state are transactionally coordinated. If RabbitMQ is unavailable, the transfer can still commit and the Outbox retries publication. If PostgreSQL is unavailable, the API becomes NotReady and the transfer fails without partial state. RabbitMQ consumers use bounded retry queues and a DLQ, while a Transactional Inbox makes at-least-once delivery safe for persisted effects. Client retries use an Idempotency-Key, so an uncertain or lost response does not create a second transfer.

A useful distinction is:

> RabbitMQ outage is an asynchronous delivery problem. PostgreSQL outage is a transactional availability problem.

Another useful distinction is:

> The system does not promise exactly-once messaging. It combines at-least-once delivery with idempotent transactional consumption.

---

## Português Brasileiro

### 1. Objetivo

Este documento registra experimentos de falha executados contra o deployment Kubernetes do FluxPay Transfer Engine.

O objetivo não foi afirmar resiliência teórica apenas por inspeção do código. As dependências foram interrompidas deliberadamente e o comportamento resultante foi observado por respostas HTTP, estado do Kubernetes, estado do PostgreSQL, filas RabbitMQ, logs do Worker e registros de Outbox/Inbox.

Esses experimentos são evidências de engenharia da arquitetura implementada em um ambiente kind local. Eles não constituem certificação formal de disponibilidade.

---

### 2. Sistema Testado

O deployment continha:

- duas réplicas da API FluxPay;
- uma réplica do FluxPay Worker;
- um StatefulSet PostgreSQL com uma réplica e armazenamento persistente;
- um StatefulSet RabbitMQ com uma réplica e armazenamento persistente;
- um Job Migrator concluído;
- acesso NodePort à API;
- publicação via Transactional Outbox;
- publisher confirmations no RabbitMQ;
- consumo via Transactional Inbox;
- filas de retry baseadas em TTL;
- dead-letter queue.

O caminho síncrono é:

    Cliente
      |
      v
    FluxPay API
      |
      v
    Transação PostgreSQL
      |
      +--> débito
      +--> crédito
      +--> transferência
      +--> idempotência
      +--> mensagem Outbox

O caminho assíncrono é:

    PostgreSQL Outbox
      |
      v
    Worker / Outbox Processor
      |
      v
    RabbitMQ
      |
      v
    consumer transfer.completed.v1
      |
      v
    Transactional Inbox

---

### 3. Matriz de Falhas

| Cenário de falha | Comportamento observado | Principal garantia demonstrada |
| --- | --- | --- |
| RabbitMQ indisponível | A transferência continuou disponível; o Outbox reteve o evento e publicou depois | Falha do broker não corrompe nem bloqueia a transação financeira síncrona |
| PostgreSQL indisponível | Readiness da API ficou unhealthy; requisição falhou sem estado financeiro parcial | Fail-safe para a dependência transacional crítica |
| Consumer falhando repetidamente | Retries de 5s, 15s e 45s, seguido de DLQ na tentativa 4 | Retry limitado e isolamento de poison message |
| Mensagem válida após DLQ | Nova mensagem foi processada normalmente enquanto a anterior permaneceu isolada | DLQ não bloqueia permanentemente o pipeline |
| Worker perdido durante entrega sem ACK | Entrega voltou ao pipeline e foi concluída pelo Worker substituto | Entrega at-least-once com estado durável no broker |
| Requisição da API interrompida durante transação aberta | Nenhum estado parcial de conta, transferência, idempotência ou Outbox permaneceu | Rollback transacional do PostgreSQL protege o estado financeiro |
| Cliente repete a mesma Idempotency-Key | Uma transferência foi criada; replay posterior retornou a mesma transferência | Retry seguro após resposta incerta/perdida |

---

### 4. Indisponibilidade do RabbitMQ

RabbitMQ foi escalado para zero réplicas enquanto PostgreSQL, API e Worker permaneceram implantados. Uma transferência foi enviada enquanto o broker estava indisponível.

A transferência foi concluída com sucesso. A origem foi debitada uma vez, o destino creditado uma vez, a transferência permaneceu `Completed` e uma mensagem Outbox existia, ainda sem publicação.

O Outbox persistiu a falha e programou retry. Depois que RabbitMQ voltou, o Worker se recuperou sem restart.

A mesma linha do Outbox terminou com `published_at` preenchido, `next_attempt_at` limpo, `dead_lettered_at` nulo e `last_error` limpo.

O `attempt_count` final foi 3 porque ocorreu uma tentativa adicional enquanto RabbitMQ ainda estava inicializando. A terceira tentativa funcionou.

O Inbox do consumer registrou uma mensagem processada e as filas foram drenadas.

#### Conclusão

RabbitMQ não é dependência síncrona do comando de transferência. O Transactional Outbox desacopla a transação financeira durável da disponibilidade imediata do broker.

---

### 5. Indisponibilidade do PostgreSQL

#### 5.1 Liveness e readiness

PostgreSQL foi escalado para zero réplicas.

Antes da falha, `/health/live` e `/health/ready` retornavam HTTP 200. Durante a falha, os dois Pods da API continuaram `Running`, mas ficaram `0/1 Ready`.

Acesso direto ao Pod mostrou:

- `/health/live` -> HTTP 200, `Healthy`;
- `/health/ready` -> HTTP 503, `Unhealthy`;
- PostgreSQL -> `Unhealthy`.

Quando todos os Pods foram removidos dos endpoints prontos do Service, NodePort deixou de ser uma forma válida de testar liveness do processo. O acesso direto ao Pod foi necessário para separar liveness de roteamento do Service.

#### 5.2 Comportamento financeiro fail-safe

Duas contas foram criadas com `1000.00` e `100.00`. PostgreSQL foi interrompido e uma transferência de `125.50` foi enviada diretamente para um Pod vivo da API.

A requisição retornou HTTP 500.

Após restaurar PostgreSQL, continuavam exatamente:

- origem `1000.00`;
- destino `100.00`;
- contagem de transferências inalterada;
- contagem de idempotências inalterada;
- contagem de Outbox inalterada;
- nenhuma linha para a Idempotency-Key usada.

#### Conclusão

PostgreSQL é dependência síncrona crítica. Quando ele está indisponível, FluxPay falha a operação em vez de aceitar uma transação que não pode persistir atomicamente.

---

### 6. Retry do Consumer e Dead-Letter Queue

A política permite quatro tentativas:

| Tentativa que falhou | Delay antes da próxima tentativa |
| ---: | ---: |
| 1 | 5 segundos |
| 2 | 15 segundos |
| 3 | 45 segundos |
| 4 | sem retry; dead-letter |

Mensagens estruturalmente inválidas são tratadas como falhas permanentes e vão para dead-letter sem retries transitórios.

Uma mensagem `transfer.completed.v1` estruturalmente válida foi publicada enquanto PostgreSQL estava indisponível. A validação passou, mas o processamento falhou no Transactional Inbox.

Os logs mostraram:

    Attempt=1 -> retry -> 5 segundos
    Attempt=2 -> retry -> 15 segundos
    Attempt=3 -> retry -> 45 segundos
    Attempt=4 -> esgotado -> dead-letter

A observação das filas confirmou a passagem pelos retries de 5s, 15s e 45s e a chegada à `fluxpay.transfer-completed.dlq`.

Depois que PostgreSQL voltou, não havia registro parcial no Inbox.

Um segundo evento válido foi publicado enquanto o anterior permanecia na DLQ. Ele foi processado na tentativa 1, gravou um Inbox concluído, recebeu ACK e deixou a fila principal vazia.

#### Conclusão

O teste demonstrou retries limitados, delay queues reais, isolamento em DLQ, ausência de claim parcial no Inbox e continuidade do processamento de mensagens saudáveis.

---

### 7. Perda do Worker Durante Entrega em Voo

Foi criado um lock controlado no PostgreSQL sobre `consumer_inbox_messages`. Em seguida, uma transferência real foi executada e seu evento Outbox foi publicado.

O consumer recebeu o evento, mas ficou bloqueado ao persistir o claim do Inbox. RabbitMQ mostrou `messages_ready = 0` e `messages_unacknowledged = 1`.

PostgreSQL mostrou ao mesmo tempo a sessão do lock e uma sessão do Worker aguardando durante o INSERT do Inbox.

O Pod do Worker foi removido à força. Kubernetes criou um Worker substituto com outro UID. Enquanto o lock continuava ativo, RabbitMQ voltou a mostrar uma entrega unacknowledged e PostgreSQL mostrou uma nova sessão do consumer aguardando.

Depois que o lock foi liberado, o Worker substituto concluiu e reconheceu a mensagem.

O Inbox continha exatamente uma linha para o evento e a fila principal voltou a zero.

A tentativa final observada foi 3, porque o lock controlado provocou atividade de retry durante o experimento. Portanto, não descrevemos o resultado como "a primeira entrega foi redelivered exatamente uma vez".

A conclusão correta é que o pipeline recuperou uma entrega em voo após o desaparecimento do Worker original, consistente com semântica at-least-once.

FluxPay não afirma exatamente-once messaging. O modelo é:

    entrega at-least-once
        +
    Transactional Inbox
        +
    consumo idempotente
        =
    um único efeito persistido

---

### 8. Interrupção da API Durante uma Transação Aberta

A inspeção do código confirmou a ordem:

1. abrir transação PostgreSQL;
2. tentar adquirir a `Idempotency-Key`;
3. carregar as duas contas com `ORDER BY id FOR UPDATE`;
4. debitar origem;
5. creditar destino;
6. concluir a transferência;
7. adicionar transferência;
8. adicionar evento ao Outbox;
9. salvar alterações;
10. completar idempotência;
11. commit.

O claim da idempotência participa da mesma transação do movimento financeiro e do Outbox.

Duas contas foram criadas com `1000.00` e `100.00`. Uma transação PostgreSQL controlada bloqueou as duas linhas. A transferência foi enviada diretamente para uma réplica específica da API.

PostgreSQL mostrou essa conexão bloqueada em `SELECT ... ORDER BY id FOR UPDATE`, com o IP do Pod selecionado identificado como cliente bloqueado.

Outra sessão não conseguia enxergar o novo registro de idempotência porque o claim ainda não havia recebido commit.

#### Nuance do force-delete no Kubernetes

O objeto do Pod foi removido usando force delete e Kubernetes criou uma réplica substituta. Entretanto, a sessão PostgreSQL antiga não desapareceu imediatamente.

Isso demonstrou que `kubectl delete pod --force` remove o objeto Kubernetes sem esperar confirmação da terminação real do processo subjacente.

A sessão PostgreSQL antiga foi explicitamente terminada antes de liberar o lock artificial. Assim, a transação bloqueada não poderia continuar e fazer commit.

Depois disso, o banco mostrou:

| Estado | Resultado |
| --- | ---: |
| Saldo origem | 1000.00 |
| Saldo destino | 100.00 |
| Linhas de idempotência da chave | 0 |
| Transferências do par de contas | 0 |
| Mensagens Outbox da transferência | 0 |

Nenhum estado parcial permaneceu.

#### Retry do cliente com a mesma chave

O cliente repetiu a mesma requisição com a mesma `Idempotency-Key`.

O retry retornou HTTP 201 e criou uma transferência. O estado passou para:

- origem `874.50`;
- destino `225.50`;
- uma idempotência;
- uma transferência;
- uma mensagem Outbox.

A mesma requisição foi enviada novamente. O replay retornou HTTP 200 e o mesmo Transfer ID. Os saldos não mudaram novamente e continuaram existindo exatamente uma idempotência, uma transferência e uma mensagem Outbox.

O evento foi publicado e o Inbox registrou uma mensagem processada.

#### Conclusão

O experimento demonstrou o modelo de recuperação para uma resposta incerta: quando uma tentativa não recebe commit e o cliente não pode confiar no resultado, reutilizar a mesma Idempotency-Key permite retry seguro sem criar uma segunda transferência.

---

### 9. Semântica dos Health Checks

`GET /health/live` responde se o processo da API está vivo. Ele não depende de PostgreSQL ou RabbitMQ.

`GET /health/ready` responde se a instância pode atender o caminho transacional. Ele valida PostgreSQL.

RabbitMQ não faz parte do readiness da API propositalmente, porque o Outbox permite concluir a transação síncrona enquanto a publicação está temporariamente indisponível.

---

### 10. Garantias Demonstradas

Os experimentos forneceram evidência concreta de que:

- débito e crédito são atômicos no PostgreSQL;
- transferência e Outbox compartilham a transação financeira;
- claim e conclusão da idempotência participam da mesma transação;
- PostgreSQL indisponível provoca falha fail-safe;
- RabbitMQ indisponível não perde evento de uma transferência já commitada;
- falhas do Outbox são persistidas e retentadas;
- o Outbox pode se recuperar sem restart do Worker;
- retry do consumer é limitado e observável;
- trabalho esgotado é isolado em DLQ;
- mensagem na DLQ não bloqueia mensagens posteriores;
- entrega do broker é tratada como at-least-once;
- Inbox evita efeitos persistidos duplicados;
- Kubernetes substitui Pods de aplicação;
- retry do cliente após resposta incerta é seguro quando a mesma chave é reutilizada.

---

### 11. O Que Estes Testes Não Provam

Os experimentos não provam:

- exactly-once delivery pelo RabbitMQ;
- exactly-once messaging no sistema distribuído;
- zero downtime em qualquer falha possível do Kubernetes;
- alta disponibilidade multi-node do PostgreSQL;
- alta disponibilidade multi-node do RabbitMQ;
- correção em qualquer partição de rede possível;
- disaster recovery multi-região;
- cumprimento de SLO de produção;
- capacidade de produção;
- verificação formal de todas as race conditions.

O ambiente local usa uma réplica de PostgreSQL e uma de RabbitMQ. Persistência e recuperação da aplicação foram testadas; alta disponibilidade de infraestrutura exige outra topologia.

---

### 12. Pontos para Entrevista

Uma explicação concisa é:

> O FluxPay mantém a transação financeira no PostgreSQL e trata o broker como dependência assíncrona. Débito, crédito, transferência, idempotência e Outbox são coordenados transacionalmente. Se RabbitMQ ficar indisponível, a transferência ainda pode receber commit e o Outbox publica depois. Se PostgreSQL ficar indisponível, a API fica NotReady e a transferência falha sem estado parcial. Consumers usam retries limitados e DLQ, enquanto o Transactional Inbox torna segura a entrega at-least-once para efeitos persistidos. Retentativas do cliente usam Idempotency-Key, então uma resposta incerta ou perdida não cria uma segunda transferência.

Uma distinção útil é:

> Falha do RabbitMQ é um problema de entrega assíncrona. Falha do PostgreSQL é um problema de disponibilidade transacional.

Outra distinção útil é:

> O sistema não promete exactly-once messaging. Ele combina entrega at-least-once com consumo transacional idempotente.
