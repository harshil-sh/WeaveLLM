# Chat With Your Docs

Chat with local docs in ~20 lines.

## Getting started

1. Set your OpenAI API key in `appsettings.json`.
2. Drop `.txt` or `.md` files into the `docs/` folder.
3. Run the sample:

```bash
dotnet run --project samples/ChatWithDocs
```

The pipeline indexes every file on startup, then enters a REPL where you can ask questions about your documents. Press **Ctrl+C** to quit.
