---
name: new-graph-node
description: Add a new node to the WeaveLLM graph state machine
---
Create a new graph node in src/WeaveLLM.Core/Graph/Nodes/
- Implements INode<TInput, TOutput>
- Wraps an IChain<TInput, TOutput> as its execution logic
- All errors as ChainResult.Failure — never throw
- CancellationToken on all async methods
- XML doc comments on all public members
- Thread-safe

Node to create: $ARGUMENTS