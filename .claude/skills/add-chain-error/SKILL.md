---
name: add-chain-error
description: Add a new ChainError factory method to ChainError.cs
---
Add a new ChainError factory method to src/WeaveLLM.Core/ChainError.cs
- Code must be a PascalCase constant
- Message must be actionable — tell the developer what to do, not just what happened
- Bad:  "Request failed"
- Good: "OpenAI returned 429. Add RateLimitingMiddleware or reduce request frequency."

Error to add: $ARGUMENTS