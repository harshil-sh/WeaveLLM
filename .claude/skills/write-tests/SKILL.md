---
name: write-tests
description: Write unit tests for a WeaveLLM class using xUnit, FluentAssertions, NSubstitute
---
Write comprehensive unit tests for the specified class.
Framework: xUnit + FluentAssertions + NSubstitute
Naming: {Method}_{Condition}_{ExpectedOutcome}

Coverage required:
- Happy path
- Each ChainError.Code error path
- Edge cases: null, empty string, cancellation
- Concurrent calls (thread-safety)

Use [Theory] + [InlineData] for multiple input variants.
Assert via result.IsSuccess / result.Error!.Code — never catch exceptions.

Target class: $ARGUMENTS