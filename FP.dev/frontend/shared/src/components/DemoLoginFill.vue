<script setup lang="ts">
import { computed, ref } from 'vue'

const props = defineProps<{ accountType: 'member' | 'admin'; disabled?: boolean }>()
const emit = defineEmits<{ fill: [account: { email: string; password: string }] }>()
const enabled = import.meta.env.DEV && typeof window !== 'undefined' && ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)
const accounts = ref<Array<{ label: string; accountType: string; email: string; password: string }>>([])
const visible = computed(() => accounts.value.filter(account => account.accountType === props.accountType))
const message = ref('')

async function load(event: Event) {
  accounts.value = []
  message.value = ''
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  try {
    if (file.size > 32768) throw new Error('invalid')
    const pack = JSON.parse(await file.text()) as Record<string, unknown>
    if (pack.version !== 1 || typeof pack.database !== 'string' || !/^DoSelectDemo_[0-9a-f]{32}$/i.test(pack.database) || !Array.isArray(pack.accounts) || pack.accounts.length > 20) throw new Error('invalid')
    accounts.value = pack.accounts.map((value: unknown) => {
      if (!value || typeof value !== 'object') throw new Error('invalid')
      const account = value as Record<string, unknown>
      if (typeof account.label !== 'string' || account.label.length > 60 || typeof account.email !== 'string' || !/^(demo-[a-z0-9-]+|member-\d+)@example\.invalid$/.test(account.email) || typeof account.password !== 'string' || account.password.length < 12 || account.password.length > 128 || !['member', 'admin'].includes(String(account.accountType))) throw new Error('invalid')
      return { label: account.label, email: account.email, password: account.password, accountType: String(account.accountType) }
    })
    message.value = '工作包已載入此頁記憶體；請確認目前使用的是工作包對應的隔離 Demo 資料庫。'
  } catch {
    accounts.value = []
    message.value = '無法讀取 Demo 登入工作包，請使用本機匯出的 JSON 檔案。'
  } finally { input.value = '' }
}
</script>

<template>
  <aside
    v-if="enabled"
    class="demo-login-fill"
    aria-label="Demo 帳號快捷填寫"
  >
    <p>Demo 快捷填寫：載入本機登入工作包後，選擇帳號。仍需自行按登入，後台仍需雙重驗證。</p>
    <label>載入 Demo 登入工作包<input
      type="file"
      accept="application/json,.json"
      :disabled="disabled"
      @change="load"
    ></label>
    <p
      v-if="message"
      role="status"
    >
      {{ message }}
    </p>
    <button
      v-for="account in visible"
      :key="account.email"
      type="button"
      :disabled="disabled"
      @click="emit('fill', { email: account.email, password: account.password })"
    >
      填入{{ account.label }}
    </button>
    <button
      v-if="accounts.length"
      type="button"
      @click="accounts = []; message = ''"
    >
      卸載工作包
    </button>
  </aside>
</template>

<style scoped>
.demo-login-fill { border: 1px dashed var(--color-border); padding: .75rem; border-radius: .75rem; }
.demo-login-fill p { font-size: .875rem; }
</style>
