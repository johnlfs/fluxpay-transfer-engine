# FluxPay Transfer Engine

## English

FluxPay Transfer Engine is a backend portfolio project focused on reliable financial transfers and the engineering problems involved in processing money safely under concurrency and failure.

### Goals

The project will explore and implement:

- transactional money transfers;
- concurrency control;
- idempotency;
- PostgreSQL persistence;
- transactional outbox;
- RabbitMQ messaging;
- Redis where architecturally justified;
- observability with OpenTelemetry;
- automated testing;
- load and concurrency testing;
- Docker and Docker Compose;
- Kubernetes;
- CI/CD;
- production-oriented AWS architecture.

### Technology

- C# 14
- .NET 10 LTS
- ASP.NET Core
- PostgreSQL
- Redis
- RabbitMQ
- Docker
- xUnit

Additional infrastructure will be introduced gradually as the application requires it.

### Repository Structure

    src/
      FluxPay.Api/
      FluxPay.Application/
      FluxPay.Domain/
      FluxPay.Infrastructure/
      FluxPay.Worker/

    tests/
      FluxPay.UnitTests/
      FluxPay.IntegrationTests/
      FluxPay.ArchitectureTests/

    load-tests/
      k6/

    deploy/
      docker/
      kubernetes/

    docs/
      architecture/
      adr/

    scripts/

### Current Status

Project bootstrap and architectural foundation in progress.

The project intentionally starts small. Infrastructure and architectural patterns will only be introduced when they solve a concrete problem demonstrated during development.

---

## Português Brasileiro

O FluxPay Transfer Engine é um projeto de portfólio backend focado em transferências financeiras confiáveis e nos problemas de engenharia envolvidos em movimentar dinheiro com segurança diante de concorrência e falhas.

### Objetivos

O projeto irá explorar e implementar:

- transferências financeiras transacionais;
- controle de concorrência;
- idempotência;
- persistência com PostgreSQL;
- Transactional Outbox;
- mensageria com RabbitMQ;
- Redis quando houver justificativa arquitetural;
- observabilidade com OpenTelemetry;
- testes automatizados;
- testes de carga e concorrência;
- Docker e Docker Compose;
- Kubernetes;
- CI/CD;
- arquitetura de produção orientada à AWS.

### Tecnologias

- C# 14
- .NET 10 LTS
- ASP.NET Core
- PostgreSQL
- Redis
- RabbitMQ
- Docker
- xUnit

Infraestruturas adicionais serão introduzidas gradualmente conforme a aplicação realmente precisar delas.

### Estrutura do Repositório

    src/
      FluxPay.Api/
      FluxPay.Application/
      FluxPay.Domain/
      FluxPay.Infrastructure/
      FluxPay.Worker/

    tests/
      FluxPay.UnitTests/
      FluxPay.IntegrationTests/
      FluxPay.ArchitectureTests/

    load-tests/
      k6/

    deploy/
      docker/
      kubernetes/

    docs/
      architecture/
      adr/

    scripts/

### Status Atual

O bootstrap do projeto e a fundação arquitetural estão em andamento.

O projeto começa intencionalmente pequeno. Infraestruturas e padrões arquiteturais somente serão introduzidos quando resolverem um problema concreto demonstrado durante o desenvolvimento.
