import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { cases, datasetVersion, fixtures } from '../evals/ai/v1/cases-source.mjs'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(scriptDirectory, '..')
const evalDirectory = resolve(projectRoot, 'evals', 'ai', 'v1')
const datasetPath = resolve(evalDirectory, 'dataset.zh-TW.v1.jsonl')
const fixturePath = resolve(evalDirectory, 'context-fixtures.v1.json')
const checkOnly = process.argv.includes('--check')

const datasetContent = `${cases.map((item) => JSON.stringify(item)).join('\n')}\n`
const fixtureContent = `${JSON.stringify(fixtures, null, 2)}\n`

if (checkOnly) {
  const [existingDataset, existingFixtures] = await Promise.all([
    readFile(datasetPath, 'utf8'),
    readFile(fixturePath, 'utf8'),
  ])

  if (existingDataset !== datasetContent || existingFixtures !== fixtureContent) {
    console.error('AI evaluation artifacts are stale. Run: node scripts/build-ai-eval-dataset.mjs')
    process.exitCode = 1
  } else {
    console.log(`AI evaluation artifacts match ${datasetVersion} source (${cases.length} cases).`)
  }
} else {
  await mkdir(evalDirectory, { recursive: true })
  await Promise.all([
    writeFile(datasetPath, datasetContent, 'utf8'),
    writeFile(fixturePath, fixtureContent, 'utf8'),
  ])
  console.log(`Generated ${cases.length} AI evaluation cases for ${datasetVersion}.`)
}
