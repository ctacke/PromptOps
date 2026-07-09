#!/usr/bin/env node
// Minimal stand-in for a SonarQube/SonarCloud server's measures Web API, used only by
// scripts/metrics-smoke-test.ps1 to prove SonarMetricCollector end-to-end without requiring a
// real Sonar installation. Not part of the daemon or any plugin.
import http from "node:http";

const port = Number(process.argv[2] ?? 6789);

const server = http.createServer((req, res) => {
  if (req.url?.startsWith("/api/measures/component")) {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({
      component: {
        key: "promptops-smoke-test",
        measures: [
          { metric: "violations", value: "9" },
          { metric: "vulnerabilities", value: "2" },
          { metric: "code_smells", value: "6" },
          { metric: "coverage", value: "78.3" },
          { metric: "duplicated_lines_density", value: "3.1" },
          { metric: "complexity", value: "55" }
        ]
      }
    }));
    return;
  }
  res.writeHead(404);
  res.end();
});

server.listen(port, "0.0.0.0", () => console.log(`fake-sonar-server listening on 0.0.0.0:${port}`));
