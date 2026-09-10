// Pulls the live OpenAPI document from the locally running API (Development-only route)
// and writes it to the shared contracts folder. Run `dotnet run --project src/backend/DoSelect.Api`
// (or scripts/start-all.ps1) first. See 03-架構/OpenAPI與前端Client流程 for the full pipeline.
import { writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const apiBaseUrl = process.env.VITE_API_BASE_URL ?? 'http://localhost:5126'
const outputPath = fileURLToPath(new URL('../../../contracts/openapi.v1.json', import.meta.url))

const response = await fetch(`${apiBaseUrl}/openapi/v1.json`)
if (!response.ok) {
  throw new Error(`Failed to fetch OpenAPI document: ${response.status} ${response.statusText}`)
}

const body = await response.text()
const document = JSON.parse(body) // fail fast if the API returned something unexpected
const liveServerUrl = document.servers?.[0]?.url
// The server advertises the address it was started with. Normalize that environment detail so
// a developer using 127.0.0.1 cannot make the committed contract drift from CI's localhost URL.
const normalizedBody = typeof liveServerUrl === 'string'
  ? body.replace(JSON.stringify(liveServerUrl), JSON.stringify('http://localhost:5126/'))
  : body
await writeFile(outputPath, normalizedBody.endsWith('\n') ? normalizedBody : `${normalizedBody}\n`, 'utf8')
console.log(`Wrote ${outputPath}`)
