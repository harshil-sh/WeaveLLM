---
name: new-middleware
description: Add a new middleware to the WeaveLLM pipeline
---
Create a new middleware in src/WeaveLLM.Core/Middleware/
- Implements IChainMiddleware<TInput, TOutput>
- Returns ChainResult<TOutput> — never throw
- CancellationToken on all async methods
- Thread-safe
- XML doc comments on all public members

Behaviour: $ARGUMENTS