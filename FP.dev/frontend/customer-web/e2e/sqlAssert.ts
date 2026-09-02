import { execFileSync } from 'node:child_process'

interface SqlTarget {
  server: string
  database: string
  // Windows dev boxes point ConnectionStrings__DefaultConnection at a Trusted_Connection, so
  // sqlcmd defaults to Windows-integrated auth with no -U/-P needed. CI's SQL Server runs in a
  // Linux container with no Windows auth, so its connection string carries a User Id/Password
  // instead — sqlcmd must be given them explicitly there, or it tries (and fails) SSPI/Kerberos.
  user?: string
  password?: string
}

function parseConnectionString(connectionString: string): SqlTarget {
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
  const user = parts['user id'] ?? parts['uid'] ?? parts['user']
  const password = parts['password'] ?? parts['pwd']
  return { server, database, user, password }
}

function target(): SqlTarget {
  const connectionString = process.env.ConnectionStrings__DefaultConnection
  if (!connectionString) {
    throw new Error('ConnectionStrings__DefaultConnection must be set to run a SQL assertion.')
  }
  const parsed = parseConnectionString(connectionString)
  if (!/^DoSelectE2E(_[0-9a-f]{32})?$/i.test(parsed.database)) {
    throw new Error(`Refusing to run a SQL assertion against a non-isolated database '${parsed.database}'.`)
  }
  return parsed
}

function authArgs({ user, password }: SqlTarget): string[] {
  return user && password ? ['-U', user, '-P', password] : []
}

export function sqlScalar(query: string): string {
  const sql = target()
  const output = execFileSync('sqlcmd', [
    '-S', sql.server, '-d', sql.database, '-C', ...authArgs(sql), '-h', '-1', '-W',
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
  const sql = target()
  execFileSync('sqlcmd', [
    '-S', sql.server, '-d', sql.database, '-C', ...authArgs(sql), '-b',
    '-Q', `SET QUOTED_IDENTIFIER ON; ${statement}`,
  ], { encoding: 'utf-8' })
}
