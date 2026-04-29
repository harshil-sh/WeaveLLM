---
name: xml-docs
description: Add XML doc comments to all public members in a file
---
Add XML doc comments to all public members in the specified file.
- Methods: document each parameter, return value, and both ChainResult success/failure cases
- Interfaces: document the contract, not the implementation
- Properties: one-line summary only
- Do not add comments to private or internal members
- Never write "Gets or sets" — describe what the member represents

Target file: $ARGUMENTS