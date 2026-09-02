import { execFileSync } from 'node:child_process'

/**
 * Direct SQL assertions against the test's own isolated DoSelectE2E_* database (never the shared
 * DoSelectDb — see WP-H03/H04's own database-isolation notes). Shells out to sqlcmd rather than
 * adding a new SQL client npm dependency: sqlcmd is already the tool this whole test suite's setup
 * scripts use, and WP-A's own Gate rules require stopping to ask before adding a new package.
 *
 * `ConnectionStrings__DefaultConnection` is already set in this same process's env by whatever
 * started the isolated E2E database (the same env var the backend webServer reads), so the target
 * database is derived from it rather than passed separately — this can never accidentally point at
 * a different (e.g. shared) database than the one the test's own backend is using.
 */
function parseConnectionString(connectionString: string): { server: string, database: string } {
  const parts = Object.fromEntries(
    connectionString.split(';').filter(Boolean).map((part) => {
      const [key, ...rest] = part.split('=')
      return [key!.trim().toLowerCase(), rest.join('=').trim()]
    }),
  )
  const server = parts['server'] ?? parts['data source']
  const database = parts['database'] ?? parts['initial catalog']
  if (!server || !database) {
    throw new Error(`Could not parse a server/database out of the connection string: ${connectionString}`)
  }
  return { server, database }
}

function target(): { server: string, database: string } {
  const connectionString = process.env.ConnectionStrings__DefaultConnection
  if (!connectionString) {
    throw new Error('ConnectionStrings__DefaultConnection must be set to run a SQL assertion.')
  }
  const { server, database } = parseConnectionString(connectionString)
  if (!/^DoSelectE2E(_[0-9a-f]{32})?$/i.test(database)) {
    throw new Error(`Refusing to run a SQL assertion against a non-isolated database '${database}'.`)
  }
  return { server, database }
}

/** Runs a scalar query and returns its single trimmed result column as a string. */
export function sqlScalar(query: string): string {
  const { server, database } = target()
  const output = execFileSync('sqlcmd', [
    '-S', server,
    '-d', database,
    '-C',
    '-h', '-1',
    '-W',
    '-Q', `SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; ${query}`,
  ], { encoding: 'utf-8' })
  const lines = output.split('\n').map(line => line.trim()).filter(Boolean)
  const last = lines.at(-1)
  if (last === undefined) {
    throw new Error(`SQL scalar query returned no rows: ${query}`)
  }
  return last
}

export function sqlExec(statement: string): void {
  const { server, database } = target()
  execFileSync('sqlcmd', [
    '-S', server,
    '-d', database,
    '-C',
    '-Q', `SET QUOTED_IDENTIFIER ON; ${statement}`,
  ], { encoding: 'utf-8' })
}
