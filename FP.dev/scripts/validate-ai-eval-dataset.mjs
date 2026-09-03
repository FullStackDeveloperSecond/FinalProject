import { readFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(scriptDirectory, '..')
const evalDirectory = resolve(projectRoot, 'evals', 'ai', 'v1')

const [manifest, fixtureDocument, datasetText] = await Promise.all([
  readJson(resolve(evalDirectory, 'manifest.json')),
  readJson(resolve(evalDirectory, 'context-fixtures.v1.json')),
  readFile(resolve(evalDirectory, 'dataset.zh-TW.v1.jsonl'), 'utf8'),
])

const errors = []
const lines = datasetText.trimEnd().split(/\r?\n/)
const cases = lines.map((line, index) => {
  try {
    return JSON.parse(line)
  } catch (error) {
    errors.push(`line ${index + 1}: invalid JSON (${error.message})`)
    return null
  }
}).filter(Boolean)

const fixtureIds = new Set(fixtureDocument.fixtures.map((fixture) => fixture.fixtureId))
const catalogCandidates = new Map(
  fixtureDocument.fixtures
    .flatMap((fixture) => fixture.candidates ?? [])
    .map((candidate) => [candidate.id, candidate]),
)
const seenIds = new Set()
const seenMessages = new Set()
const groupCounts = new Map()
const splitCounts = new Map()
const groupSplitCounts = new Map()

for (const item of cases) {
  validateRequiredShape(item, errors)

  if (seenIds.has(item.caseId)) errors.push(`${item.caseId}: duplicate caseId`)
  seenIds.add(item.caseId)
  if (seenMessages.has(item.input.message)) errors.push(`${item.caseId}: duplicate input message`)
  seenMessages.add(item.input.message)

  increment(groupCounts, item.primaryGroup)
  increment(splitCounts, item.split)
  increment(groupSplitCounts, `${item.primaryGroup}:${item.split}`)

  for (const fixtureId of item.prerequisites.fixtureIds) {
    if (!fixtureIds.has(fixtureId)) errors.push(`${item.caseId}: unknown fixture ${fixtureId}`)
  }
  for (const sourceId of item.expected.citations.sourceIds) {
    if (!fixtureIds.has(sourceId)) errors.push(`${item.caseId}: unknown citation source ${sourceId}`)
  }
  for (const candidateId of item.expected.allowedCandidateIds) {
    const candidate = catalogCandidates.get(candidateId)
    if (!candidate) {
      errors.push(`${item.caseId}: unknown candidate ${candidateId}`)
      continue
    }
    const budgetMax = item.expected.intentFields['budget.maxTwd']
    if (item.expected.outcome === 'recommend' && budgetMax !== null && candidate.price > budgetMax) {
      errors.push(`${item.caseId}: candidate ${candidateId} exceeds budget ${budgetMax}`)
    }
  }

  const clarification = item.expected.clarification
  if (clarification.concepts.length > 2 || clarification.maximumQuestions > 2) {
    errors.push(`${item.caseId}: clarification exceeds two questions`)
  }
  if (clarification.required && item.expected.outcome !== 'clarify') {
    errors.push(`${item.caseId}: required clarification must use clarify outcome`)
  }
  if (item.tags.includes('core_clarification') && !item.expected.hardFailRules.includes('missing_core_clarification')) {
    errors.push(`${item.caseId}: core clarification is not a hard fail`)
  }
  if (item.expected.modelCall === 'forbidden' && item.expected.tools.allowed.length > 0) {
    errors.push(`${item.caseId}: model-forbidden case cannot allow tools`)
  }
  if (item.annotation.primaryAnnotator === item.annotation.reviewer) {
    errors.push(`${item.caseId}: annotator and reviewer must differ`)
  }
}

compareCount('case total', cases.length, manifest.caseCount, errors)
for (const [group, expected] of Object.entries(manifest.primaryGroups)) {
  compareCount(`group ${group}`, groupCounts.get(group) ?? 0, expected, errors)
}
for (const [split, expected] of Object.entries(manifest.splits)) {
  compareCount(`split ${split}`, splitCounts.get(split) ?? 0, expected, errors)
}

const groupPlans = {
  'SEARCH-NOVICE': [18, 9, 3],
  'SEARCH-CREATOR': [12, 6, 2],
  'SEARCH-COMPATIBILITY': [12, 6, 2],
  'SEARCH-NO-RESULT-DEGRADED': [9, 5, 1],
  'SUPPORT-POLICY': [9, 4, 2],
  'SUPPORT-SECURITY': [12, 6, 2],
}
for (const [group, [development, release, challenge]] of Object.entries(groupPlans)) {
  compareCount(`${group}:development`, groupSplitCounts.get(`${group}:development`) ?? 0, development, errors)
  compareCount(`${group}:release`, groupSplitCounts.get(`${group}:release`) ?? 0, release, errors)
  compareCount(`${group}:challenge`, groupSplitCounts.get(`${group}:challenge`) ?? 0, challenge, errors)
}

const serializedDataset = JSON.stringify(cases)
const forbiddenPatterns = [
  { name: 'real-looking email', pattern: /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/i },
  { name: 'Taiwan mobile number', pattern: /(^|\D)09\d{8}(\D|$)/ },
  { name: 'OpenAI-style secret', pattern: /\bsk-[A-Za-z0-9_-]{20,}\b/ },
  { name: 'Brevo-style secret', pattern: /\bxkeysib-[A-Za-z0-9_-]{20,}\b/i },
  { name: 'private key', pattern: /BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY/ },
]
for (const { name, pattern } of forbiddenPatterns) {
  if (pattern.test(serializedDataset)) errors.push(`dataset contains ${name}`)
}

if (manifest.containsProductionData !== false || manifest.containsRealPersonalData !== false) {
  errors.push('manifest must explicitly declare no production or real personal data')
}

if (errors.length > 0) {
  console.error(`AI evaluation dataset validation failed with ${errors.length} error(s):`)
  for (const error of errors) console.error(`- ${error}`)
  process.exitCode = 1
} else {
  console.log(`Validated ${cases.length} AI evaluation cases.`)
  console.log(`Groups: ${formatCounts(groupCounts)}`)
  console.log(`Splits: ${formatCounts(splitCounts)}`)
  console.log('Privacy scan: no real-looking email, Taiwan mobile number, API secret, or private key detected.')
  console.log('Live OpenAI calls: not performed.')
}

async function readJson(path) {
  return JSON.parse(await readFile(path, 'utf8'))
}

function validateRequiredShape(item, validationErrors) {
  const requiredStrings = ['caseId', 'datasetVersion', 'language', 'split', 'primaryGroup', 'feature']
  for (const property of requiredStrings) {
    if (typeof item[property] !== 'string' || item[property].length === 0) {
      validationErrors.push(`${item.caseId ?? '<unknown>'}: missing ${property}`)
    }
  }
  if (item.datasetVersion !== 'zh-TW-v1.0.2-draft') validationErrors.push(`${item.caseId}: wrong dataset version`)
  if (item.language !== 'zh-TW') validationErrors.push(`${item.caseId}: wrong language`)
  if (!/^[A-Z-]+-\d{3}$/.test(item.caseId)) validationErrors.push(`${item.caseId}: invalid caseId format`)
  if (typeof item.input?.message !== 'string' || item.input.message.length > 2000) validationErrors.push(`${item.caseId}: invalid message`)
  if (!Array.isArray(item.prerequisites?.fixtureIds) || item.prerequisites.fixtureIds.length === 0) validationErrors.push(`${item.caseId}: fixtureIds required`)
  if (!Array.isArray(item.expected?.answer?.requiredPoints) || item.expected.answer.requiredPoints.length === 0) validationErrors.push(`${item.caseId}: answer points required`)
  if (!Array.isArray(item.evidence?.sourceRefs) || item.evidence.sourceRefs.length === 0) validationErrors.push(`${item.caseId}: sourceRefs required`)
  if (item.caseId === 'SUPPORT-POLICY-015' &&
      !item.evidence?.sourceRefs?.includes('02-領域需求/90-驗收規格/AI搜尋與客服驗收規格#UC-AI-SUPPORT-03｜禁止 AI 寫入商業資料')) {
    validationErrors.push(`${item.caseId}: AI no-write evidence source required`)
  }
  if (!item.annotation?.primaryAnnotator || !item.annotation?.reviewer) validationErrors.push(`${item.caseId}: annotation responsibility required`)
}

function increment(map, key) {
  map.set(key, (map.get(key) ?? 0) + 1)
}

function compareCount(label, actual, expected, validationErrors) {
  if (actual !== expected) validationErrors.push(`${label}: expected ${expected}, got ${actual}`)
}

function formatCounts(map) {
  return [...map.entries()].map(([key, value]) => `${key}=${value}`).join(', ')
}
