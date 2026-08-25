import { spawn } from 'node:child_process'
import { createServer } from 'node:net'
import { mkdir, open, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(scriptDirectory, '..')
const apiProject = join(repositoryRoot, 'src', 'backend', 'DoSelect.Api', 'DoSelect.Api.csproj')
const apiProjectDirectory = dirname(apiProject)
const apiAssembly = join(
  repositoryRoot,
  'src',
  'backend',
  'DoSelect.Api',
  'bin',
  'Debug',
  'net10.0',
  'DoSelect.Api.dll',
)
const contractPath = join(repositoryRoot, 'contracts', 'openapi.v1.json')
const apiOrigin = 'http://127.0.0.1:5126'
const openApiUrl = `${apiOrigin}/openapi/v1.json`
const startupTimeoutMilliseconds = 60_000

await assertPortAvailable(5126)
await run('dotnet', ['build', apiProject, '--nologo', '--verbosity', 'minimal'])

const logDirectory = join(tmpdir(), `doselect-openapi-${process.pid}`)
await mkdir(logDirectory, { recursive: true })
const stdoutPath = join(logDirectory, 'api.stdout.log')
const stderrPath = join(logDirectory, 'api.stderr.log')
const stdout = await open(stdoutPath, 'w')
const stderr = await open(stderrPath, 'w')

const apiProcess = spawn('dotnet', [apiAssembly, '--urls', apiOrigin], {
  cwd: apiProjectDirectory,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    Features__AiEnabled: 'false',
    Features__EmailEnabled: 'false',
    Observability__FileLoggingEnabled: 'false',
    Storage__DataRoot: join(tmpdir(), 'DoSelectOpenApi'),
  },
  stdio: ['ignore', stdout.fd, stderr.fd],
})

try {
  const document = await waitForOpenApiDocument(apiProcess)
  await mkdir(dirname(contractPath), { recursive: true })
  await writeFile(contractPath, `${JSON.stringify(document, null, 2)}\n`, 'utf8')
  console.log(`Exported ${openApiUrl} to ${contractPath}`)
} catch (error) {
  const stdoutText = await readFile(stdoutPath, 'utf8').catch(() => '')
  const stderrText = await readFile(stderrPath, 'utf8').catch(() => '')
  if (stdoutText) {
    console.error('API stdout:')
    console.error(stdoutText)
  }
  if (stderrText) {
    console.error('API stderr:')
    console.error(stderrText)
  }
  throw error
} finally {
  await stop(apiProcess)
  await stdout.close()
  await stderr.close()
  await rm(logDirectory, { recursive: true, force: true })
}

async function assertPortAvailable(port) {
  await new Promise((resolvePromise, reject) => {
    const server = createServer()
    server.unref()
    server.once('error', (error) => {
      reject(new Error(`Port ${port} must be available before exporting OpenAPI.`, { cause: error }))
    })
    server.listen({ host: '127.0.0.1', port, exclusive: true }, () => {
      server.close(resolvePromise)
    })
  })
}

async function waitForOpenApiDocument(childProcess) {
  const deadline = Date.now() + startupTimeoutMilliseconds
  let lastError

  while (Date.now() < deadline) {
    if (childProcess.exitCode !== null) {
      throw new Error(`API exited with code ${childProcess.exitCode} before OpenAPI was available.`)
    }

    try {
      const response = await fetch(openApiUrl)
      if (!response.ok) {
        throw new Error(`OpenAPI endpoint returned HTTP ${response.status}.`)
      }

      const document = await response.json()
      validateOpenApiDocument(document)
      return document
    } catch (error) {
      lastError = error
      await delay(250)
    }
  }

  throw new Error(
    `OpenAPI endpoint did not become available within ${startupTimeoutMilliseconds / 1000} seconds.`,
    { cause: lastError },
  )
}

function validateOpenApiDocument(document) {
  if (!document || typeof document !== 'object' || Array.isArray(document)) {
    throw new Error('OpenAPI endpoint returned a non-object document.')
  }
  if (typeof document.openapi !== 'string' || !document.openapi) {
    throw new Error('OpenAPI document is missing its version.')
  }
  if (!document.paths || typeof document.paths !== 'object' || Array.isArray(document.paths)) {
    throw new Error('OpenAPI document is missing its paths object.')
  }
}

async function run(command, arguments_) {
  await new Promise((resolvePromise, reject) => {
    const childProcess = spawn(command, arguments_, {
      cwd: repositoryRoot,
      env: process.env,
      stdio: 'inherit',
    })
    childProcess.once('error', reject)
    childProcess.once('exit', (code) => {
      if (code === 0) {
        resolvePromise()
      } else {
        reject(new Error(`${command} exited with code ${code}.`))
      }
    })
  })
}

async function stop(childProcess) {
  if (childProcess.exitCode !== null) {
    return
  }

  const exited = new Promise((resolvePromise) => childProcess.once('exit', resolvePromise))
  childProcess.kill()
  await Promise.race([
    exited,
    delay(5_000).then(() => {
      if (childProcess.exitCode === null) {
        childProcess.kill('SIGKILL')
      }
    }),
  ])

  if (childProcess.exitCode === null) {
    await exited
  }
}

function delay(milliseconds) {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds))
}
