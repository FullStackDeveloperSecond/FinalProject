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
JSON.parse(body) // fail fast if the API returned something unexpected
await writeFile(outputPath, body, 'utf8')
console.log(`Wrote ${outputPath}`)
