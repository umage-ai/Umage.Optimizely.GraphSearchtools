# GraphSearchtools

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Graph search tooling for Optimizely CMS 12 and CMS 13. A CMS-shell-integrated admin
area that exposes pinned-result curation, synonym management, a query playground,
and relevancy-tuning tools — all built on Optimizely Graph.

Distributed as the NuGet package `UmageAI.Optimizely.GraphSearchTools`.

## Status

Pre-release. Phase 0 (framework fork) is in progress. Tools land per the
phased plan in `docs/implementation-plan.md`.

## Install

```bash
dotnet add package UmageAI.Optimizely.GraphSearchTools
```

```csharp
// Startup.cs
services.AddGraphSearchtools(o => Configuration.GetSection("CodeArt:GraphSearchtools").Bind(o));
app.UseGraphSearchtools();
```

## Configuration

```json
{
  "Optimizely": {
    "ContentGraph": {
      "GatewayAddress": "https://cg.optimizely.com",
      "AppKey": "...",
      "Secret": "...",
      "SingleKey": "..."
    }
  },
  "CodeArt": {
    "GraphSearchtools": {
      "AuthorizedRoles": ["WebAdmins", "Administrators"],
      "Features": { "Overview": true }
    }
  }
}
```

The add-on reuses the host's `Optimizely:ContentGraph` credentials by default; the
`CodeArt:GraphSearchtools:Graph` block can override them per-environment.

## License

MIT — see [LICENSE](LICENSE).

Powered by [umage.ai](https://umage.ai).
