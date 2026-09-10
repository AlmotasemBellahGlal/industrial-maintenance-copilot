# ADR-001: Adopt Clean Architecture

- **Status:** Accepted
- **Date:** 2026-09-10

## Context

The Industrial Maintenance Copilot depends on several external technologies that may change independently from the business logic, including:

- Hosted and local LLM providers
- Embedding providers
- Vector stores
- Relational databases
- Background job infrastructure
- HTTP and real-time communication frameworks

The assessment also requires the Domain and Application layers to remain independent of LLM SDKs, vector-store SDKs, and web frameworks.

The system must support replacing an LLM provider, embedding model, or vector store through configuration and an adapter without modifying the core business logic.

The D5 Industrial Maintenance domain also contains safety-critical business rules. In particular, mandatory safety prerequisites and supervisor approval must be enforced by deterministic application/domain logic rather than relying on model-generated text.

## Decision

The system will use Clean Architecture with the following projects:

- `IndustrialCopilot.Domain`
- `IndustrialCopilot.Application`
- `IndustrialCopilot.Infrastructure`
- `IndustrialCopilot.Api`
- `IndustrialCopilot.Worker`

### Domain

Contains enterprise and domain rules such as maintenance workflow concepts, work-order state, approval rules, and safety invariants.

It has no project dependencies.

### Application

Contains use cases, application contracts, orchestration abstractions, and interfaces for external capabilities.

It depends only on `IndustrialCopilot.Domain`.

External capabilities will be accessed through abstractions such as LLM providers, retrieval services, repositories, job queues, tracing, and progress publishing.

### Infrastructure

Contains adapters and implementations for external technologies such as databases, vector stores, LLM providers, embedding providers, queues, and observability infrastructure.

It depends on `IndustrialCopilot.Application` and `IndustrialCopilot.Domain`.

### API

Acts as the HTTP entry point to the system.

It handles transport concerns such as authentication, authorization, request validation, HTTP endpoints, streaming connections, and dependency-injection composition.

It depends on `IndustrialCopilot.Application` and `IndustrialCopilot.Infrastructure`.

### Worker

Executes asynchronous and long-running jobs outside the HTTP request lifecycle.

It depends on `IndustrialCopilot.Application` and `IndustrialCopilot.Infrastructure`.

This separation is particularly important for the T7 Async Long-Running Jobs requirement.

## Dependency Rule

Dependencies must point toward the business core:

```text
API ──────────────► Application ─────► Domain
                         ▲
                         │
Infrastructure ──────────┘

Worker ───────────► Application
   │                     ▲
   └──────────────► Infrastructure
```

The following dependencies are prohibited:

```text
Domain         ─X─► Application
Domain         ─X─► Infrastructure
Domain         ─X─► API
Application    ─X─► Infrastructure
Application    ─X─► API
```

The Domain and Application layers must not reference concrete LLM, vector-store, queue, database, or web-framework SDKs.

## Alternatives Considered

### Traditional Layered Architecture

A conventional API → Services → Data Access architecture would be simpler initially.

It was rejected because application services could become coupled to infrastructure concerns such as the selected LLM provider, vector database, or queue implementation, making provider replacement and isolated testing harder.

### Vertical Slice Architecture

Vertical Slice Architecture could provide strong feature locality and reduce coupling between unrelated use cases.

It was not selected as the primary architectural style because this system has several shared external capabilities and explicit assessment requirements around provider abstraction and infrastructure independence.

Vertical-slice organization may still be used inside the Application layer where useful.

### Microservices

Separating ingestion, retrieval, orchestration, and background processing into independent services could improve independent scaling and fault isolation.

It was rejected for the MVP because the additional deployment, networking, observability, consistency, and operational complexity is not justified within the assessment scope.

The MVP will use a modular application with a separately hosted background Worker.

## Consequences

### Positive

- Business rules remain independent of external AI and storage technologies.
- LLM and vector-store providers can be replaced through adapters and configuration.
- Domain and application logic can be unit tested without real external services.
- Safety rules can be enforced independently from LLM output.
- HTTP request handling is separated from long-running workflow execution.
- Infrastructure choices can evolve without rewriting core use cases.

### Negative

- More projects, interfaces, and dependency-injection configuration are required.
- Simple features may require more files than in a traditional layered application.
- Developers must actively maintain layer boundaries.
- Incorrectly designed abstractions could add unnecessary complexity.

## Enforcement

The architecture will be enforced through:

- Project references that follow the dependency rule.
- Dependency injection for external implementations.
- Interfaces at the application boundary.
- Unit tests with external services stubbed or mocked.
- Architecture tests or CI checks where practical.
- Code review of dependency changes.
